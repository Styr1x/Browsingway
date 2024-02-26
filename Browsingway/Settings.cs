using Browsingway.Common;
using Dalamud.Interface;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Browsingway;

// ReSharper disable once ClassNeverInstantiated.Global
internal class Settings : IDisposable
{
	public event EventHandler<InlayConfiguration>? InlayAdded;
	public event EventHandler<InlayConfiguration>? InlayNavigated;
	public event EventHandler<InlayConfiguration>? InlayDebugged;
	public event EventHandler<InlayConfiguration>? InlayRemoved;
	public event EventHandler<InlayConfiguration>? InlayZoomed;
	public event EventHandler<InlayConfiguration>? InlayMuted;

	public event EventHandler<InlayConfiguration>? InlayUserCssChanged;
	public event EventHandler? TransportChanged;

	public readonly Configuration Config;

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	private static DalamudPluginInterface PluginInterface { get; set; } = null!;

	[PluginService]
	// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
	private static IChatGui Chat { get; set; } = null!;

	private bool _actAvailable = false;

#if DEBUG
	private bool _open = true;
#else
	private bool _open;
#endif

	private List<FrameTransportMode> _availableTransports = new();

	private InlayConfiguration? _selectedInlay;
	private Timer? _saveDebounceTimer;

	public Settings()
	{
		PluginInterface.UiBuilder.OpenConfigUi += () => _open = true;
		Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
	}

	public void Dispose() { }

	public void OnActAvailabilityChanged(bool available)
	{
		_actAvailable = available;
		foreach (InlayConfiguration? inlayConfig in Config.Inlays)
		{
			if (inlayConfig is { ActOptimizations: true, Disabled: false })
			{
				if (_actAvailable)
					InlayAdded?.Invoke(this, inlayConfig);
				else
					InlayRemoved?.Invoke(this, inlayConfig);
			}
		}
	}

	public void HandleConfigCommand(string rawArgs)
	{
		_open = true;

		// TODO: Add further config handling if required here.
	}

	public void HandleInlayCommand(string rawArgs)
	{
		string[] args = rawArgs.Split(null as char[], 3, StringSplitOptions.RemoveEmptyEntries);

		// Ensure there's enough arguments
		if (args.Length < 2 || (args[1] != "reload" && args.Length < 3))
		{
			Chat.PrintError("Invalid inlay command. Supported syntax: '[inlayCommandName] [setting] [value]'");
			return;
		}

		// Find the matching inlay config
		InlayConfiguration? targetConfig = Config.Inlays.Find(inlay => GetInlayCommandName(inlay) == args[0]);
		if (targetConfig == null)
		{
			Chat.PrintError(
				$"Unknown inlay '{args[0]}'.");
			return;
		}

		switch (args[1])
		{
			case "url":
				CommandSettingString(args[2], ref targetConfig.Url);
				// TODO: This call is duped with imgui handling. DRY.
				NavigateInlay(targetConfig);
				break;
			case "locked":
				CommandSettingBoolean(args[2], ref targetConfig.Locked);
				break;
			case "hidden":
				CommandSettingBoolean(args[2], ref targetConfig.Hidden);
				break;
			case "typethrough":
				CommandSettingBoolean(args[2], ref targetConfig.TypeThrough);
				break;
			case "fullscreen":
				CommandSettingBoolean(args[2], ref targetConfig.Fullscreen);
				break;
			case "clickthrough":
				CommandSettingBoolean(args[2], ref targetConfig.ClickThrough);
				break;
			case "muted":
				CommandSettingBoolean(args[2], ref targetConfig.Muted);
				break;
			case "disabled":
				CommandSettingBoolean(args[2], ref targetConfig.Disabled);
				break;
			case "act":
				CommandSettingBoolean(args[2], ref targetConfig.ActOptimizations);
				break;
			case "reload":
				ReloadInlay(targetConfig);
				break;

			default:
				Chat.PrintError(
					$"Unknown setting '{args[1]}. Valid settings are: url,hidden,locked,fullscreen,clickthrough,typethrough,muted,disabled,act.");
				return;
		}

		SaveSettings();
	}

	private void CommandSettingString(string value, ref string target)
	{
		target = value;
	}

	private void CommandSettingBoolean(string value, ref bool target)
	{
		switch (value)
		{
			case "on":
				target = true;
				break;
			case "off":
				target = false;
				break;
			case "toggle":
				target = !target;
				break;
			default:
				Chat.PrintError(
					$"Unknown boolean value '{value}. Valid values are: on,off,toggle.");
				break;
		}
	}

