namespace Browsingway.Services;

/// <summary>
/// Manages overlay lifecycle - creation, updates, and removal.
/// Supports both persistent (config-based) and ephemeral (plugin-provided) overlays.
/// </summary>
internal interface IOverlayManager
{
	/// <summary>
	/// Creates or updates an overlay from configuration.
	/// </summary>
	void AddOrUpdateOverlay(InlayConfiguration config);

	/// <summary>
	/// Gets the count of currently active overlays.
	/// </summary>
	int GetActiveOverlayCount();

	/// <summary>
	/// Removes an overlay by its GUID.
	/// </summary>
	void RemoveOverlay(Guid guid);

	/// <summary>
	/// Navigates an overlay to a new URL.
	/// </summary>
	void NavigateOverlay(Guid guid, string url);

	/// <summary>
	/// Sets the zoom level for an overlay.
	/// </summary>
	void SetZoom(Guid guid, float zoom);

	/// <summary>
	/// Sets the mute state for an overlay.
	/// </summary>
	void SetMuted(Guid guid, bool muted);

	/// <summary>
	/// Injects custom CSS into an overlay.
	/// </summary>
	void SetCustomCss(Guid guid, string css);

	/// <summary>
	/// Opens dev tools for an overlay.
	/// </summary>
	void OpenDevTools(Guid guid);

	/// <summary>
	/// Reloads all overlays from current configuration.
	/// Called after settings are saved.
	/// </summary>
	void ReloadAllFromConfig(Configuration config, bool actAvailable);

	/// <summary>
	/// Renders all active overlays.
	/// </summary>
	void RenderAll();
}
