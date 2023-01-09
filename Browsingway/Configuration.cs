using Browsingway.Common;
using Dalamud.Configuration;

namespace Browsingway;

[Serializable]
internal class Configuration : IPluginConfiguration
{
	public FrameTransportMode FrameTransportMode = FrameTransportMode.SharedTexture;
	public List<InlayConfiguration> Inlays = new();
	public int Version { get; set; } = 0;
}

[Serializable]
internal class InlayConfiguration
{
	public bool ClickThrough;
	public int Framerate = 60;
	public Guid Guid;
	public bool Hidden;
	public bool Locked;
	public string Name = null!;
	public float Opacity = 100f;
	public bool TypeThrough;
	public string Url = null!;
	public float Zoom = 100f;
	public bool Disabled;
	public bool Muted;
	public bool ActOptimizations;
}