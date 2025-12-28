using Dalamud.Plugin.Ipc;
using System.Diagnostics;

namespace Browsingway.Services;

internal sealed class ActManager
{
	public bool IsRunning { get; private set; }
	public event EventHandler<bool>? AvailabilityChanged;

	private readonly ICallGateSubscriber<bool> _iinactIpc;
	private int _ticksSinceCheck = 2000;
	private bool? _pendingNotification;

	public ActManager(IServiceContainer services)
	{
		_iinactIpc = services.PluginInterface.GetIpcSubscriber<bool>("IINACT.Server.Listening");
	}

	public void Check()
	{
		// Process any pending notification from background thread
		if (_pendingNotification.HasValue)
		{
			AvailabilityChanged?.Invoke(this, _pendingNotification.Value);
			_pendingNotification = null;
		}

		if (_ticksSinceCheck++ < 2000) return;
		_ticksSinceCheck = 0;

		// Try IINACT IPC first
		if (TryCheckIinactIpc()) return;

		// Fall back to process detection on background thread
		Task.Run(CheckProcesses);
	}

	private bool TryCheckIinactIpc()
	{
		try
		{
			bool listening = _iinactIpc.InvokeFunc();
			SetRunning(listening);
			return true; // IPC succeeded, no need for process check
		}
		catch
		{
			return false; // IPC failed, try process detection
		}
	}

	private void CheckProcesses()
	{
		bool found = IsActProcessRunning("Advanced Combat Tracker") 
		          || IsActProcessRunning("IINACT");
		SetRunning(found);
	}

	private static bool IsActProcessRunning(string processName)
	{
		var proc = Process.GetProcessesByName(processName).FirstOrDefault();
		if (proc is null) return false;
		
		// Wait for process to initialize (5 seconds) or check window title for ACT
		return processName == "Advanced Combat Tracker" 
			? proc.MainWindowTitle.Contains("Advanced Combat Tracker") || (DateTime.Now - proc.StartTime).TotalSeconds >= 5
			: (DateTime.Now - proc.StartTime).TotalSeconds >= 5;
	}

	private void SetRunning(bool running)
	{
		if (IsRunning == running) return;
		IsRunning = running;
		_pendingNotification = running;
	}
}
