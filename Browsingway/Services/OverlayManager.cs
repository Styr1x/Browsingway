using Browsingway.Common.Ipc;
using Browsingway.Extensions;
using Browsingway.Interop;
using Browsingway.UI.Windows;
using Dalamud.Interface.Windowing;
using System.Diagnostics;
using TerraFX.Interop.Windows;

namespace Browsingway.Services;

/// <summary>
/// Manages overlay lifecycle - creation, updates, and removal.
/// </summary>
internal sealed class OverlayManager : IDisposable
{
	private const int SyncDebounceMs = 200;

	private readonly Dictionary<Guid, OverlayWindow> _overlays = [];
	private readonly Dictionary<Guid, OverlayConfiguration> _activeConfigs = [];
	private readonly HashSet<Guid> _ephemeralOverlays = [];
	private readonly IServiceContainer _services;
	private readonly RenderProcessManager _renderProcessManager;
	private readonly string _pluginDir;
	private readonly WindowSystem _windowSystem;
	private readonly GameEnvTracker _visibilityTracker;

	// Debounce sync to renderer
	private readonly Stopwatch _syncDebounceTimer = new();
	private bool _syncPending;

	public OverlayManager(
		IServiceContainer services,
		RenderProcessManager renderProcessManager,
		string pluginDir,
		WindowSystem windowSystem,
		GameEnvTracker visibilityTracker)
	{
		_services = services;
		_renderProcessManager = renderProcessManager;
		_pluginDir = pluginDir;
		_windowSystem = windowSystem;
		_visibilityTracker = visibilityTracker;

		// Subscribe to visibility environment changes
		_visibilityTracker.EnvironmentChanged += OnVisibilityEnvironmentChanged;

		// Subscribe to Framework.Update for debounced sync
		_services.Framework.Update += OnFrameworkUpdate;

		// Subscribe to RPC events
		if (_renderProcessManager.Rpc != null)
		{
			_renderProcessManager.Rpc.SetCursor += OnSetCursor;
			_renderProcessManager.Rpc.UpdateTexture += OnUpdateTexture;
		}
	}

	public void Dispose()
	{
		_visibilityTracker.EnvironmentChanged -= OnVisibilityEnvironmentChanged;
		_services.Framework.Update -= OnFrameworkUpdate;

		foreach (var overlay in _overlays.Values)
		{
			_windowSystem.RemoveWindow(overlay);
			overlay.Dispose();
		}
		_overlays.Clear();
		_activeConfigs.Clear();
		_ephemeralOverlays.Clear();
	}

	#region Sync to Renderer

	private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
	{
		// Check if sync is pending and debounce time has passed
		if (_syncPending && _syncDebounceTimer.ElapsedMilliseconds >= SyncDebounceMs)
		{
			_syncPending = false;
			_syncDebounceTimer.Reset();
			ExecuteSync();
		}
	}

	/// <summary>
	/// Requests a sync to the renderer. Debounced to 200ms.
	/// </summary>
	public void RequestSync()
	{
		if (!_syncPending)
		{
			_syncDebounceTimer.Restart();
		}
		_syncPending = true;
	}

	/// <summary>
	/// Immediately syncs all overlay states to the renderer.
	/// </summary>
	public void SyncNow()
	{
		_syncPending = false;
		_syncDebounceTimer.Reset();
		ExecuteSync();
	}

	private void ExecuteSync()
	{
		if (_renderProcessManager.Rpc == null) return;

		var states = _overlays.Values
			.Select(overlay => overlay.GetState())
			.Where(state => state != null)
			.Cast<OverlayState>()
			.ToList();

		_renderProcessManager.Rpc.SyncOverlays(states).FireAndForget(_services.PluginLog);
	}

	#endregion

	private void OnVisibilityEnvironmentChanged(object? sender, GameEnvironment environment)
	{
		UpdateAllVisibility(environment);
	}

	/// <summary>
	/// Updates visibility for all active overlays based on current environment.
	/// </summary>
	public void UpdateAllVisibility(GameEnvironment environment)
	{
		foreach (var overlay in _overlays.Values)
		{
			overlay.UpdateVisibility(environment);
		}

		// Visibility affects which overlays are synced to renderer
		RequestSync();
	}


	public void AddOrUpdateOverlay(OverlayConfiguration config)
	{
		if (_overlays.TryGetValue(config.Guid, out var existing))
		{
			// Overlay exists - check if we need to recreate it (framerate changed)
			if (_activeConfigs.TryGetValue(config.Guid, out var oldConfig) && oldConfig.Framerate != config.Framerate)
			{
				// Framerate change requires recreation
				_windowSystem.RemoveWindow(existing);
				existing.Dispose();
				_overlays.Remove(config.Guid);
				CreateOverlay(config);
			}
			else
			{
				// Config changed - update visibility with current environment
				existing.UpdateVisibility(_visibilityTracker.CurrentEnvironment);
			}
		}
		else
		{
			CreateOverlay(config);
		}

		_activeConfigs[config.Guid] = config;
	}

