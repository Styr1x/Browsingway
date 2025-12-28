using Browsingway.Commands;
using Browsingway.Models;
using Browsingway.Services;
using Browsingway.UI.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using System.Numerics;
using System.Reflection;

namespace Browsingway;

public class Plugin : IDalamudPlugin
{
	private const string _command = "/bw";

	private readonly DependencyManager _dependencyManager;
	private readonly string _pluginConfigDir;
	private readonly string _pluginDir;
	private readonly IServiceContainer _services;
	private readonly ActManager _actManager;

	private RenderProcess? _renderProcess;
	private OverlayManager? _overlayManager;
	private SettingsWindow? _settingsWindow;
	private DependencyWindow? _dependencyWindow;
	private OverlayCommandHandler? _commandHandler;
	private Configuration? _config;
	private readonly WindowSystem _windowSystem = new("Browsingway");

	public Plugin(IDalamudPluginInterface pluginInterface)
	{
		// Initialize service container via Dalamud IoC
		_services = pluginInterface.Create<ServiceContainer>()!;

		_pluginDir = pluginInterface.AssemblyLocation.DirectoryName ?? "";
		if (string.IsNullOrEmpty(_pluginDir))
		{
			throw new Exception("Could not determine plugin directory");
		}

		_pluginConfigDir = pluginInterface.GetPluginConfigDirectory();

		_actManager = new ActManager(_services);

		_dependencyManager = new DependencyManager(_services, _pluginDir, _pluginConfigDir);
		_dependencyManager.DependenciesReady += (_, _) => DependenciesReady();
		// Initialise DependencyWindow
		_dependencyWindow = new DependencyWindow(_dependencyManager, _services, _pluginDir);
		_windowSystem.AddWindow(_dependencyWindow);
		
		_dependencyManager.Initialise();

		// Hook up render hook
		pluginInterface.UiBuilder.Draw += Render;
	}

	// Required for LivePluginLoader support
	public string AssemblyLocation { get; } = Assembly.GetExecutingAssembly().Location;
	public string Name => "Browsingway";

	public void Dispose()
	{
		_overlayManager?.Dispose();
		_renderProcess?.Dispose();
		_settingsWindow?.Dispose();
		_dependencyWindow?.Dispose();

		_windowSystem.RemoveAllWindows();

		_services.CommandManager.RemoveHandler(_command);

		WndProcHandler.Shutdown();
		DxHandler.Shutdown();

		_dependencyManager.Dispose();
	}

	private void DependenciesReady()
	{
		// Spin up DX handling from the plugin interface
		DxHandler.Initialise(_services.PluginInterface);

		// Spin up WndProc hook
		WndProcHandler.Initialise(DxHandler.WindowHandle);
		WndProcHandler.WndProcMessage += OnWndProc;

		// Boot the render process
		int pid = Environment.ProcessId;
		_renderProcess = new RenderProcess(_services, pid, _pluginDir, _pluginConfigDir, _dependencyManager);

		// Create overlay manager
		_overlayManager = new OverlayManager(_services, _renderProcess, _pluginDir);

		// Load configuration
		_config = _services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

		// Create settings window
		_settingsWindow = new SettingsWindow(
			_services,
			_overlayManager,
			_config,
			() => _actManager.IsRunning,
			() => _overlayManager.GetActiveOverlayCount(),
			_pluginDir);

		_windowSystem.AddWindow(_settingsWindow);

		// Create command handler
		_commandHandler = new OverlayCommandHandler(
			_services,
			_overlayManager,
			() => _config!,
			() =>
			{
				_services.PluginInterface.SavePluginConfig(_config!);
				_overlayManager.ReloadAllFromConfig(_config!, _actManager.IsRunning);
				_settingsWindow.OnConfigChanged();
			});

		// Handle renderer ready event
		_renderProcess.Rpc!.RendererReady += msg =>
		{
			if (!msg.HasDxSharedTexturesSupport)
			{
				_services.PluginLog.Error("Could not initialize shared textures transport. Browsingway will not work.");
				return;
			}

			_services.Framework.RunOnFrameworkThread(() =>
			{
				// Initial load of overlays from config
				_overlayManager.ReloadAllFromConfig(_config!, _actManager.IsRunning);
			});
		};

		_renderProcess.Start();

		// Handle ACT availability changes
		_actManager.AvailabilityChanged += OnActAvailabilityChanged;

		// Hook up the main BW command
		_services.CommandManager.AddHandler(_command,
			new CommandInfo(HandleCommand) { HelpMessage = "Control Browsingway from the chat line! Type '/bw config' or open the settings for more info.", ShowInHelp = true });
	}

	private WndProcResult OnWndProc(WindowsMessage msg, ulong wParam, long lParam)
	{
		return _overlayManager?.HandleWndProc(msg, wParam, lParam) ?? WndProcResult.NotHandled;
	}

	private void OnActAvailabilityChanged(object? sender, bool e)
	{
		_settingsWindow?.OnActAvailabilityChanged();
	}

	private void Render()
	{
		// _dependencyManager.Render(); // Removed as handled by WindowSystem
		_windowSystem.Draw();

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

		_renderProcess?.EnsureRenderProcessIsAlive();
		_actManager.Check();

		_overlayManager?.RenderAll();

		ImGui.PopStyleVar();
	}

	private void HandleCommand(string command, string rawArgs)
	{
		string[] args = rawArgs.Split(null as char[], 2, StringSplitOptions.RemoveEmptyEntries);

		if (args.Length == 0)
		{
			_services.Chat.PrintError("No subcommand specified. Valid subcommands are: config,overlay.");
			return;
		}

		string subcommandArgs = args.Length > 1 ? args[1] : "";

		switch (args[0])
		{
			case "config":
				_settingsWindow?.Open();
				break;
			case "inlay":
			case "overlay":
				_commandHandler?.Handle(subcommandArgs);
				break;
			default:
				_services.Chat.PrintError($"Unknown subcommand '{args[0]}'. Valid subcommands are: config,overlay,inlay.");
				break;
		}
	}
}
