using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Browsingway.Services;

/// <summary>
/// Concrete service container populated via Dalamud IoC.
/// Created once at plugin startup and passed to all components via constructor injection.
/// </summary>
public sealed class ServiceContainer : IServiceContainer
{
	[PluginService] public ICommandManager CommandManager { get; init; } = null!;
	[PluginService] public IChatGui Chat { get; init; } = null!;
	[PluginService] public IPluginLog PluginLog { get; init; } = null!;
	[PluginService] public ITextureProvider TextureProvider { get; init; } = null!;
	[PluginService] public IDalamudPluginInterface PluginInterface { get; init; } = null!;
	[PluginService] public IFramework Framework { get; init; } = null!;
	[PluginService] public IClientState ClientState { get; init; } = null!;
	[PluginService] public IObjectTable ObjectTable { get; init; } = null!;
}
