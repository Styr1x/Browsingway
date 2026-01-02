using Browsingway.Common;
using Browsingway.Common.Ipc;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using TerraFX.Interop.Windows;

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

		bool dxRunning = DxHandler.Initialise(new LUID {LowPart = args.DxgiAdapterLuidLow, HighPart = args.DxgiAdapterLuidHigh});
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

	private static void InitializeIpc(string channelName)
	{
		_rpc = new RendererRpc(channelName);

		// Declarative state sync
		_rpc.SyncOverlays += RpcOnSyncOverlays;

		// Imperative actions (user-triggered, not state)
		_rpc.Navigate += RpcOnNavigate;
		_rpc.Debug += RpcOnDebug;
		_rpc.MouseButton += RpcOnMouseButton;
		_rpc.KeyEvent += RpcOnKeyEvent;
	}

	#region Declarative State Sync

	private static void RpcOnSyncOverlays(SyncOverlaysMessage msg)
	{
		lock (_lockIpc)
		{
			if (_isShuttingDown)
				return;

			var desiredGuids = new HashSet<Guid>();

			// Build set of desired overlays and create/update each
			foreach (var state in msg.Overlays)
			{
				var guid = new Guid(state.Guid.Span);
				desiredGuids.Add(guid);

				if (_overlays.TryGetValue(guid, out var existing))
				{
					// Overlay exists - check if framerate changed (requires recreation)
					if (existing.Framerate != state.Framerate)
					{
						// Recreate overlay with new framerate
						existing.Dispose();
						_overlays.Remove(guid);
						CreateOverlay(guid, state);
					}
					else
					{
						// Update existing overlay
						bool resized = existing.Update(state);
						if (resized)
						{
							_ = _rpc.UpdateTexture(guid, existing.RenderHandler.SharedTextureHandle);
						}
					}
				}
				else
				{
					// Create new overlay
					CreateOverlay(guid, state);
				}
			}

			// Remove overlays not in desired state
			var toRemove = _overlays.Keys.Where(g => !desiredGuids.Contains(g)).ToList();
			foreach (var guid in toRemove)
			{
				if (_overlays.Remove(guid, out var overlay))
				{
					overlay.Dispose();
				}
			}
		}
	}

	private static void CreateOverlay(Guid guid, OverlayState state)
	{
		Size size = new(state.Width, state.Height);
		var renderHandler = new TextureRenderHandler(size);

		Overlay overlay = new(state.Id, state.Url, state.Zoom, state.Muted, state.Framerate, state.CustomCss, renderHandler);
		overlay.Initialise();
		_overlays.Add(guid, overlay);

		renderHandler.CursorChanged += (o, cursor) =>
		{
			_ = _rpc.SetCursor(new SetCursorMessage() { Guid = guid.ToByteArray(), Cursor = cursor });
		};

		_ = _rpc.UpdateTexture(guid, renderHandler.SharedTextureHandle);
	}

	#endregion

	#region Imperative Actions

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

	#endregion

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
		string assemblyName = args.Name.Split(new[] {','}, 2)[0] + ".dll";

		string? assemblyPath = null;
		if (assemblyName.StartsWith("CefSharp"))
		{
			assemblyPath = Path.Combine(_cefAssemblyDir, assemblyName);
		}
		else if (assemblyName.StartsWith("TerraFX"))
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
