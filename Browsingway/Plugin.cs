using Browsingway.Common;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Plugin;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;

namespace Browsingway;

public class Plugin : IDalamudPlugin
{
	private const string _command = "/bw";

	private readonly DependencyManager _dependencyManager;
	private readonly Dictionary<Guid, Inlay> _inlays = new();
	private readonly string _pluginConfigDir;
	private readonly string _pluginDir;

	private RenderProcess? _renderProcess;
	private Settings? _settings;

	public Plugin()
	{
		_pluginDir = Path.GetDirectoryName(AssemblyLocation) ?? "";
		if (String.IsNullOrEmpty(_pluginDir))
		{
			throw new Exception("Could not determine plugin directory");
		}

		_pluginConfigDir = PluginInterface.GetPluginConfigDirectory();

		_dependencyManager = new DependencyManager(_pluginDir, _pluginConfigDir);
		_dependencyManager.DependenciesReady += (_, _) => DependenciesReady();
		_dependencyManager.Initialise();

		// Hook up render hook
		PluginInterface.UiBuilder.Draw += Render;
	}

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	private static DalamudPluginInterface PluginInterface { get; set; } = null!;

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	private static CommandManager CommandManager { get; set; } = null!;

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	private static ChatGui Chat { get; set; } = null!;

	// Required for LivePluginLoader support
	public string AssemblyLocation { get; } = Assembly.GetExecutingAssembly().Location;
	public string Name => "Browsingway";

	public void Dispose()
	{
		foreach (Inlay inlay in _inlays.Values) { inlay.Dispose(); }

		_inlays.Clear();

		_renderProcess?.Dispose();

		_settings?.Dispose();

		CommandManager.RemoveHandler(_command);

		WndProcHandler.Shutdown();
		DxHandler.Shutdown();

		PluginInterface.Dispose();

		_dependencyManager.Dispose();
	}

	private void DependenciesReady()
	{
		// Spin up DX handling from the plugin interface
		DxHandler.Initialise(PluginInterface);

		// Spin up WndProc hook
		WndProcHandler.Initialise(DxHandler.WindowHandle);
		WndProcHandler.WndProcMessage += OnWndProc;

		// Boot the render process. This has to be done before initialising settings to prevent a
		// race condition inlays receiving a null reference.
		int pid = Process.GetCurrentProcess().Id;
		_renderProcess = new RenderProcess(pid, _pluginDir, _pluginConfigDir, _dependencyManager);
		_renderProcess.Receive += HandleIpcRequest;
		_renderProcess.Start();

		// Prep settings
		_settings = PluginInterface.Create<Settings>();
		if (_settings is not null)
		{
			_settings.InlayAdded += OnInlayAdded;
			_settings.InlayNavigated += OnInlayNavigated;
			_settings.InlayDebugged += OnInlayDebugged;
			_settings.InlayRemoved += OnInlayRemoved;
			_settings.InlayZoomed += OnInlayZoomed;
			_settings.TransportChanged += OnTransportChanged;
		}

		// Hook up the main BW command
		CommandManager.AddHandler(_command, new CommandInfo(HandleCommand) { HelpMessage = "Control Browsingway from the chat line! Type '/bw config' or open the settings for more info.", ShowInHelp = true });
	}

	private (bool, long) OnWndProc(WindowsMessage msg, ulong wParam, long lParam)
	{
		// Notify all the inlays of the wndproc, respond with the first capturing response (if any)
		// TODO: Yeah this ain't great but realistically only one will capture at any one time for now.
		IEnumerable<(bool, long)> responses = _inlays.Select(pair => pair.Value.WndProcMessage(msg, wParam, lParam));
		return responses.FirstOrDefault(pair => pair.Item1);
	}

	private void OnInlayAdded(object? sender, InlayConfiguration inlayConfig)
	{
		if (_renderProcess is null || _settings is null)
		{
			return;
		}

		Inlay inlay = new(_renderProcess, _settings.Config, inlayConfig);
		_inlays.Add(inlayConfig.Guid, inlay);
	}

	private void OnInlayNavigated(object? sender, InlayConfiguration config)
	{
		Inlay inlay = _inlays[config.Guid];
		inlay.Navigate(config.Url);
	}

	private void OnInlayDebugged(object? sender, InlayConfiguration config)
	{
		Inlay inlay = _inlays[config.Guid];
		inlay.Debug();
	}

	private void OnInlayRemoved(object? sender, InlayConfiguration config)
	{
		Inlay inlay = _inlays[config.Guid];
		_inlays.Remove(config.Guid);
		inlay.Dispose();
	}

	private void OnInlayZoomed(object? sender, InlayConfiguration config)
	{
		Inlay inlay = _inlays[config.Guid];
		inlay.Zoom(config.Zoom);
	}

	private void OnTransportChanged(object? sender, EventArgs unused)
	{
		// Transport has changed, need to rebuild all the inlay renderers
		foreach (Inlay inlay in _inlays.Values)
		{
			inlay.InvalidateTransport();
		}
	}

	private object? HandleIpcRequest(object? sender, UpstreamIpcRequest request)
	{
		switch (request)
		{
			case ReadyNotificationRequest readyNotificationRequest:
				{
					if (_settings is not null)
					{
						_settings.SetAvailableTransports(readyNotificationRequest.AvailableTransports);
						_settings.HydrateInlays();
					}

					return null;
				}

			case SetCursorRequest setCursorRequest:
				{
					// TODO: Integrate ideas from Bridge re: SoC between widget and inlay
					Inlay? inlay = _inlays.Values.FirstOrDefault(inlay => inlay.RenderGuid == setCursorRequest.Guid);

					inlay?.SetCursor(setCursorRequest.Cursor);
					return null;
				}

			default:
				throw new Exception($"Unknown IPC request type {request.GetType().Name} received.");
		}
	}

	private void Render()
	{
		_dependencyManager.Render();
		_settings?.Render();

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

		_renderProcess?.EnsureRenderProcessIsAlive();

		foreach (Inlay inlay in _inlays.Values) { inlay.Render(); }

		ImGui.PopStyleVar();
	}

	private void HandleCommand(string command, string rawArgs)
	{
		// Docs complain about perf of multiple splits.
		// I'm not convinced this is a sufficiently perf-critical path to care.
		string[] args = rawArgs.Split(null as char[], 2, StringSplitOptions.RemoveEmptyEntries);

		if (args.Length == 0)
		{
			Chat.PrintError(
				"No subcommand specified. Valid subcommands are: config,inlay.");
			return;
		}

		string subcommandArgs = args.Length > 1 ? args[1] : "";

		switch (args[0])
		{
			case "config":
				_settings?.HandleConfigCommand(subcommandArgs);
				break;
			case "inlay":
				_settings?.HandleInlayCommand(subcommandArgs);
				break;
			default:
				Chat.PrintError(
					$"Unknown subcommand '{args[0]}'. Valid subcommands are: config,inlay.");
				break;
		}
	}
}