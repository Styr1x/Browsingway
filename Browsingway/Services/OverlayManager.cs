using Browsingway.Interop;
using Browsingway.UI.Windows;
using Dalamud.Interface.Windowing;
using TerraFX.Interop.Windows;

namespace Browsingway.Services;

/// <summary>
/// Manages overlay lifecycle - creation, updates, and removal.
/// </summary>
internal sealed class OverlayManager : IOverlayManager, IDisposable
{
	private readonly Dictionary<Guid, OverlayWindow> _overlays = [];
	private readonly Dictionary<Guid, OverlayConfiguration> _activeConfigs = [];
	private readonly HashSet<Guid> _ephemeralOverlays = [];
	private readonly IServiceContainer _services;
	private readonly RenderProcessManager _renderProcessManager;
	private readonly string _pluginDir;
	private readonly WindowSystem _windowSystem;
	private readonly GameEnvTracker _visibilityTracker;

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

		foreach (var overlay in _overlays.Values)
		{
			_windowSystem.RemoveWindow(overlay);
			overlay.Dispose();
		}
		_overlays.Clear();
		_activeConfigs.Clear();
		_ephemeralOverlays.Clear();
	}

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
		var overlay = new OverlayWindow(_services, _renderProcessManager, config, _pluginDir);
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
	}

	public void NavigateOverlay(Guid guid, string url)
	{
		if (_overlays.TryGetValue(guid, out var overlay))
		{
			overlay.Navigate(url);
		}
	}

	public void SetZoom(Guid guid, float zoom)
	{
		if (_overlays.TryGetValue(guid, out var overlay))
		{
			overlay.Zoom(zoom);
		}
	}

	public void SetMuted(Guid guid, bool muted)
	{
		if (_overlays.TryGetValue(guid, out var overlay))
		{
			overlay.Mute(muted);
		}
	}

	public void SetCustomCss(Guid guid, string css)
	{
		if (_overlays.TryGetValue(guid, out var overlay))
		{
			overlay.InjectUserCss(css);
		}
	}

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
			RemoveOverlay(guid);
		}
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
