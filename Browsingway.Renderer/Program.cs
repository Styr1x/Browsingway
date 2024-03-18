using Browsingway.Common;
using Browsingway.Common.Ipc;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Browsingway.Renderer;

internal static class Program
{
	private static string _cefAssemblyDir = null!;
	private static string _dalamudAssemblyDir = null!;

	private static Thread? _parentWatchThread;
	private static EventWaitHandle? _waitHandle;

	private static RendererRpc _rpc = null!;

	private static readonly Dictionary<Guid, Overlay> _overlays = new();

	private static bool _isShuttingDown;
	private static readonly object _lockIpc = new();

	private static void Main(string[] rawArgs)
	{
		Console.WriteLine("Render process running.");

		// Deserialize the arguments
		var args = RenderParamsSerializer.Deserialize(rawArgs[0]);

		// Need to pull these out before Run() so the resolver can access.
		_cefAssemblyDir = args.CefAssemblyDir;
		_dalamudAssemblyDir = args.DalamudAssemblyDir;

		AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolver;

		Run(args);
	}

	// Main process logic. Seperated to ensure assembly resolution is configured.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Run(RenderParams args)
	{
		_waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, args.KeepAliveHandleName);

		// Boot up a thread to make sure we shut down if parent dies
		_parentWatchThread = new Thread(WatchParentStatus);
		_parentWatchThread.Start(args.ParentPid);

		AppDomain.CurrentDomain.FirstChanceException += (_, e) => Console.Error.WriteLine(e.Exception.ToString());

		bool dxRunning = DxHandler.Initialise(args.DxgiAdapterLuid);
		CefHandler.Initialise(_cefAssemblyDir, args.CefCacheDir, args.ParentPid);

		InitializeIpc(args.IpcChannelName);

		Console.WriteLine("Notifying on ready state.");

		// Notify plugin that render process is running
		_ = _rpc.RendererReady(dxRunning);

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

	// TODO: move RPC stuff away
	private static void InitializeIpc(string channelName)
	{
		_rpc = new RendererRpc(channelName);
		_rpc.Debug += RpcOnDebug;
		_rpc.Mute += RpcOnMute;
		_rpc.Navigate += RpcOnNavigate;
		_rpc.Zoom += RpcOnZoom;
		_rpc.KeyEvent += RpcOnKeyEvent;
		_rpc.MouseButton += RpcOnMouseButton;
		_rpc.NewOverlay += RpcOnNewOverlay;
		_rpc.RemoveOverlay += RpcOnRemoveOverlay;
		_rpc.ResizeOverlay += RpcOnResizeOverlay;
		_rpc.InjectUserCss += RpcOnInjectUserCss;
	}

	private static void RpcOnInjectUserCss(InjectUserCssMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			var guid = new Guid(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out var overlay))
				overlay.InjectUserCss(msg.Css);
		}
	}

	private static void RpcOnResizeOverlay(ResizeOverlayMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			var guid = new Guid(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out var overlay))
			{
				overlay.Resize(new Size(msg.Width, msg.Height));
				_ = _rpc.UpdateTexture(guid, overlay.RenderHandler.SharedTextureHandle);
			}
		}
	}

	private static void RpcOnRemoveOverlay(RemoveOverlayMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			var guid = new Guid(msg.Guid.Span);
			if (_overlays.Remove(guid, out var overlay))
			{
				overlay.Dispose();
			}
		}
	}

	private static void RpcOnNewOverlay(NewOverlayMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			Size size = new(msg.Width, msg.Height);

			var renderHandler = new TextureRenderHandler(size);
			var guid = new Guid(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out Overlay? value))
			{
				value.Dispose();
				_overlays.Remove(guid);
			}

			Overlay overlay = new(msg.Id, msg.Url, msg.Zoom, msg.Muted, msg.Framerate, msg.CustomCss, renderHandler);
			overlay.Initialise();
			_overlays.Add(guid, overlay);

			renderHandler.CursorChanged += (o, cursor) =>
			{
				_ = _rpc.SetCursor(new SetCursorMessage() { Guid = msg.Guid, Cursor = cursor });
			};

			_ = _rpc.UpdateTexture(guid, renderHandler.SharedTextureHandle);
		}
	}

	private static void RpcOnMouseButton(MouseButtonMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			var guid = new Guid(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out var overlay))
				overlay.HandleMouseEvent(msg);
		}
	}

	private static void RpcOnKeyEvent(KeyEventMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			var guid = new Guid(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out var overlay))
				overlay.HandleKeyEvent(msg);
		}
	}

	private static void RpcOnZoom(ZoomMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			var guid = new Guid(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out var overlay))
				overlay.Zoom(msg.Zoom);
		}
	}

	private static void RpcOnNavigate(NavigateMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			var guid = new Guid(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out var overlay))
				overlay.Navigate(msg.Url);
		}
	}

	private static void RpcOnMute(MuteMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			var guid = new Guid(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out var overlay))
				overlay.Mute(msg.Mute);
		}
	}

	private static void RpcOnDebug(DebugMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			var guid = new Guid(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out var overlay))
				overlay.Debug();
		}
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