	public void SetAvailableTransports(FrameTransportMode transports)
	{
		// Decode bit flags to array for easier ui crap
		_availableTransports = Enum.GetValues(typeof(FrameTransportMode))
			.Cast<FrameTransportMode>()
			.Where(transport => transport != FrameTransportMode.None && transports.HasFlag(transport))
			.ToList();

		// If the configured transport isn't available, pick the first so we don't end up in a weird spot.
		// NOTE: Might be nice to avoid saving this to disc - a one-off failure may cause a save of full fallback mode.
		if (_availableTransports.Count > 0 && !_availableTransports.Contains(Config.FrameTransportMode))
		{
			SetActiveTransport(_availableTransports[0]);
		}
	}

	public void HydrateInlays()
	{
		// Hydrate any inlays in the config
		foreach (InlayConfiguration? inlayConfig in Config.Inlays)
		{
			if (!inlayConfig.Disabled && (!inlayConfig.ActOptimizations || _actAvailable))
			{
				InlayAdded?.Invoke(this, inlayConfig);
			}
		}
	}

	private InlayConfiguration? AddNewInlay()
	{
		InlayConfiguration? inlayConfig = new() { Guid = Guid.NewGuid(), Name = "New inlay", Url = "about:blank" };
		Config.Inlays.Add(inlayConfig);
		InlayAdded?.Invoke(this, inlayConfig);
		SaveSettings();

		return inlayConfig;
	}

	private void NavigateInlay(InlayConfiguration inlayConfig)
	{
		if (inlayConfig.Url == "") { inlayConfig.Url = "about:blank"; }

		InlayNavigated?.Invoke(this, inlayConfig);
	}

	private void UpdateZoomInlay(InlayConfiguration inlayConfig)
	{
		InlayZoomed?.Invoke(this, inlayConfig);
	}

	private void UpdateMuteInlay(InlayConfiguration inlayConfig)
	{
		InlayMuted?.Invoke(this, inlayConfig);
	}

	private void UpdateUserCss(InlayConfiguration inlayConfig)
	{
		InlayUserCssChanged?.Invoke(this, inlayConfig);
	}

	private void ReloadInlay(InlayConfiguration inlayConfig) { NavigateInlay(inlayConfig); }

	private void DebugInlay(InlayConfiguration inlayConfig)
	{
		InlayDebugged?.Invoke(this, inlayConfig);
	}

	private void RemoveInlay(InlayConfiguration inlayConfig)
	{
		InlayRemoved?.Invoke(this, inlayConfig);
		Config.Inlays.Remove(inlayConfig);
		SaveSettings();
	}

	private void SetActiveTransport(FrameTransportMode transport)
	{
		Config.FrameTransportMode = transport;
		TransportChanged?.Invoke(this, EventArgs.Empty);
	}

	private void DebouncedSaveSettings()
	{
		_saveDebounceTimer?.Dispose();
		_saveDebounceTimer = new Timer(_ => SaveSettings(), null, 1000, Timeout.Infinite);
	}

	private void SaveSettings()
	{
		_saveDebounceTimer?.Dispose();
		_saveDebounceTimer = null;
		PluginInterface.SavePluginConfig(Config);
	}

	private string GetInlayCommandName(InlayConfiguration inlayConfig)
	{
		return Regex.Replace(inlayConfig.Name, @"\s+", "").ToLower();
	}

	public void Render()
	{
		if (!_open) { return; }

		// Primary window container
		ImGui.SetNextWindowSizeConstraints(new Vector2(400, 300), new Vector2(9001, 9001));
		ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None
		                               | ImGuiWindowFlags.NoScrollbar
		                               | ImGuiWindowFlags.NoScrollWithMouse
		                               | ImGuiWindowFlags.NoCollapse;
		ImGui.Begin("Browsingway Settings", ref _open, windowFlags);

		RenderPaneSelector();

		// Pane details
		bool dirty = false;
		ImGui.SameLine();
		ImGui.BeginChild("details");
		if (_selectedInlay == null)
		{
			dirty |= RenderGeneralSettings();
		}
		else
		{
			dirty |= RenderInlaySettings(_selectedInlay);
		}

		ImGui.EndChild();

		if (dirty) { DebouncedSaveSettings(); }

		ImGui.End();
	}

