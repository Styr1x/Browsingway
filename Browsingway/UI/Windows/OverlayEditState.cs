namespace Browsingway.UI.Windows;

/// <summary>
/// Mutable edit state for an overlay configuration.
/// Changes here don't affect the actual config until Save is called.
/// </summary>
internal sealed class OverlayEditState
{
	public Guid Guid { get; init; }
	public string Name = "";
	public string Url = "";

	// Rendering
	public float Zoom;
	public float Opacity;
	public int Framerate;

	// Behavior flags
	public bool Disabled;
	public bool Hidden;
	public bool Locked;
	public bool Muted;
	public bool TypeThrough;
	public bool ClickThrough;
	public bool Fullscreen;

	// Combat/PvP visibility
	public bool ActOptimizations;
	public bool HideOutOfCombat;
	public bool HideInPvP;
	public int HideDelay;

	// Advanced
	public string CustomCss = "";

	/// <summary>
	/// Creates an edit state from an existing configuration.
	/// </summary>
	public static OverlayEditState FromConfig(InlayConfiguration config) => new()
	{
		Guid = config.Guid,
		Name = config.Name,
		Url = config.Url,
		Zoom = config.Zoom,
		Opacity = config.Opacity,
		Framerate = config.Framerate,
		Disabled = config.Disabled,
		Hidden = config.Hidden,
		Locked = config.Locked,
		Muted = config.Muted,
		TypeThrough = config.TypeThrough,
		ClickThrough = config.ClickThrough,
		Fullscreen = config.Fullscreen,
		ActOptimizations = config.ActOptimizations,
		HideOutOfCombat = config.HideOutOfCombat,
		HideInPvP = config.HideInPvP,
		HideDelay = config.HideDelay,
		CustomCss = config.CustomCss
	};

	/// <summary>
	/// Applies the edit state back to a configuration.
	/// </summary>
	public void ApplyTo(InlayConfiguration config)
	{
		config.Name = Name;
		config.Url = Url;
		config.Zoom = Zoom;
		config.Opacity = Opacity;
		config.Framerate = Framerate;
		config.Disabled = Disabled;
		config.Hidden = Hidden;
		config.Locked = Locked;
		config.Muted = Muted;
		config.TypeThrough = TypeThrough;
		config.ClickThrough = ClickThrough;
		config.Fullscreen = Fullscreen;
		config.ActOptimizations = ActOptimizations;
		config.HideOutOfCombat = HideOutOfCombat;
		config.HideInPvP = HideInPvP;
		config.HideDelay = HideDelay;
		config.CustomCss = CustomCss;
	}

	/// <summary>
	/// Checks if the edit state differs from the original configuration.
	/// </summary>
	public bool IsDifferentFrom(InlayConfiguration config) =>
		Name != config.Name ||
		Url != config.Url ||
		Math.Abs(Zoom - config.Zoom) > 0.01f ||
		Math.Abs(Opacity - config.Opacity) > 0.01f ||
		Framerate != config.Framerate ||
		Disabled != config.Disabled ||
		Hidden != config.Hidden ||
		Locked != config.Locked ||
		Muted != config.Muted ||
		TypeThrough != config.TypeThrough ||
		ClickThrough != config.ClickThrough ||
		Fullscreen != config.Fullscreen ||
		ActOptimizations != config.ActOptimizations ||
		HideOutOfCombat != config.HideOutOfCombat ||
		HideInPvP != config.HideInPvP ||
		HideDelay != config.HideDelay ||
		CustomCss != config.CustomCss;
}
