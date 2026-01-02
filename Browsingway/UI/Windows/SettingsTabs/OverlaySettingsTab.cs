using Browsingway.Services;
using Browsingway.UI.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Browsingway.UI.Windows.SettingsTabs;

internal partial class OverlaySettingsTab
{
	private readonly List<TextEditorWindow> _activeEditors = [];
	private Guid? _pendingDeleteGuid;
	private string? _pendingDeleteName;

	private void OpenEditor(string title, string currentCode, Action<string> onSave)
	{
		var window = new TextEditorWindow(title, currentCode, onSave, () => { });
		_activeEditors.Add(window);
	}

	private static string SplitCamelCase(string input)
	{
		return System.Text.RegularExpressions.Regex.Replace(input, "(\\B[A-Z])", " $1");
	}

	public void CloseEditors()
	{
		foreach (var editor in _activeEditors)
		{
			editor.IsOpen = false;
		}
		_activeEditors.Clear();
	}
	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespaceRegex();

	private static string GetRuleSummary(VisibilityRule rule)
	{
		string condition = rule.Negated ? "If NOT" : "If";
		string trigger = SplitCamelCase(rule.Trigger.ToString()).ToLower();
		string action = rule.Action.ToString().ToLower();
		string delay = rule.DelaySeconds > 0 ? $" after {rule.DelaySeconds}s" : "";
		return $"{condition} {trigger}, {action} overlay{delay}";
	}

	private static (bool hasConflict, string message) CheckForConflicts(List<VisibilityRule> rules, int currentIndex)
	{
		var current = rules[currentIndex];
		for (int i = 0; i < rules.Count; i++)
		{
			if (i == currentIndex) continue;
			var other = rules[i];

			// Same trigger and negation, but different actions
			if (other.Trigger == current.Trigger && other.Negated == current.Negated)
			{
				bool isOpposite = (current.Action == VisibilityAction.Show && other.Action == VisibilityAction.Hide) ||
								  (current.Action == VisibilityAction.Hide && other.Action == VisibilityAction.Show) ||
								  (current.Action == VisibilityAction.Enable && other.Action == VisibilityAction.Disable) ||
								  (current.Action == VisibilityAction.Disable && other.Action == VisibilityAction.Enable);
				if (isOpposite)
					return (true, $"Conflicts with rule {i + 1}: same condition but opposite action");
			}
		}
		return (false, "");
	}

	private static readonly (string Name, VisibilityRule Rule)[] RulePresets =
	[
		("Show in combat", new VisibilityRule { Trigger = VisibilityTrigger.InCombat, Action = VisibilityAction.Show }),
		("Hide out of combat", new VisibilityRule { Negated = true, Trigger = VisibilityTrigger.InCombat, Action = VisibilityAction.Hide }),
		("Show when ACT available", new VisibilityRule { Trigger = VisibilityTrigger.ActAvailable, Action = VisibilityAction.Show }),
		("Hide in PvP", new VisibilityRule { Trigger = VisibilityTrigger.InPvp, Action = VisibilityAction.Hide }),
		("Disable in PvP", new VisibilityRule { Trigger = VisibilityTrigger.InPvp, Action = VisibilityAction.Disable }),
	];

	private readonly Configuration _config;
	private readonly OverlayManager _overlayManager;
	private readonly Func<bool> _getActAvailable;
	private readonly ISharedImmediateTexture? _texWarningIcon;

	// Edit state
	private readonly Dictionary<Guid, OverlayEditState> _editStates = [];
	private Guid? _selectedOverlayGuid;
	private readonly HashSet<Guid> _newOverlays = [];

	public OverlaySettingsTab(Configuration config, OverlayManager overlayManager, Func<bool> getActAvailable, ISharedImmediateTexture? warningIcon)
	{
		_config = config;
		_texWarningIcon = warningIcon;
		_overlayManager = overlayManager;
		_getActAvailable = getActAvailable;
		RefreshEditStates();
	}

	public void RefreshEditStates()
	{
		_editStates.Clear();
		foreach (var config in _config.Overlays)
		{
			_editStates[config.Guid] = OverlayEditState.FromConfig(config);
		}
	}

	public bool IsDirty()
	{
		if (_newOverlays.Count > 0)
			return true;

		foreach (var (guid, editState) in _editStates)
		{
			var config = _config.Overlays.Find(o => o.Guid == guid);
			if (config == null || editState.IsDifferentFrom(config))
				return true;
		}

		return false;
	}

