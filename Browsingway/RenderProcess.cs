using Browsingway.Common;
using Dalamud.Logging;
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

	public RenderProcess(int pid,
		string pluginDir,
		string configDir,
		DependencyManager dependencyManager
	)
	{
		_keepAliveHandleName = $"BrowsingwayRendererKeepAlive{pid}";
		_ipcChannelName = $"BrowsingwayRendererIpcChannel{pid}";
		_dependencyManager = dependencyManager;
		_pluginDir = pluginDir;
		_configDir = configDir;
		_parentPid = pid;

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

	public void EnsureRenderProcessIsAlive()
	{
		if (!_running || !_process.HasExited)
		{
			return;
		}


		// TODO: this should really be async

		// process crashed, restart
		PluginLog.LogError("Render process crashed - will restart asap");
		_process = SetupProcess();
		_process.Start();
		_process.BeginOutputReadLine();
		_process.BeginErrorReadLine();

		// notify everyone that we have to reinit
		OnProcessCrashed();
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

	private Process SetupProcess()
	{
		string cefAssemblyDir = _dependencyManager.GetDependencyPathFor("cef");

		RenderProcessArguments processArgs = new()
		{
			ParentPid = _parentPid,
			DalamudAssemblyDir = Path.GetDirectoryName(typeof(PluginLog).Assembly.Location)!,
			CefAssemblyDir = cefAssemblyDir,
			CefCacheDir = Path.Combine(_configDir, "cef-cache"),
			DxgiAdapterLuid = DxHandler.AdapterLuid,
			KeepAliveHandleName = _keepAliveHandleName,
			IpcChannelName = _ipcChannelName
		};

		Process process = new Process();
		process.StartInfo = new ProcessStartInfo
		{
			FileName = Path.Combine(_pluginDir, "renderer", "Browsingway.Renderer.exe"),
			Arguments = processArgs.Serialise().Replace("\"", "\"\"\""),
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		process.OutputDataReceived += (_, args) => PluginLog.Log($"[Render]: {args.Data}");
		process.ErrorDataReceived += (_, args) => PluginLog.LogError($"[Render]: {args.Data}");

		return process;
	}

	private void OnProcessCrashed()
	{
		Crashed?.Invoke(this, EventArgs.Empty);
	}
}