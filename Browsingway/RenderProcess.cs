using Browsingway.Common;
using Browsingway.Common.Ipc;
using Dalamud.Plugin.Services;
using System.Diagnostics;

namespace Browsingway;

internal class RenderProcess : IDisposable
{
	public event EventHandler? Crashed;
	public BrowsingwayRpc? Rpc { get; private set; }

	private readonly DependencyManager _dependencyManager;

	private readonly string _ipcChannelName;

	private readonly string _keepAliveHandleName;
	private readonly int _parentPid;
	private readonly string _pluginDir;

	private readonly string _cefCacheDir;
	private readonly FileStream? _cacheSlotLock;

	private const string _cefCacheDirName = "cef-cache";
	private const int _maxCacheSlots = 16;

	private DateTime _lastRenderCheck = DateTime.MinValue;
	private uint _restartCount = 0;

	private const uint _maxRestarts = 5;
	private const uint _checkDelaySeconds = 1;
	private const uint _processOkAfterSeconds = 5;

	private Process _process;
	private bool _running;

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
		_parentPid = pid;

		(_cefCacheDir, _cacheSlotLock) = ClaimCacheSlot(configDir);

		try
		{
			Rpc = new BrowsingwayRpc(_ipcChannelName);
			_process = SetupProcess();
		}
		catch
		{
			_cacheSlotLock?.Dispose();
			throw;
		}
	}

	private static (string dir, FileStream? slotLock) ClaimCacheSlot(string configDir)
	{
		// Claimed once here, not in SetupProcess, so the profile dir stays stable across renderer restarts.
		// Slot 1 keeps the original name; the OS releases the lock however the process ends.
		string firstDir = Path.Combine(configDir, _cefCacheDirName);
		IOException? lastInUse = null;

		for (int slot = 1; slot <= _maxCacheSlots; slot++)
		{
			string name = slot == 1 ? _cefCacheDirName : $"{_cefCacheDirName}-{slot}";
			string dir = Path.Combine(configDir, name);

			try
			{
				FileStream slotLock = new(Path.Combine(configDir, $"{name}.lock"), FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
				if (slot > 1)
				{
					Services.PluginLog.Info($"Using CEF cache slot {slot} ({dir}); earlier slots are held by other instances");
				}

				return (dir, slotLock);
			}
			catch (IOException e)
			{
				lastInUse = e;
				Services.PluginLog.Debug($"CEF cache slot {slot} could not be claimed: {e.Message}");
			}
			catch (Exception e)
			{
				// not another instance; the lock only arbitrates multiboxing, so run unlocked rather than fail
				Services.PluginLog.Error(e, $"Could not open the CEF cache lock in {configDir}; running unlocked on {firstDir}");
				return (firstDir, null);
			}
		}

		Services.PluginLog.Error(lastInUse, $"All {_maxCacheSlots} CEF cache slots are in use; running unlocked on {firstDir}");
		return (firstDir, null);
	}

	public void Dispose()
	{
		try
		{
			Stop();

			_process.Dispose();
			Rpc?.Dispose();
		}
		finally
		{
			_cacheSlotLock?.Dispose();
		}
	}

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
		if (!_running)
		{
			return;
		}

		// only check every second, reduces stress on the render thread
		if (DateTime.Now - _lastRenderCheck < TimeSpan.FromSeconds(_checkDelaySeconds))
		{
			return;
		}

		_lastRenderCheck = DateTime.Now;

		if (!HasProcessExited())
		{
			// process is still running, reset restart counter if it ran for at least 5 seconds
			if (_restartCount > 0 && DateTime.Now - _process.StartTime > TimeSpan.FromSeconds(_processOkAfterSeconds))
			{
				_restartCount = 0;
			}

			return;
		}

		if (_restartCount >= _maxRestarts)
		{
			Services.PluginLog.Error("Render process is crashing in a loop - please check the logs. No further restarts will be attempted until Browsingway is restarted.");
			Stop();
			Rpc?.Dispose();
			Rpc = null;
			OnProcessCrashed();
			return;
		}

		Task.Run(() =>
		{
			if (_hasExited && 0 == Interlocked.Exchange(ref _restarting, 1))
			{
				try
				{
					// process crashed, restart
					_restartCount++;
					Services.PluginLog.Error($"Render process crashed - will restart asap (attempt {_restartCount}/{_maxRestarts}).");
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
					Services.PluginLog.Error(e, "Failed to restart render process");
				}
				finally
				{
					Interlocked.Exchange(ref _restarting, 0);
				}
			}
		});
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
		try { _process.WaitForExit(1000); }
		catch (InvalidOperationException) { }
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
					Services.PluginLog.Error(e, "Failed to get process exit status");
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

		RenderParams processArgs = new()
		{
			ParentPid = _parentPid,
			DalamudAssemblyDir = Path.GetDirectoryName(typeof(IPluginLog).Assembly.Location)!,
			CefAssemblyDir = cefAssemblyDir,
			CefCacheDir = _cefCacheDir,
			DxgiAdapterLuidLow = DxHandler.AdapterLuid.LowPart,
			DxgiAdapterLuidHigh = DxHandler.AdapterLuid.HighPart,
			KeepAliveHandleName = _keepAliveHandleName,
			IpcChannelName = _ipcChannelName
		};

		Process process = new();
		process.StartInfo = new ProcessStartInfo
		{
			FileName = Path.Combine(_pluginDir, "renderer", "Browsingway.Renderer.exe"),
			Arguments = RenderParamsSerializer.Serialize(processArgs),
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		process.OutputDataReceived += (_, args) => Services.PluginLog.Info($"[Render]: {args.Data}");
		process.ErrorDataReceived += (_, args) => Services.PluginLog.Error($"[Render]: {args.Data}");

		return process;
	}

	private void OnProcessCrashed()
	{
		Crashed?.Invoke(this, EventArgs.Empty);
	}
}