	public void ApplyChanges()
	{
		foreach (var (guid, editState) in _editStates)
		{
			var config = _config.Overlays.Find(o => o.Guid == guid);
			if (config != null)
			{
				editState.ApplyTo(config);
			}
		}

		_newOverlays.Clear();
	}

	public void RevertChanges()
	{
		_selectedOverlayGuid = null;
		_newOverlays.Clear();
		RefreshEditStates();
	}

	public void Draw()
	{
		if (ImGui.BeginTabBar("OverlayTabs", ImGuiTabBarFlags.FittingPolicyScroll))
		{
			foreach (var overlay in _config.Overlays)
			{
				if (!_editStates.TryGetValue(overlay.Guid, out var editState))
					continue;

				ImGuiTabItemFlags flags = ImGuiTabItemFlags.None;

				// Select newly created overlay
				if (_selectedOverlayGuid == overlay.Guid && _newOverlays.Contains(overlay.Guid))
				{
					flags |= ImGuiTabItemFlags.SetSelected;
					_newOverlays.Remove(overlay.Guid);
				}

				bool tabOpen = true;
				if (ImGui.BeginTabItem($"{overlay.Name}##{overlay.Guid}", ref tabOpen, flags))
				{
					// Close editors when switching overlay tabs
					if (_selectedOverlayGuid != overlay.Guid)
					{
						CloseEditors();
					}
					_selectedOverlayGuid = overlay.Guid;

					ImGui.BeginChild("OverlaySettings", Vector2.Zero, false);
					RenderOverlaySettings(editState);
					ImGui.EndChild();

					ImGui.EndTabItem();
				}

				if (!tabOpen)
				{
					// Request confirmation before deleting
					_pendingDeleteGuid = overlay.Guid;
					_pendingDeleteName = editState.Name;
					ImGui.OpenPopup("Delete Overlay?");
				}
			}

			// Delete confirmation popup
			RenderDeleteConfirmationPopup();

			// Add button
			if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Trailing))
			{
				var newOverlay = new OverlayConfiguration();
				_config.Overlays.Add(newOverlay);
				_editStates[newOverlay.Guid] = OverlayEditState.FromConfig(newOverlay);
				_selectedOverlayGuid = newOverlay.Guid;
				_newOverlays.Add(newOverlay.Guid);
			}
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Add new overlay");