	private void RenderPaneSelector()
	{
		// Selector pane
		ImGui.BeginGroup();
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

		int selectorWidth = 100;
		ImGui.BeginChild("panes", new Vector2(selectorWidth, -ImGui.GetFrameHeightWithSpacing()), true);

		// General settings
		if (ImGui.Selectable("General", _selectedInlay == null))
		{
			_selectedInlay = null;
		}

		// Inlay selector list
		ImGui.Dummy(new Vector2(0, 5));
		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
		ImGui.Text("- Inlays -");
		ImGui.PopStyleVar();
		foreach (InlayConfiguration? inlayConfig in Config?.Inlays!)
		{
			if (ImGui.Selectable($"{inlayConfig.Name}##{inlayConfig.Guid}", _selectedInlay == inlayConfig))
			{
				_selectedInlay = inlayConfig;
			}
		}

		ImGui.EndChild();

		// Selector controls
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
		ImGui.PushFont(UiBuilder.IconFont);

		int buttonWidth = selectorWidth / 2;
		if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), new Vector2(buttonWidth, 0)))
		{
			_selectedInlay = AddNewInlay();
		}

		ImGui.SameLine();
		if (_selectedInlay != null)
		{
			if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0)))
			{
				InlayConfiguration? toRemove = _selectedInlay;
				_selectedInlay = null;
				RemoveInlay(toRemove);
			}
		}
		else
		{
			ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
			ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0));
			ImGui.PopStyleVar();
		}

		ImGui.PopFont();
		ImGui.PopStyleVar(2);

		ImGui.EndGroup();
	}

	private bool RenderGeneralSettings()
	{
		bool dirty = false;

		ImGui.Text("Select an inlay on the left to edit its settings.");

		if (ImGui.CollapsingHeader("Command Help", ImGuiTreeNodeFlags.DefaultOpen))
		{
			// TODO: If this ever gets more than a few options, should probably colocate help with the defintion. Attributes?
			ImGui.Text("/bw config");
			ImGui.Text("Open this configuration window.");
			ImGui.Dummy(new Vector2(0, 5));
			ImGui.Text("/bw inlay [inlayCommandName] [setting] [value]");
			ImGui.TextWrapped(
				"Change a setting for an inlay.\n" +
				"\tinlayCommandName: The inlay to edit. Use the 'Command Name' shown in its config.\n" +
				"\tsetting: Value to change. Accepted settings are:\n" +
				"\t\turl: string\n" +
				"\t\tdisabled: boolean\n" +
				"\t\tmuted: boolean\n" +
				"\t\tact: boolean\n" +
				"\t\tlocked: boolean\n" +
				"\t\thidden: boolean\n" +
				"\t\ttypethrough: boolean\n" +
				"\t\tclickthrough: boolean\n" +
				"\t\tfullscreen: boolean\n" +
				"\t\treload: -\n" +
				"\tvalue: Value to set for the setting. Accepted values are:\n" +
				"\t\tstring: any string value\n\t\tboolean: on, off, toggle");
		}

		if (ImGui.CollapsingHeader("Advanced Settings"))
		{
			IEnumerable<string> options = _availableTransports.Select(transport => transport.ToString());
			int currentIndex = _availableTransports.IndexOf(Config.FrameTransportMode);

			if (_availableTransports.Count == 0)
			{
				options = options.Append("Initialising...");
				currentIndex = 0;
			}

			if (options.Count() <= 1) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

			bool transportChanged = ImGui.Combo("Frame transport", ref currentIndex, options.ToArray(), options.Count());
			if (options.Count() <= 1) { ImGui.PopStyleVar(); }

			// TODO: Flipping this should probably try to rebuild existing inlays
			dirty |= transportChanged;
			if (transportChanged)
			{
				SetActiveTransport(_availableTransports[currentIndex]);
			}

			if (Config.FrameTransportMode == FrameTransportMode.BitmapBuffer)
			{
				ImGui.PushStyleColor(ImGuiCol.Text, 0xFF0000FF);
				ImGui.TextWrapped("The bitmap buffer frame transport is a fallback, and should only be used if no other options work for you. It is not as stable as the shared texture option.");
				ImGui.PopStyleColor();
			}
		}

		return dirty;
	}

	private bool RenderInlaySettings(InlayConfiguration inlayConfig)
	{
		bool dirty = false;

		ImGui.PushID(inlayConfig.Guid.ToString());

		dirty |= ImGui.InputText("Name", ref inlayConfig.Name, 100);

		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
		string? commandName = GetInlayCommandName(inlayConfig);
		ImGui.InputText("Command Name", ref commandName, 100);
		ImGui.PopStyleVar();

		dirty |= ImGui.InputText("URL", ref inlayConfig.Url, 1000);
		if (ImGui.IsItemDeactivatedAfterEdit()) { NavigateInlay(inlayConfig); }

		if (ImGui.InputFloat("Zoom", ref inlayConfig.Zoom, 1f, 10f, "%.0f%%"))
		{
			// clamp to allowed range 
			if (inlayConfig.Zoom < 10f)
			{
				inlayConfig.Zoom = 10f;
			}
			else if (inlayConfig.Zoom > 500f)
			{
				inlayConfig.Zoom = 500f;
			}

			dirty = true;

			// notify of zoom change
			UpdateZoomInlay(inlayConfig);
		}

		if (ImGui.InputFloat("Opacity", ref inlayConfig.Opacity, 1f, 10f, "%.0f%%"))
		{
			// clamp to allowed range 
			if (inlayConfig.Opacity < 10f)
			{
				inlayConfig.Opacity = 10f;
			}
			else if (inlayConfig.Opacity > 100f)
			{
				inlayConfig.Opacity = 100f;
			}

			dirty = true;
		}

		if (ImGui.InputInt("Framerate", ref inlayConfig.Framerate, 1, 10))
		{
			// clamp to allowed range 
			if (inlayConfig.Framerate < 1)
			{
				inlayConfig.Framerate = 1;
			}
			else if (inlayConfig.Framerate > 300)
			{
				inlayConfig.Framerate = 300;
			}

			dirty = true;

			// framerate changes require the recreation of the browser instance
			// TODO: this is ugly as heck, fix once proper IPC is up and running
			InlayRemoved?.Invoke(this, inlayConfig);
			InlayAdded?.Invoke(this, inlayConfig);
		}

		ImGui.SetNextItemWidth(100);
		ImGui.Columns(2, "boolInlayOptions", false);

		if (ImGui.Checkbox("Disabled", ref inlayConfig.Disabled))
		{
			if (inlayConfig.Disabled)
				InlayRemoved?.Invoke(this, inlayConfig);
			else
				InlayAdded?.Invoke(this, inlayConfig);
			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Disables the inlay. Contrary to just hiding it this setting will stop it from ever being created."); }

		ImGui.NextColumn();
		ImGui.NextColumn();


		if (ImGui.Checkbox("Muted", ref inlayConfig.Muted))
		{
			UpdateMuteInlay(inlayConfig);
			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Enables or disables audio playback."); }

		ImGui.NextColumn();
		ImGui.NextColumn();
		
		if (ImGui.Checkbox("ACT/IINACT optimizations", ref inlayConfig.ActOptimizations))
		{
			if (!inlayConfig.Disabled)
			{
				if (inlayConfig.ActOptimizations)
				{
					if (!_actAvailable)
						InlayRemoved?.Invoke(this, inlayConfig);
					else
						InlayAdded?.Invoke(this, inlayConfig);
				}
				else
				{
					InlayAdded?.Invoke(this, inlayConfig);
				}
			}

			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Enables ACT/IINACT specific optimizations. This will automatically disable the overlay if ACT/IINACT is not running."); }

		ImGui.NextColumn();
		ImGui.NextColumn();

		dirty |= ImGui.Checkbox("Fullscreen", ref inlayConfig.Fullscreen);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Automatically makes this inlay cover the entire screen when enabled."); }

		ImGui.NextColumn();
		ImGui.NextColumn();

		if (inlayConfig.ClickThrough || inlayConfig.Fullscreen) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		bool true_ = true;
		bool implicit_ = inlayConfig.ClickThrough || inlayConfig.Fullscreen;
		dirty |= ImGui.Checkbox("Locked", ref implicit_ ? ref true_ : ref inlayConfig.Locked);
		if (inlayConfig.ClickThrough) { ImGui.PopStyleVar(); }

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the inlay from being resized or moved. This is implicitly set by Click Through and Fullscreen."); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox("Hidden", ref inlayConfig.Hidden);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Hide the inlay. This does not stop the inlay from executing, only from being displayed."); }

		ImGui.NextColumn();

		if (inlayConfig.ClickThrough) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		dirty |= ImGui.Checkbox("Type Through", ref inlayConfig.ClickThrough ? ref true_ : ref inlayConfig.TypeThrough);
		if (inlayConfig.ClickThrough || inlayConfig.Fullscreen) { ImGui.PopStyleVar(); }

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the inlay from intercepting any keyboard events. Implicitly set by Click Through."); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox("Click Through", ref inlayConfig.ClickThrough);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the inlay from intercepting any mouse events. Implicitly sets Locked and Type Through."); }

		ImGui.NextColumn();

		ImGui.Columns(1);
		
		ImGui.NewLine();
		if (ImGui.CollapsingHeader("Experimental / Unsupported"))
		{
			ImGui.Text("Custom CSS code:");
			if (ImGui.InputTextMultiline("Custom CSS code", ref inlayConfig.CustomCss, 1000000,
				    new Vector2(-1, ImGui.GetTextLineHeight() * 10)))
			{
				dirty = true;
			}

			if (ImGui.IsItemDeactivatedAfterEdit()) { UpdateUserCss(inlayConfig); }
		}

		ImGui.NewLine();
		if (ImGui.Button("Reload")) { ReloadInlay(inlayConfig); }

		ImGui.SameLine();
		if (ImGui.Button("Open Dev Tools")) { DebugInlay(inlayConfig); }

		ImGui.PopID();

		return dirty;
	}
}