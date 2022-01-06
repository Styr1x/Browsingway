﻿using Browsingway.Common;
using Browsingway.Renderer.RenderHandlers;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Browsingway.Renderer;

internal static class Program
{
	private static string _cefAssemblyDir = null!;
	private static string _dalamudAssemblyDir = null!;
	private static int ParentProcessID;

	private static Thread? _parentWatchThread;
	private static EventWaitHandle? _waitHandle;

	private static IpcBuffer<DownstreamIpcRequest, UpstreamIpcRequest?> _ipcBuffer = null!;

	private static readonly Dictionary<Guid, Inlay> _inlays = new();

	private static bool _isShuttingDown;
	private static readonly object _lockIpc = new();

	private static void Main(string[] rawArgs)
	{
		Console.WriteLine("Render process running.");
		RenderProcessArguments args = RenderProcessArguments.Deserialize(rawArgs[0]);

		// Need to pull these out before Run() so the resolver can access.
		_cefAssemblyDir = args.CefAssemblyDir;
		_dalamudAssemblyDir = args.DalamudAssemblyDir;

		ParentProcessID = args.ParentPid;
		AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolver;

		Run(args);
	}

	// Main process logic. Seperated to ensure assembly resolution is configured.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Run(RenderProcessArguments args)
	{
		_waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, args.KeepAliveHandleName);

		// Boot up a thread to make sure we shut down if parent dies
		_parentWatchThread = new Thread(WatchParentStatus);
		_parentWatchThread.Start(args.ParentPid);

#if DEBUG
		AppDomain.CurrentDomain.FirstChanceException += (_, e) => Console.Error.WriteLine(e.Exception.ToString());
#endif

		bool dxRunning = DxHandler.Initialise(args.DxgiAdapterLuid);
		CefHandler.Initialise(_cefAssemblyDir, args.CefCacheDir, ParentProcessID);

		_ipcBuffer = new IpcBuffer<DownstreamIpcRequest, UpstreamIpcRequest?>(args.IpcChannelName, HandleIpcRequest);

		Console.WriteLine("Notifying on ready state.");

		// We always support bitmap buffer transport
		FrameTransportMode availableTransports = FrameTransportMode.BitmapBuffer;
		if (dxRunning) { availableTransports |= FrameTransportMode.SharedTexture; }

		_ipcBuffer.RemoteRequest<object>(new ReadyNotificationRequest { AvailableTransports = availableTransports });

		Console.WriteLine("Waiting...");

		_waitHandle.WaitOne();
		Console.WriteLine("Render process shutting down.");
		lock (_lockIpc)
		{
			_isShuttingDown = true;
		}

