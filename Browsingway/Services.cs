using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Browsingway;

public class Services
{
	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	public static ICommandManager CommandManager { get; private set; } = null!;

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	public static IChatGui Chat { get; private set; } = null!;

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	public static IPluginLog PluginLog { get; private set; } = null!;

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	public static ITextureProvider TextureProvider { get; private set; } = null!;

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	public static DalamudPluginInterface PluginInterface { get; private set; } = null!;

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	public static IFramework Framework { get; private set; } = null!;

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	public static IClientState ClientState { get; set; } = null!;
}