			ImGui.EndTabBar();
		}

		if (_config.Overlays.Count == 0)
		{
			ImGuiHelpers.ScaledDummy(20);
			ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Click + to create your first overlay.");
		}

		// Draw active editor windows
		for (int i = _activeEditors.Count - 1; i >= 0; i--)
		{
			var editor = _activeEditors[i];
			if (!editor.IsOpen)
			{
				_activeEditors.RemoveAt(i);
				continue;
			}

			bool open = editor.IsOpen;
			editor.PreDraw();
			ImGui.SetNextWindowSize(editor.SizeConstraints!.Value.MinimumSize, ImGuiCond.FirstUseEver);
			if (ImGui.Begin(editor.WindowName, ref open, editor.Flags))
			{
				editor.Draw();
			}
			ImGui.End();
			editor.IsOpen = open;
		}
	}

	private void RenderOverlaySettings(OverlayEditState state)
	{
		ImGui.PushID(state.Guid.ToString());

		ImGuiHelpers.ScaledDummy(5);

		// General settings
		ImGui.Separator();
		ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "General");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		float inputWidth = ImGui.GetContentRegionAvail().X * 0.75f;

		ImGui.Text("Name");
		ImGui.SetNextItemWidth(inputWidth);
		ImGui.InputText("##Name", ref state.Name, 100);
		ImGuiHelpers.ScaledDummy(5);

		ImGui.Text("URL");
		ImGui.SetNextItemWidth(inputWidth);
		ImGui.InputText("##URL", ref state.Url, 1000);
		ImGuiHelpers.ScaledDummy(5);

		ImGui.Unindent();

		// Rendering settings
		ImGui.Separator();
		ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Rendering");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		ImGui.SliderFloat("Zoom", ref state.Zoom, 10f, 500f, "%.0f%%");
		ImGui.SliderFloat("Opacity", ref state.Opacity, 10f, 100f, "%.0f%%");
		ImGui.SliderInt("Framerate", ref state.Framerate, 1, 144);

		ImGuiHelpers.ScaledDummy(5);

		ImGui.Unindent();

		// Behavior settings
		ImGui.Separator();
		ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Behavior");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		float availWidth = ImGui.GetContentRegionAvail().X;
		float checkboxWidth = 120 * ImGuiHelpers.GlobalScale;
		bool useColumns = availWidth > checkboxWidth * 2 + 20 * ImGuiHelpers.GlobalScale;
		float columnOffset = useColumns ? availWidth / 2 : 0;

		ImGui.Checkbox("Muted", ref state.Muted);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Mute audio playback");

		if (useColumns)
			ImGui.SameLine(columnOffset);

		bool isImplicitlyLocked = state.ClickThrough || state.Fullscreen;
		if (isImplicitlyLocked)
			ImGui.BeginDisabled();
		bool lockedValue = isImplicitlyLocked || state.Locked;
		if (ImGui.Checkbox("Locked", ref lockedValue) && !isImplicitlyLocked)
			state.Locked = lockedValue;
		if (isImplicitlyLocked)
			ImGui.EndDisabled();
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Lock position and size");

		ImGui.Checkbox("Click Through", ref state.ClickThrough);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Pass all mouse input through");

		if (useColumns)
			ImGui.SameLine(columnOffset);

		bool isImplicitlyTypeThrough = state.ClickThrough;
		if (isImplicitlyTypeThrough)
			ImGui.BeginDisabled();
		bool typeThroughValue = isImplicitlyTypeThrough || state.TypeThrough;
		if (ImGui.Checkbox("Type Through", ref typeThroughValue) && !isImplicitlyTypeThrough)
			state.TypeThrough = typeThroughValue;
		if (isImplicitlyTypeThrough)
			ImGui.EndDisabled();
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Pass keyboard input through");

		ImGuiHelpers.ScaledDummy(5);

		ImGui.Unindent();

		// Visibility settings
		ImGui.Separator();
		ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Visibility");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		// Base Visibility dropdown
		ImGui.Text("Base Visibility");
		ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
		if (ImGui.BeginCombo("##BaseVisibility", state.BaseVisibility.ToString()))
		{
			foreach (var visibility in Enum.GetValues<BaseVisibility>())
			{
				if (ImGui.Selectable(visibility.ToString(), state.BaseVisibility == visibility))
				{
					state.BaseVisibility = visibility;
				}
			}
			ImGui.EndCombo();
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Visible: normal display\nHidden: hidden but still running\nDisabled: completely disabled");

		ImGuiHelpers.ScaledDummy(5);

		ImGui.Text("Visibility Rules:");
		ImGuiHelpers.ScaledDummy(2);

		// Rules List
		for (int i = 0; i < state.VisibilityRules.Count; i++)
		{
			var rule = state.VisibilityRules[i];
			ImGui.PushID($"Rule{i}");

			// Check for conflicts
			var (hasConflict, conflictMessage) = CheckForConflicts(state.VisibilityRules, i);

			ImGui.AlignTextToFramePadding();

			// Reorder buttons at the start
			if ( i <= 0 )
				ImGui.BeginDisabled();
			if (ImGui.ArrowButton("##Up", ImGuiDir.Up))
			{
				(state.VisibilityRules[i], state.VisibilityRules[i - 1]) = (state.VisibilityRules[i - 1], state.VisibilityRules[i]);
			}
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Move up");
			if ( i <= 0 )
				ImGui.EndDisabled();
			
			ImGui.SameLine();
			
			if ( i >= state.VisibilityRules.Count - 1)
				ImGui.BeginDisabled();
			if (ImGui.ArrowButton("##Down", ImGuiDir.Down))
			{
				(state.VisibilityRules[i], state.VisibilityRules[i + 1]) = (state.VisibilityRules[i + 1], state.VisibilityRules[i]);
			}
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Move down");
			if ( i >= state.VisibilityRules.Count - 1)
				ImGui.EndDisabled();
			
			ImGui.SameLine();

			// Enabled checkbox
			ImGui.Checkbox("##Enabled", ref rule.Enabled);
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(rule.Enabled ? "Click to disable this rule" : "Click to enable this rule");

			ImGui.SameLine();

			// Dim the row if disabled
			if (!rule.Enabled)
				ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

			// Conflict warning icon
			if (hasConflict)
			{
				ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), "!");
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip(conflictMessage);
				ImGui.SameLine();
			}

			// Combined If/If NOT dropdown
			ImGui.SetNextItemWidth(70 * ImGuiHelpers.GlobalScale);
			string ifLabel = rule.Negated ? "If NOT" : "If";
			if (ImGui.BeginCombo("##IfNot", ifLabel))
			{
				if (ImGui.Selectable("If", !rule.Negated))
					rule.Negated = false;
				if (ImGui.Selectable("If NOT", rule.Negated))
					rule.Negated = true;
				ImGui.EndCombo();
			}

			ImGui.SameLine();

			// Trigger Dropdown
			ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
			if (ImGui.BeginCombo("##Trigger", SplitCamelCase(rule.Trigger.ToString())))
			{
				foreach (var trigger in Enum.GetValues<VisibilityTrigger>())
				{
					if (ImGui.Selectable(SplitCamelCase(trigger.ToString()), rule.Trigger == trigger))
					{
						rule.Trigger = trigger;
					}
				}
				ImGui.EndCombo();
			}

			ImGui.SameLine();
			ImGui.Text("then");
			ImGui.SameLine();

			// Action Dropdown
			ImGui.SetNextItemWidth(80 * ImGuiHelpers.GlobalScale);
			if (ImGui.BeginCombo("##Action", rule.Action.ToString()))
			{
				foreach (var action in Enum.GetValues<VisibilityAction>())
				{
					if (ImGui.Selectable(action.ToString(), rule.Action == action))
					{
						rule.Action = action;
					}
				}
				ImGui.EndCombo();
			}

			ImGui.SameLine();

			// Delay - show compact toggle or full input
			if (rule.DelaySeconds > 0)
			{
				ImGui.Text("after");
				ImGui.SameLine();
				ImGui.SetNextItemWidth(35 * ImGuiHelpers.GlobalScale);
				ImGui.InputInt("##Delay", ref rule.DelaySeconds, 0, 0);
				if (rule.DelaySeconds < 0) rule.DelaySeconds = 0;
				ImGui.SameLine();
				ImGui.Text("sec");
			}
			else
			{
				ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "(+delay)");
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip("Click to add a delay");
				if (ImGui.IsItemClicked())
					rule.DelaySeconds = 5;
			}

			if (!rule.Enabled)
				ImGui.PopStyleVar();

			ImGui.SameLine();

			// Remove Button
			if (ImGui.SmallButton("X"))
			{
				state.VisibilityRules.RemoveAt(i);
				i--;
				ImGui.PopID();
				continue;
			}
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Remove rule");

			// Summary tooltip for the whole row
			ImGui.SameLine();
			ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1f), "?");
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(GetRuleSummary(rule));

			ImGui.PopID();
		}

		// Add Rule section
		if (ImGui.Button("Add Rule"))
		{
			state.VisibilityRules.Add(new VisibilityRule
			{
				Trigger = VisibilityTrigger.InCombat,
				Action = VisibilityAction.Show,
				DelaySeconds = 0
			});
		}

		ImGui.SameLine();

		ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
		if (ImGui.BeginCombo("##Presets", "Add from preset..."))
		{
			foreach (var (name, preset) in RulePresets)
			{
				if (ImGui.Selectable(name))
				{
					state.VisibilityRules.Add(new VisibilityRule
					{
						Enabled = preset.Enabled,
						Negated = preset.Negated,
						Trigger = preset.Trigger,
						Action = preset.Action,
						DelaySeconds = preset.DelaySeconds
					});
				}
			}
			ImGui.EndCombo();
		}

		ImGuiHelpers.ScaledDummy(5);

		ImGui.Unindent();

		// Advanced settings
		ImGui.Separator();
		ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Advanced");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		ImGui.Checkbox("Fullscreen", ref state.Fullscreen);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Make overlay cover the entire screen");

		ImGui.Spacing();

		// Action buttons
		if (ImGui.Button("Reload", new Vector2(80 * ImGuiHelpers.GlobalScale, 0)))
		{
			_overlayManager.NavigateOverlay(state.Guid, state.Url);
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Reload the overlay page");

		ImGui.SameLine();

		if (ImGui.Button("Dev Tools", new Vector2(100 * ImGuiHelpers.GlobalScale, 0)))
		{
			_overlayManager.OpenDevTools(state.Guid);
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Open browser developer tools");

		ImGuiHelpers.ScaledDummy(5);

		float buttonWidth = 60 * ImGuiHelpers.GlobalScale;
		float labelWidth = 160 * ImGuiHelpers.GlobalScale;

		// Custom CSS
		bool hasCss = !string.IsNullOrWhiteSpace(state.CustomCss);
		ImGui.Text("Custom CSS:");
		ImGui.SameLine();
		ImGui.TextColored(hasCss ? new Vector4(1f, 0.3f, 0.3f, 1f) : new Vector4(0.3f, 0.8f, 0.3f, 1f),
			hasCss ? "enabled" : "disabled");
		ImGui.SameLine(labelWidth);
		if (ImGui.Button("Edit##CustomCss", new Vector2(buttonWidth, 0)))
		{
			OpenEditor($"Editing Custom CSS for {state.Name}", state.CustomCss, code => state.CustomCss = code);
		}

		// Custom JS
		bool hasJs = !string.IsNullOrWhiteSpace(state.CustomJs);
		ImGui.Text("Custom JS:");
		ImGui.SameLine();
		ImGui.TextColored(hasJs ? new Vector4(1f, 0.3f, 0.3f, 1f) : new Vector4(0.3f, 0.8f, 0.3f, 1f),
			hasJs ? "enabled" : "disabled");
		ImGui.SameLine(labelWidth);
		if (ImGui.Button("Edit##CustomJs", new Vector2(buttonWidth, 0)))
		{
			OpenEditor($"Editing Custom JS for {state.Name}", state.CustomJs, code => state.CustomJs = code);
		}

		ImGuiHelpers.ScaledDummy(5);

		ImGui.Unindent();

		ImGui.PopID();
	}

	private void RenderDeleteConfirmationPopup()
	{
		Vector2 center = ImGui.GetMainViewport().GetCenter();
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

		if (ImGui.BeginPopupModal("Delete Overlay?", ImGuiWindowFlags.AlwaysAutoResize))
		{
			float padding = 10 * ImGuiHelpers.GlobalScale;
			float iconSize = 64 * ImGuiHelpers.GlobalScale;

			// Top padding
			ImGui.Dummy(new Vector2(0, padding / 2));

			ImGui.Dummy(new Vector2(padding, 0));
			ImGui.SameLine();

			// Icon on left side
			if (_texWarningIcon != null)
			{
				ImGui.Dummy(new Vector2(0, padding / 2));
				ImGui.Image(_texWarningIcon.GetWrapOrEmpty().Handle, new Vector2(iconSize, iconSize));
				ImGui.SameLine();
			}

			// Message on right side
			ImGui.BeginGroup();
			ImGuiHelpers.ScaledDummy(5);
			ImGui.Text($"Are you sure you want to delete the overlay \"{_pendingDeleteName}\"?");
			ImGui.Spacing();
			ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "This action cannot be undone.");
			ImGui.EndGroup();

			ImGui.SameLine();
			ImGui.Dummy(new Vector2(padding, 0));

			ImGui.Dummy(new Vector2(0, padding));
			ImGui.Separator();
			ImGui.Dummy(new Vector2(0, padding / 2));

			float buttonWidth = 80 * ImGuiHelpers.GlobalScale;

			ImGui.Dummy(new Vector2(padding, 0));
			ImGui.SameLine();

			if (ImGui.Button("Delete", new Vector2(buttonWidth, 0)))
			{
				if (_pendingDeleteGuid.HasValue)
				{
					var overlay = _config.Overlays.Find(o => o.Guid == _pendingDeleteGuid.Value);
					if (overlay != null)
					{
						_config.Overlays.Remove(overlay);
						_editStates.Remove(_pendingDeleteGuid.Value);
						if (_selectedOverlayGuid == _pendingDeleteGuid.Value)
							_selectedOverlayGuid = _config.Overlays.FirstOrDefault()?.Guid;
					}
				}
				_pendingDeleteGuid = null;
				_pendingDeleteName = null;
				ImGui.CloseCurrentPopup();
			}

			ImGui.SameLine();

			if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
			{
				_pendingDeleteGuid = null;
				_pendingDeleteName = null;
				ImGui.CloseCurrentPopup();
			}

			ImGui.Dummy(new Vector2(0, padding / 2));

			ImGui.EndPopup();
		}
	}
}
