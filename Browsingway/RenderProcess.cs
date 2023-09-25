using Browsingway.Common;
using Dalamud.Plugin.Services;
using System.Diagnostics;

namespace Browsingway;

internal class RenderProcess : IDisposable
{
	public delegate object? ReceiveEventHandler(object sender, UpstreamIpcRequest request);

	private readonly string _configDir;
	private readonly DependencyManager _dependencyManager;

	private readonly IpcBuffer<UpstreamIpcRequest, DownstreamIpcRequest> _ipc;
	private readonly string _ipcChannelName;

	private readonly string _keepAliveHandleName;
	private readonly int _parentPid;
	private readonly string _pluginDir;

	private Process _process;
	private bool _running;
	private readonly IPluginLog _pluginLog;

	public RenderProcess(int pid,
		string pluginDir,
		string configDir,
		DependencyManager dependencyManager,
		IPluginLog pluginLog
	)
	{
		_keepAliveHandleName = $"BrowsingwayRendererKeepAlive{pid}";
		_ipcChannelName = $"BrowsingwayRendererIpcChannel{pid}";
		_dependencyManager = dependencyManager;
		_pluginDir = pluginDir;
		_configDir = configDir;
		_parentPid = pid;
		_pluginLog = pluginLog;

		_ipc = new IpcBuffer<UpstreamIpcRequest, DownstreamIpcRequest>(_ipcChannelName, request => Receive?.Invoke(this, request));

		_process = SetupProcess();
	}

	public void Dispose()
	{
		Stop();

		_process.Dispose();
		_ipc.Dispose();
	}

	public event EventHandler? Crashed;

	public event ReceiveEventHandler? Receive;

	public void Start()
	{
		if (_running)
		{
			return;
		}

		_process.Start();
		_process.BeginOutputReadLine();
		_process.BeginErrorReadLine();

		_running = true;
	}

	private int _restarting = 0; // This needs to be a numeric type for Interlocked.Exchange

	public void EnsureRenderProcessIsAlive()
	{
		if (!_running || !HasProcessExited())
		{
			return;
		}

		Task.Run(() =>
		{
			if (_hasExited && 0 == Interlocked.Exchange(ref _restarting, 1))
			{
				try
				{
					// process crashed, restart
					_pluginLog.Error("Render process crashed - will restart asap");
					_process = SetupProcess();
					_process.Start();
					_process.BeginOutputReadLine();
					_process.BeginErrorReadLine();

					// notify everyone that we have to reinit
					OnProcessCrashed();

					// reset the process exit flag
					_hasExited = false;
				}
				catch (Exception e)
				{
					_pluginLog.Error(e, "Failed to restart render process");
				}
				finally
				{
					Interlocked.Exchange(ref _restarting, 0);
				}
			}
		});
	}

	public void Send(DownstreamIpcRequest request) { Send<object>(request); }

	// TODO: Option to wrap this func in an async version?
	public Task<IpcResponse<TResponse>> Send<TResponse>(DownstreamIpcRequest request)
	{
		return _ipc.RemoteRequestAsync<TResponse>(request);
	}

	public void Stop()
	{
		if (!_running) { return; }

		_running = false;

		// Grab the handle the process is waiting on and open it up
		EventWaitHandle handle = new(false, EventResetMode.ManualReset, _keepAliveHandleName);
		handle.Set();
		handle.Dispose();

		// Give the process a sec to gracefully shut down, then kill it
		_process.WaitForExit(1000);
		try { _process.Kill(); }
		catch (InvalidOperationException) { }
	}

	private bool _hasExited = false;
	private int _checkingExited = 0; // This needs to be a numeric type for Interlocked.Exchange

	private bool HasProcessExited()
	{
		// Process.HasExited can be an expensive call (on some systems?), so it's
		// offloaded to a Task, here. This could be related to Riot's Vanguard
		// kernel anti-cheat. The performance bottleneck occurs in ntdll, so this
		// is difficult to isolate and debug.
		Task.Run(() =>
		{
			if (!_hasExited && 0 == Interlocked.Exchange(ref _checkingExited, 1))
			{
				try
				{
					_hasExited = _process.HasExited;
				}
				catch (Exception e)
				{
					_pluginLog.Error(e, "Failed to get process exit status");
				}
				finally
				{
					Interlocked.Exchange(ref _checkingExited, 0);
				}
			}
		});

		return _hasExited;
	}

	private Process SetupProcess()
	{
		string cefAssemblyDir = _dependencyManager.GetDependencyPathFor("cef");

		RenderProcessArguments processArgs = new()
		{
			ParentPid = _parentPid,
			DalamudAssemblyDir = Path.GetDirectoryName(typeof(IPluginLog).Assembly.Location)!,
			CefAssemblyDir = cefAssemblyDir,
			CefCacheDir = Path.Combine(_configDir, "cef-cache"),
			DxgiAdapterLuid = DxHandler.AdapterLuid,
			KeepAliveHandleName = _keepAliveHandleName,
			IpcChannelName = _ipcChannelName
		};

		Process process = new();
		process.StartInfo = new ProcessStartInfo
		{
			FileName = Path.Combine(_pluginDir, "renderer", "Browsingway.Renderer.exe"),
			Arguments = processArgs.Serialise().Replace("\"", "\"\"\""),
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		string runtimePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "runtime");
		// ensure Dalamud's runtime is used even if there's a system runtime, to avoid runtime version mismatches
		process.StartInfo.EnvironmentVariables.Remove("DOTNET_ROOT");
		process.StartInfo.EnvironmentVariables.Add("DOTNET_ROOT", runtimePath);

		process.OutputDataReceived += (_, args) => _pluginLog.Info($"[Render]: {args.Data}");
		process.ErrorDataReceived += (_, args) => _pluginLog.Error($"[Render]: {args.Data}");

		return process;
	}

	private void OnProcessCrashed()
	{
		Crashed?.Invoke(this, EventArgs.Empty);
	}
}