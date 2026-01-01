using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace Browsingway.UI.Windows.SettingsTabs;

internal class GeneralSettingsTab
{
	private readonly Configuration _config;

	public GeneralSettingsTab(Configuration config)
	{
		_config = config;
	}

	public void Draw()
	{
		// General Settings
		ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "General");
		ImGui.Spacing();
		
		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		// Rendering Backend
		ImGui.Text("Rendering Backend");
		ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
		if (ImGui.BeginCombo("##Backend", _config.Backend.ToString()))
		{
			foreach (var backend in Enum.GetValues<RenderingBackend>())
			{
				if (ImGui.Selectable(backend.ToString(), _config.Backend == backend))
				{
					_config.Backend = backend;
				}
			}
			ImGui.EndCombo();
		}

		ImGuiHelpers.ScaledDummy(10);
		ImGui.Unindent();

		// IPC Settings
		ImGui.Separator();
		ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "IPC");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		bool allowEphemeral = _config.AllowEphemeralWindows;
		if (ImGui.Checkbox("Allow ephemeral window creation", ref allowEphemeral))
		{
			_config.AllowEphemeralWindows = allowEphemeral;
		}

		bool allowConfigChanges = _config.AllowConfigChanges;
		if (ImGui.Checkbox("Allow configuration changes", ref allowConfigChanges))
		{
			_config.AllowConfigChanges = allowConfigChanges;
		}

		ImGuiHelpers.ScaledDummy(10);
		ImGui.Unindent();

		ImGui.Separator();
		ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Command Help");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "/bw config");
		ImGui.TextWrapped("Open this configuration window.");

		ImGuiHelpers.ScaledDummy(10);

		ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "/bw overlay [name] [setting] [value]");
		ImGui.TextWrapped(
			"Change a setting for an overlay.\n\n" +
			"Settings:\n" +
			"  url <string>            - Set overlay URL\n" +
			"  visibility <value>      - Set visibility (visible/hidden/disabled)\n" +
			"  muted <bool>            - Mute/unmute audio\n" +
			"  locked <bool>           - Lock position/size\n" +
			"  typethrough <bool>      - Pass keyboard input\n" +
			"  clickthrough <bool>     - Pass mouse input\n" +
			"  fullscreen <bool>       - Fullscreen mode\n" +
			"  reload                  - Reload the overlay\n\n" +
			"Boolean values: on, off, toggle");

		ImGui.Unindent();
	}
}
