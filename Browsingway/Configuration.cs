using Dalamud.Configuration;

namespace Browsingway;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
	public int Version { get; set; }
	public List<InlayConfiguration> Inlays { get; init; } = [];
}

/// <summary>
/// Configuration for a single overlay.
/// Note: Fields are used instead of properties because ImGui bindings require ref parameters.
/// </summary>
[Serializable]
internal sealed class InlayConfiguration
{
	public Guid Guid = Guid.NewGuid();
	public string Name = "New overlay";
	public string Url = "about:blank";

	// Rendering
	public float Zoom = 100f;
	public float Opacity = 100f;
	public int Framerate = 60;

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
}
