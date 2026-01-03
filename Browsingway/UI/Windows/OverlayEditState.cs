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
	public BaseVisibility BaseVisibility;
	public bool Locked;
	public bool Muted;
	public bool TypeThrough;
	public bool ClickThrough;
	public bool Fullscreen;

	// Positioning (percentage-based)
	public ScreenPosition Position;
	/// <summary>X offset from anchor point as percentage of screen width (-100 to +100)</summary>
	public float PositionX;
	/// <summary>Y offset from anchor point as percentage of screen height (-100 to +100)</summary>
	public float PositionY;
	/// <summary>Overlay width as percentage of screen width (0 to 100)</summary>
	public float PositionWidth;
	/// <summary>Overlay height as percentage of screen height (0 to 100)</summary>
	public float PositionHeight;

	// Advanced
	public string CustomCss = "";
	public string CustomJs = "";

	public List<VisibilityRule> VisibilityRules = [];

	/// <summary>
	/// Creates an edit state from an existing configuration.
	/// </summary>
	public static OverlayEditState FromConfig(OverlayConfiguration config) => new()
	{
		Guid = config.Guid,
		Name = config.Name,
		Url = config.Url,
		Zoom = config.Zoom,
		Opacity = config.Opacity,
		Framerate = config.Framerate,
		BaseVisibility = config.BaseVisibility,
		Locked = config.Locked,
		Muted = config.Muted,
		TypeThrough = config.TypeThrough,
		ClickThrough = config.ClickThrough,
		Fullscreen = config.Fullscreen,
		Position = config.Position,
		PositionX = config.PositionX,
		PositionY = config.PositionY,
		PositionWidth = config.PositionWidth,
		PositionHeight = config.PositionHeight,
		CustomCss = config.CustomCss,
		CustomJs = config.CustomJs,
		VisibilityRules = config.VisibilityRules.Select(r => new VisibilityRule { Enabled = r.Enabled, Negated = r.Negated, Trigger = r.Trigger, Action = r.Action, DelaySeconds = r.DelaySeconds }).ToList()
	};

	/// <summary>
	/// Applies the edit state back to a configuration.
	/// </summary>
	public void ApplyTo(OverlayConfiguration config)
	{
		config.Name = Name;
		config.Url = Url;
		config.Zoom = Zoom;
		config.Opacity = Opacity;
		config.Framerate = Framerate;
		config.BaseVisibility = BaseVisibility;
		config.Locked = Locked;
		config.Muted = Muted;
		config.TypeThrough = TypeThrough;
		config.ClickThrough = ClickThrough;
		config.Fullscreen = Fullscreen;
		config.Position = Position;
		config.PositionX = PositionX;
		config.PositionY = PositionY;
		config.PositionWidth = PositionWidth;
		config.PositionHeight = PositionHeight;
		config.CustomCss = CustomCss;
		config.CustomJs = CustomJs;
		config.VisibilityRules = VisibilityRules.Select(r => new VisibilityRule { Enabled = r.Enabled, Negated = r.Negated, Trigger = r.Trigger, Action = r.Action, DelaySeconds = r.DelaySeconds }).ToList();
	}

	/// <summary>
	/// Checks if the edit state differs from the original configuration.
	/// </summary>
	public bool IsDifferentFrom(OverlayConfiguration config)
	{
		if (Name != config.Name ||
			Url != config.Url ||
			Math.Abs(Zoom - config.Zoom) > 0.01f ||
			Math.Abs(Opacity - config.Opacity) > 0.01f ||
			Framerate != config.Framerate ||
			BaseVisibility != config.BaseVisibility ||
			Locked != config.Locked ||
			Muted != config.Muted ||
			TypeThrough != config.TypeThrough ||
			ClickThrough != config.ClickThrough ||
			Fullscreen != config.Fullscreen ||
			Position != config.Position ||
			Math.Abs(PositionX - config.PositionX) > 0.01f ||
			Math.Abs(PositionY - config.PositionY) > 0.01f ||
			Math.Abs(PositionWidth - config.PositionWidth) > 0.01f ||
			Math.Abs(PositionHeight - config.PositionHeight) > 0.01f ||
			CustomCss != config.CustomCss ||
			CustomJs != config.CustomJs)
		{
			return true;
		}

		if (VisibilityRules.Count != config.VisibilityRules.Count)
			return true;

		for (int i = 0; i < VisibilityRules.Count; i++)
		{
			if (VisibilityRules[i].Enabled != config.VisibilityRules[i].Enabled ||
				VisibilityRules[i].Negated != config.VisibilityRules[i].Negated ||
				VisibilityRules[i].Trigger != config.VisibilityRules[i].Trigger ||
				VisibilityRules[i].Action != config.VisibilityRules[i].Action ||
				VisibilityRules[i].DelaySeconds != config.VisibilityRules[i].DelaySeconds)
				return true;
		}

		return false;
	}
}
