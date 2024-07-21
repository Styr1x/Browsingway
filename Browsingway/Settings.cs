using Dalamud.Interface;
using ImGuiNET;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Browsingway;

// ReSharper disable once ClassNeverInstantiated.Global
internal class Settings : IDisposable
{
	public event EventHandler<InlayConfiguration>? OverlayAdded;
	public event EventHandler<InlayConfiguration>? OverlayNavigated;
	public event EventHandler<InlayConfiguration>? OverlayDebugged;
	public event EventHandler<InlayConfiguration>? OverlayRemoved;
	public event EventHandler<InlayConfiguration>? OverlayZoomed;
	public event EventHandler<InlayConfiguration>? OverlayMuted;
	public event EventHandler<InlayConfiguration>? OverlayUserCssChanged;
	public readonly Configuration Config;
	private bool _actAvailable = false;

#if DEBUG
	private bool _open = true;
#else
	private bool _open;
#endif

	private InlayConfiguration? _selectedOverlay;
	private Timer? _saveDebounceTimer;

	public Settings()
	{
		Services.PluginInterface.UiBuilder.OpenConfigUi += () => _open = true;
		Config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
	}

	public void Dispose() { }

	public void OnActAvailabilityChanged(bool available)
	{
		_actAvailable = available;
		foreach (InlayConfiguration? overlayConfig in Config.Inlays)
		{
			if (overlayConfig is { ActOptimizations: true, Disabled: false })
			{
				if (_actAvailable)
					OverlayAdded?.Invoke(this, overlayConfig);
				else
					OverlayRemoved?.Invoke(this, overlayConfig);
			}
		}
	}

	public void HandleConfigCommand(string rawArgs)
	{
		_open = true;

		// TODO: Add further config handling if required here.
	}

	public void HandleOverlayCommand(string rawArgs)
	{
		string[] args = rawArgs.Split(null as char[], 3, StringSplitOptions.RemoveEmptyEntries);

		// Ensure there's enough arguments
		if (args.Length < 2 || (args[1] != "reload" && args.Length < 3))
		{
			Services.Chat.PrintError("Invalid overlay command. Supported syntax: '[overlayCommandName] [setting] [value]'");
			return;
		}

		// Find the matching overlay config
		InlayConfiguration? targetConfig = Config.Inlays.Find(overlay => GetOverlayCommandName(overlay) == args[0]);
		if (targetConfig == null)
		{
			Services.Chat.PrintError(
				$"Unknown overlay '{args[0]}'.");
			return;
		}

		switch (args[1])
		{
			case "url":
				CommandSettingString(args[2], ref targetConfig.Url);
				// TODO: This call is duped with imgui handling. DRY.
				NavigateOverlay(targetConfig);
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
				ReloadOverlay(targetConfig);
				break;

			default:
				Services.Chat.PrintError(
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
				Services.Chat.PrintError(
					$"Unknown boolean value '{value}. Valid values are: on,off,toggle.");
				break;
		}
	}

	public void HydrateOverlays()
	{
		// Hydrate any overlays in the config
		foreach (InlayConfiguration? overlayConfig in Config.Inlays)
		{
			if (!overlayConfig.Disabled && (!overlayConfig.ActOptimizations || _actAvailable))
			{
				OverlayAdded?.Invoke(this, overlayConfig);
			}
		}
	}

	private InlayConfiguration? AddNewOverlay()
	{
		InlayConfiguration? overlayConfig = new() { Guid = Guid.NewGuid(), Name = "New overlay", Url = "about:blank" };
		Config.Inlays.Add(overlayConfig);
		OverlayAdded?.Invoke(this, overlayConfig);
		SaveSettings();

		return overlayConfig;
	}

	private void NavigateOverlay(InlayConfiguration overlayConfig)
	{
		if (overlayConfig.Url == "") { overlayConfig.Url = "about:blank"; }

		OverlayNavigated?.Invoke(this, overlayConfig);
	}

	private void UpdateZoomOverlay(InlayConfiguration overlayConfig)
	{
		OverlayZoomed?.Invoke(this, overlayConfig);
	}

	private void UpdateMuteOverlay(InlayConfiguration overlayConfig)
	{
		OverlayMuted?.Invoke(this, overlayConfig);
	}

	private void UpdateUserCss(InlayConfiguration overlayConfig)
	{
		OverlayUserCssChanged?.Invoke(this, overlayConfig);
	}

	private void ReloadOverlay(InlayConfiguration overlayConfig) { NavigateOverlay(overlayConfig); }

	private void DebugOverlay(InlayConfiguration overlayConfig)
	{
		OverlayDebugged?.Invoke(this, overlayConfig);
	}

	private void RemoveOverlay(InlayConfiguration overlayConfig)
	{
		OverlayRemoved?.Invoke(this, overlayConfig);
		Config.Inlays.Remove(overlayConfig);
		SaveSettings();
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
		Services.PluginInterface.SavePluginConfig(Config);
	}

	private string GetOverlayCommandName(InlayConfiguration overlayConfig)
	{
		return Regex.Replace(overlayConfig.Name, @"\s+", "").ToLower();
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
		if (_selectedOverlay == null)
		{
			dirty |= RenderGeneralSettings();
		}
		else
		{
			dirty |= RenderOverlaySettings(_selectedOverlay);
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
		if (ImGui.Selectable("General", _selectedOverlay == null))
		{
			_selectedOverlay = null;
		}

		// Overlay selector list
		ImGui.Dummy(new Vector2(0, 5));
		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
		ImGui.Text("- Overlays -");
		ImGui.PopStyleVar();
		foreach (InlayConfiguration? overlayConfig in Config?.Inlays!)
		{
			if (ImGui.Selectable($"{overlayConfig.Name}##{overlayConfig.Guid}", _selectedOverlay == overlayConfig))
			{
				_selectedOverlay = overlayConfig;
			}
		}

		ImGui.EndChild();

		// Selector controls
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
		ImGui.PushFont(UiBuilder.IconFont);

		int buttonWidth = selectorWidth / 2;
		if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), new Vector2(buttonWidth, 0)))
		{
			_selectedOverlay = AddNewOverlay();
		}

