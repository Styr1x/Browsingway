using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Browsingway.Services;

/// <summary>
/// Service container interface for dependency injection.
/// Provides access to all Dalamud services used by the plugin.
/// </summary>
public interface IServiceContainer
{
	ICommandManager CommandManager { get; }
	IChatGui Chat { get; }
	IPluginLog PluginLog { get; }
	ITextureProvider TextureProvider { get; }
	IDalamudPluginInterface PluginInterface { get; }
	IFramework Framework { get; }
	IClientState ClientState { get; }
	IObjectTable ObjectTable { get; }
}