	private void CreateOverlay(OverlayConfiguration config)
	{
		var overlay = new OverlayWindow(_services, _renderProcessManager, config, _pluginDir, RequestSync);
		_overlays[config.Guid] = overlay;
		_windowSystem.AddWindow(overlay);

		// Set initial visibility based on current environment
		overlay.UpdateVisibility(_visibilityTracker.CurrentEnvironment);
	}

	public int GetActiveOverlayCount() => _overlays.Count;

	public void RemoveOverlay(Guid guid)
	{
		if (_overlays.Remove(guid, out var overlay))
		{
			_windowSystem.RemoveWindow(overlay);
			overlay.Dispose();
		}
		_activeConfigs.Remove(guid);

		// Sync to renderer so it removes the overlay
		RequestSync();
	}

	/// <summary>
	/// Imperatively navigate an overlay to a new URL (user action).
	/// </summary>
	public void NavigateOverlay(Guid guid, string url)
	{
		if (_overlays.TryGetValue(guid, out var overlay))
		{
			overlay.Navigate(url);
		}
	}

	/// <summary>
	/// Imperatively open DevTools for an overlay (user action).
	/// </summary>
	public void OpenDevTools(Guid guid)
	{
		if (_overlays.TryGetValue(guid, out var overlay))
		{
			overlay.Debug();
		}
	}

	#region Ephemeral Overlays

	public Guid AddEphemeralOverlay(OverlayConfiguration config)
	{
		// Ensure unique GUID
		if (_overlays.ContainsKey(config.Guid))
		{
			config.Guid = Guid.NewGuid();
		}

		CreateOverlay(config);
		_activeConfigs[config.Guid] = config;
		_ephemeralOverlays.Add(config.Guid);
		return config.Guid;
	}

	public void RemoveEphemeralOverlay(Guid guid)
	{
		if (_ephemeralOverlays.Contains(guid))
		{
			RemoveOverlay(guid);
			_ephemeralOverlays.Remove(guid);
		}
	}

	public bool IsEphemeral(Guid guid) => _ephemeralOverlays.Contains(guid);

	public IReadOnlyCollection<Guid> GetEphemeralGuids() => _ephemeralOverlays;

	#endregion

	public void ReloadAllFromConfig(Configuration config, bool actAvailable)
	{
		// Determine which overlays should be active based on config
		HashSet<Guid> desiredOverlays = [];

		foreach (var overlayConfig in config.Overlays)
		{
			bool shouldBeActive = overlayConfig.BaseVisibility != BaseVisibility.Disabled;

			if (shouldBeActive)
			{
				desiredOverlays.Add(overlayConfig.Guid);
				AddOrUpdateOverlay(overlayConfig);
			}
		}

		// Remove overlays that are no longer in config or should be disabled
		// Preserve ephemeral overlays - they are not managed by config
		var toRemove = _overlays.Keys
			.Where(guid => !desiredOverlays.Contains(guid) && !_ephemeralOverlays.Contains(guid))
			.ToList();
		foreach (var guid in toRemove)
		{
			// Use internal remove to avoid duplicate sync calls
			if (_overlays.Remove(guid, out var overlay))
			{
				_windowSystem.RemoveWindow(overlay);
				overlay.Dispose();
			}
			_activeConfigs.Remove(guid);
		}

		// Sync all state to renderer
		RequestSync();
	}

	public WndProcResult HandleWndProc(WindowsMessage msg, ulong wParam, long lParam)
	{
		foreach (var overlay in _overlays.Values)
		{
			var result = overlay.WndProcMessage(msg, wParam, lParam);
			if (result.Handled)
				return result;
		}
		return WndProcResult.NotHandled;
	}

	private void OnSetCursor(Common.Ipc.SetCursorMessage msg)
	{
		_services.Framework.RunOnFrameworkThread(() =>
		{
			Guid guid = new(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out var overlay))
			{
				overlay.SetCursor(msg.Cursor);
			}
		});
	}

	private void OnUpdateTexture(Common.Ipc.UpdateTextureMessage msg)
	{
		_services.Framework.RunOnFrameworkThread(() =>
		{
			Guid guid = new(msg.Guid.Span);
			if (_overlays.TryGetValue(guid, out var overlay))
			{
				overlay.SetTexture((HANDLE)msg.TextureHandle);
			}
			else
			{
				_services.PluginLog.Error("Overlay Id not found");
			}
		});
	}
}
