using Browsingway.Models;

namespace Browsingway.Services;

/// <summary>
/// Manages overlay lifecycle - creation, updates, and removal.
/// </summary>
internal sealed class OverlayManager : IOverlayManager, IDisposable
{
	private readonly Dictionary<Guid, Overlay> _overlays = [];
	private readonly Dictionary<Guid, InlayConfiguration> _activeConfigs = [];
	private readonly IServiceContainer _services;
	private readonly RenderProcess _renderProcess;
	private readonly string _pluginDir;

	public OverlayManager(IServiceContainer services, RenderProcess renderProcess, string pluginDir)
	{
		_services = services;
		_renderProcess = renderProcess;
		_pluginDir = pluginDir;

		// Subscribe to RPC events
		if (_renderProcess.Rpc != null)
		{
			_renderProcess.Rpc.SetCursor += OnSetCursor;
			_renderProcess.Rpc.UpdateTexture += OnUpdateTexture;
		}
	}

	public void Dispose()
	{
		foreach (var overlay in _overlays.Values)
		{
			overlay.Dispose();
		}
		_overlays.Clear();
		_activeConfigs.Clear();
	}

	public void AddOrUpdateOverlay(InlayConfiguration config)
	{
		if (_overlays.TryGetValue(config.Guid, out var existing))
		{
			// Overlay exists - check if we need to recreate it (framerate changed)
			if (_activeConfigs.TryGetValue(config.Guid, out var oldConfig) && oldConfig.Framerate != config.Framerate)
			{
				// Framerate change requires recreation
				existing.Dispose();
				_overlays.Remove(config.Guid);
				CreateOverlay(config);
			}
			// Otherwise the overlay will pick up config changes on next render
		}
		else
		{
			CreateOverlay(config);
		}

		_activeConfigs[config.Guid] = config;
	}

	private void CreateOverlay(InlayConfiguration config)
	{
		var overlay = new Overlay(_services, _renderProcess, config, _pluginDir);
		_overlays[config.Guid] = overlay;
	}

	public int GetActiveOverlayCount() => _overlays.Count;

	public void RemoveOverlay(Guid guid)
	{
		if (_overlays.Remove(guid, out var overlay))
		{
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

	public void ReloadAllFromConfig(Configuration config, bool actAvailable)
	{
		// Determine which overlays should be active based on config
		HashSet<Guid> desiredOverlays = [];

		foreach (var inlayConfig in config.Inlays)
		{
			bool shouldBeActive = !inlayConfig.Disabled &&
			                      (!inlayConfig.ActOptimizations || actAvailable);

			if (shouldBeActive)
			{
				desiredOverlays.Add(inlayConfig.Guid);
				AddOrUpdateOverlay(inlayConfig);
			}
		}

		// Remove overlays that are no longer in config or should be disabled
		var toRemove = _overlays.Keys.Where(guid => !desiredOverlays.Contains(guid)).ToList();
		foreach (var guid in toRemove)
		{
			RemoveOverlay(guid);
		}
	}

	public void RenderAll()
	{
		foreach (var overlay in _overlays.Values)
		{
			overlay.Render();
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
				overlay.SetTexture((IntPtr)msg.TextureHandle);
			}
			else
			{
				_services.PluginLog.Error("Overlay Id not found");
			}
		});
	}
}