		ImGui.SameLine();
		if (_selectedOverlay != null)
		{
			if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0)))
			{
				InlayConfiguration? toRemove = _selectedOverlay;
				_selectedOverlay = null;
				RemoveOverlay(toRemove);
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

		ImGui.Text("Select an overlay on the left to edit its settings.");

		if (ImGui.CollapsingHeader("Command Help", ImGuiTreeNodeFlags.DefaultOpen))
		{
			// TODO: If this ever gets more than a few options, should probably colocate help with the defintion. Attributes?
			ImGui.Text("/bw config");
			ImGui.Text("Open this configuration window.");
			ImGui.Dummy(new Vector2(0, 5));
			ImGui.Text("/bw overlay [overlayCommandName] [setting] [value]");
			ImGui.TextWrapped(
				"Change a setting for an overlay.\n" +
				"\toverlayCommandName: The overlay to edit. Use the 'Command Name' shown in its config.\n" +
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

		return dirty;
	}

	private bool RenderOverlaySettings(InlayConfiguration overlayConfig)
	{
		bool dirty = false;

		ImGui.PushID(overlayConfig.Guid.ToString());

		dirty |= ImGui.InputText("Name", ref overlayConfig.Name, 100);

		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
		string? commandName = GetOverlayCommandName(overlayConfig);
		ImGui.InputText("Command Name", ref commandName, 100);
		ImGui.PopStyleVar();

		dirty |= ImGui.InputText("URL", ref overlayConfig.Url, 1000);
		if (ImGui.IsItemDeactivatedAfterEdit()) { NavigateOverlay(overlayConfig); }

		if (ImGui.InputFloat("Zoom", ref overlayConfig.Zoom, 1f, 10f, "%.0f%%"))
		{
			// clamp to allowed range 
			if (overlayConfig.Zoom < 10f)
			{
				overlayConfig.Zoom = 10f;
			}
			else if (overlayConfig.Zoom > 500f)
			{
				overlayConfig.Zoom = 500f;
			}

			dirty = true;

			// notify of zoom change
			UpdateZoomOverlay(overlayConfig);
		}

		if (ImGui.InputFloat("Opacity", ref overlayConfig.Opacity, 1f, 10f, "%.0f%%"))
		{
			// clamp to allowed range 
			if (overlayConfig.Opacity < 10f)
			{
				overlayConfig.Opacity = 10f;
			}
			else if (overlayConfig.Opacity > 100f)
			{
				overlayConfig.Opacity = 100f;
			}

			dirty = true;
		}

		if (ImGui.InputInt("Framerate", ref overlayConfig.Framerate, 1, 10))
		{
			// clamp to allowed range 
			if (overlayConfig.Framerate < 1)
			{
				overlayConfig.Framerate = 1;
			}
			else if (overlayConfig.Framerate > 300)
			{
				overlayConfig.Framerate = 300;
			}

			dirty = true;

			// framerate changes require the recreation of the browser instance
			// TODO: this is ugly as heck, fix once proper IPC is up and running
			OverlayRemoved?.Invoke(this, overlayConfig);
			OverlayAdded?.Invoke(this, overlayConfig);
		}

		ImGui.SetNextItemWidth(100);
		ImGui.Columns(2, "boolInlayOptions", false);

		if (ImGui.Checkbox("Disabled", ref overlayConfig.Disabled))
		{
			if (overlayConfig.Disabled)
				OverlayRemoved?.Invoke(this, overlayConfig);
			else
				OverlayAdded?.Invoke(this, overlayConfig);
			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Disables the overlay. Contrary to just hiding it this setting will stop it from ever being created."); }

		ImGui.NextColumn();
		ImGui.NextColumn();


		if (ImGui.Checkbox("Muted", ref overlayConfig.Muted))
		{
			UpdateMuteOverlay(overlayConfig);
			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Enables or disables audio playback."); }

		ImGui.NextColumn();

		if (ImGui.Checkbox("ACT/IINACT optimizations", ref overlayConfig.ActOptimizations))
		{
			if (!overlayConfig.Disabled)
			{
				if (overlayConfig.ActOptimizations)
				{
					if (!_actAvailable)
						OverlayRemoved?.Invoke(this, overlayConfig);
					else
						OverlayAdded?.Invoke(this, overlayConfig);
				}
				else
				{
					OverlayAdded?.Invoke(this, overlayConfig);
				}
			}

			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Enables ACT/IINACT specific optimizations. This will automatically disable the overlay if ACT/IINACT is not running.\n\nNOTE: This does NOT disable the overlay if the websocket is not reporting data."); }

		ImGui.NextColumn();

		if (overlayConfig.ClickThrough || overlayConfig.Fullscreen) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		bool true_ = true;
		bool implicit_ = overlayConfig.ClickThrough || overlayConfig.Fullscreen;
		dirty |= ImGui.Checkbox("Locked", ref implicit_ ? ref true_ : ref overlayConfig.Locked);
		if (overlayConfig.ClickThrough) { ImGui.PopStyleVar(); }

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the overlay from being resized or moved. This is implicitly set by Click Through and Fullscreen."); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox("Hidden", ref overlayConfig.Hidden);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Hide the overlay. This does not stop the overlay from executing, only from being displayed."); }

		ImGui.NextColumn();

		if (overlayConfig.ClickThrough) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		dirty |= ImGui.Checkbox("Type Through", ref overlayConfig.ClickThrough ? ref true_ : ref overlayConfig.TypeThrough);
		if (overlayConfig.ClickThrough || overlayConfig.Fullscreen) { ImGui.PopStyleVar(); }

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the overlay from intercepting any keyboard events. Implicitly set by Click Through."); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox("Click Through", ref overlayConfig.ClickThrough);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the overlay from intercepting any mouse events. Implicitly sets Locked and Type Through."); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox("Hide out of combat", ref overlayConfig.HideOutOfCombat);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Hide this overlay when out-of-combat."); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox("Hide in PvP", ref overlayConfig.HideInPvP);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Hide this overlay when in a PvP area."); }

		ImGui.NextColumn();

		if (!overlayConfig.HideOutOfCombat) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		dirty |= ImGui.InputInt("Hide Delay", ref overlayConfig.HideDelay);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Delay to hide overlay when out-of-combat in seconds."); }

		if (!overlayConfig.HideOutOfCombat) { ImGui.PopStyleVar(); }

		ImGui.Columns(1);

		ImGui.NewLine();
		if (ImGui.CollapsingHeader("Experimental / Unsupported"))
		{
			ImGui.NewLine();
			dirty |= ImGui.Checkbox("Fullscreen", ref overlayConfig.Fullscreen);
			ImGui.NewLine();
			if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Automatically makes this overlay cover the entire screen when enabled."); }

			ImGui.Text("Custom CSS code:");
			if (ImGui.InputTextMultiline("Custom CSS code", ref overlayConfig.CustomCss, 1000000,
				    new Vector2(-1, ImGui.GetTextLineHeight() * 10)))
			{
				dirty = true;
			}

			if (ImGui.IsItemDeactivatedAfterEdit()) { UpdateUserCss(overlayConfig); }
		}

		ImGui.NewLine();
		if (ImGui.Button("Reload")) { ReloadOverlay(overlayConfig); }

		ImGui.SameLine();
		if (ImGui.Button("Open Dev Tools")) { DebugOverlay(overlayConfig); }

		ImGui.PopID();

		return dirty;
	}
}