		DxHandler.Shutdown();
		CefHandler.Shutdown();
	}

	private static void WatchParentStatus(object? pid)
	{
		Console.WriteLine($"Watching parent PID {pid}");
		Process process = Process.GetProcessById((int)(pid ?? 0));
		while (true)
		{
			if (_waitHandle?.WaitOne(10) == true)
			{
				// we are shutting down because wait handle was set, so quit the monitoring thread
				return;
			}

			// process terminated but wait handle was not set, so we kill ourself
			if (process.HasExited)
			{
				Process self = Process.GetCurrentProcess();
				self.WaitForExit(1000);
				try { self.Kill(); }
				catch (InvalidOperationException) { }
			}
		}
	}

	private static object? HandleIpcRequest(DownstreamIpcRequest? request)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
			{
				return null;
			}

			switch (request)
			{
				case NewInlayRequest newInlayRequest: return OnNewInlayRequest(newInlayRequest);

				case ResizeInlayRequest resizeInlayRequest:
					{
						Inlay inlay = _inlays[resizeInlayRequest.Guid];

						inlay.Resize(new Size(resizeInlayRequest.Width, resizeInlayRequest.Height));

						return BuildRenderHandlerResponse(inlay.RenderHandler);
					}

				case NavigateInlayRequest navigateInlayRequest:
					{
						Inlay inlay = _inlays[navigateInlayRequest.Guid];
						inlay.Navigate(navigateInlayRequest.Url);
						return null;
					}

				case ZoomInlayRequest zoomInlayRequest:
					{
						Inlay inlay = _inlays[zoomInlayRequest.Guid];
						inlay.Zoom(zoomInlayRequest.Zoom);
						return null;
					}

				case DebugInlayRequest debugInlayRequest:
					{
						Inlay inlay = _inlays[debugInlayRequest.Guid];
						inlay.Debug();
						return null;
					}

				case RemoveInlayRequest removeInlayRequest:
					{
						Inlay inlay = _inlays[removeInlayRequest.Guid];
						_inlays.Remove(removeInlayRequest.Guid);
						inlay.Dispose();
						return null;
					}

				case MouseEventRequest mouseMoveRequest:
					{
						Inlay inlay = _inlays[mouseMoveRequest.Guid];
						inlay.HandleMouseEvent(mouseMoveRequest);
						return null;
					}

				case KeyEventRequest keyEventRequest:
					{
						Inlay inlay = _inlays[keyEventRequest.Guid];
						inlay.HandleKeyEvent(keyEventRequest);
						return null;
					}

				default:
					throw new Exception($"Unknown IPC request type {request?.GetType().Name} received.");
			}
		}
	}

	private static object OnNewInlayRequest(NewInlayRequest request)
	{
		Size size = new(request.Width, request.Height);
		BaseRenderHandler renderHandler = request.FrameTransportMode switch
		{
			FrameTransportMode.SharedTexture => new TextureRenderHandler(size),
			FrameTransportMode.BitmapBuffer => new BitmapBufferRenderHandler(size),
			_ => throw new Exception($"Unhandled frame transport mode {request.FrameTransportMode}")
		};

		if (_inlays.ContainsKey(request.Guid))
		{
			_inlays[request.Guid].Dispose();
			_inlays.Remove(request.Guid);
		}

		Inlay inlay = new(request.Url, request.Zoom, request.Framerate, renderHandler);
		inlay.Initialise();
		_inlays.Add(request.Guid, inlay);

		renderHandler.CursorChanged += (_, cursor) =>
		{
			_ipcBuffer.RemoteRequest<object>(new SetCursorRequest { Guid = request.Guid, Cursor = cursor });
		};

		return BuildRenderHandlerResponse(renderHandler);
	}

	private static object BuildRenderHandlerResponse(BaseRenderHandler renderHandler)
	{
		return renderHandler switch
		{
			TextureRenderHandler textureRenderHandler => new TextureHandleResponse { TextureHandle = textureRenderHandler.SharedTextureHandle },
			BitmapBufferRenderHandler bitmapBufferRenderHandler => new BitmapBufferResponse { BitmapBufferName = bitmapBufferRenderHandler.BitmapBufferName!, FrameInfoBufferName = bitmapBufferRenderHandler.FrameInfoBufferName },
			_ => throw new Exception($"Unhandled render handler type {renderHandler.GetType().Name}")
		};
	}

	private static Assembly? CustomAssemblyResolver(object? sender, ResolveEventArgs args)
	{
		string assemblyName = args.Name.Split(new[] { ',' }, 2)[0] + ".dll";

		string? assemblyPath = null;
		if (assemblyName.StartsWith("CefSharp"))
		{
			assemblyPath = Path.Combine(_cefAssemblyDir, assemblyName);
		}
		else if (assemblyName.StartsWith("SharpDX"))
		{
			assemblyPath = Path.Combine(_dalamudAssemblyDir, assemblyName);
		}

		if (assemblyPath == null) { return null; }

		if (!File.Exists(assemblyPath))
		{
			Console.Error.WriteLine($"Could not find assembly `{assemblyName}` at search path `{assemblyPath}`");
			return null;
		}

		return Assembly.LoadFile(assemblyPath);
	}
}