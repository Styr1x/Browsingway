using Browsingway.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace Browsingway.UI.Windows.SettingsTabs;

internal partial class OverlaySettingsTab
{
	// UI Constants
	private static class UIConstants
	{
		public static readonly Vector4 SectionHeaderColor = new(0.4f, 0.8f, 1f, 1f);
		public static readonly Vector4 DisabledTextColor = new(0.6f, 0.6f, 0.6f, 1f);
		public static readonly Vector4 WarningColor = new(1f, 0.8f, 0f, 1f);
		public static readonly Vector4 EnabledIndicatorColor = new(1f, 0.3f, 0.3f, 1f);
		public static readonly Vector4 DisabledIndicatorColor = new(0.3f, 0.8f, 0.3f, 1f);
		public static readonly Vector4 HelpTextColor = new(0.4f, 0.4f, 0.4f, 1f);
		public static readonly Vector4 SubtleTextColor = new(0.5f, 0.5f, 0.5f, 1f);
		public static readonly Vector4 ModalSubtextColor = new(0.7f, 0.7f, 0.7f, 1f);

		public static float ComboWidth => 150 * ImGuiHelpers.GlobalScale;
		public static float ButtonWidth => 80 * ImGuiHelpers.GlobalScale;
		public static float SmallButtonWidth => 60 * ImGuiHelpers.GlobalScale;
		public static float LargeButtonWidth => 100 * ImGuiHelpers.GlobalScale;
		public static float CheckboxColumnWidth => 120 * ImGuiHelpers.GlobalScale;
		public static float StandardPadding => 10 * ImGuiHelpers.GlobalScale;
		public static float SmallPadding => 5 * ImGuiHelpers.GlobalScale;
	}

	private readonly List<TextEditorWindow> _activeEditors = [];
	private Guid? _pendingDeleteGuid;
	private string? _pendingDeleteName;

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

	#region Public API

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

	public void CloseEditors()
	{
		foreach (var editor in _activeEditors)
		{
			editor.IsOpen = false;
		}
		_activeEditors.Clear();
	}

	#endregion

	#region Main Draw

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
			ImGui.TextColored(UIConstants.DisabledTextColor, "Click + to create your first overlay.");
		}

		// Draw active editor windows
		DrawActiveEditors();
	}

	private void RenderOverlaySettings(OverlayEditState state)
	{
		ImGui.PushID(state.Guid.ToString());

		ImGuiHelpers.ScaledDummy(5);

		RenderGeneralSection(state);
		RenderRenderingSection(state);
		RenderBehaviorSection(state);
		RenderVisibilitySection(state);
		RenderPositioningSection(state);
		RenderAdvancedSection(state);

		ImGui.PopID();
	}

	#endregion

	#region Section Renderers

	private static void RenderGeneralSection(OverlayEditState state)
	{
		ImGui.Separator();
		ImGui.TextColored(UIConstants.SectionHeaderColor, "General");
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
	}

	private static void RenderRenderingSection(OverlayEditState state)
	{
		ImGui.Separator();
		ImGui.TextColored(UIConstants.SectionHeaderColor, "Rendering");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		ImGui.SliderFloat("Zoom", ref state.Zoom, 10f, 500f, "%.0f%%");
		ImGui.SliderFloat("Opacity", ref state.Opacity, 10f, 100f, "%.0f%%");
		ImGui.SliderInt("Framerate", ref state.Framerate, 1, 144);

		ImGuiHelpers.ScaledDummy(5);

		ImGui.Unindent();
	}

	private static void RenderBehaviorSection(OverlayEditState state)
	{
		ImGui.Separator();
		ImGui.TextColored(UIConstants.SectionHeaderColor, "Behavior");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		float availWidth = ImGui.GetContentRegionAvail().X;
		bool useColumns = availWidth > UIConstants.CheckboxColumnWidth * 2 + 20 * ImGuiHelpers.GlobalScale;
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
	}

	private static void RenderVisibilitySection(OverlayEditState state)
	{
		ImGui.Separator();
		ImGui.TextColored(UIConstants.SectionHeaderColor, "Visibility");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		// Base Visibility dropdown
		ImGui.Text("Base Visibility");
		ImGui.SetNextItemWidth(UIConstants.ComboWidth);
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

		// Delegate to the dedicated rules editor
		VisibilityRulesEditor.Draw(state.VisibilityRules);

		ImGuiHelpers.ScaledDummy(5);

		ImGui.Unindent();
	}

	private static void RenderPositioningSection(OverlayEditState state)
	{
		ImGui.Separator();
		ImGui.TextColored(UIConstants.SectionHeaderColor, "Positioning");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		// Screen Position dropdown
		ImGui.Text("Screen Position");
		ImGui.SetNextItemWidth(UIConstants.ComboWidth);
		if (ImGui.BeginCombo("##ScreenPosition", GetScreenPositionDisplayName(state.Position)))
		{
			foreach (var position in Enum.GetValues<ScreenPosition>())
			{
				if (ImGui.Selectable(GetScreenPositionDisplayName(position), state.Position == position))
				{
					state.Position = position;
				}
			}
			ImGui.EndCombo();
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("System: Positioned by Dalamud window system\nFullscreen: Cover entire screen");

		ImGuiHelpers.ScaledDummy(5);

		// Determine if controls should be disabled
		bool isSystem = state.Position == ScreenPosition.System;
		bool isFullscreen = state.Position == ScreenPosition.Fullscreen;
		bool disableControls = isSystem || isFullscreen;

		// Side-by-side layout: Position/Size controls on left, Preview on right
		float posAvailWidth = ImGui.GetContentRegionAvail().X;
		float controlsWidth = 260 * ImGuiHelpers.GlobalScale;
		float previewWidth = 200 * ImGuiHelpers.GlobalScale;
		bool useSideBySide = posAvailWidth > (controlsWidth + previewWidth + 20 * ImGuiHelpers.GlobalScale);

		if (useSideBySide && !isSystem)
		{
			ImGui.BeginGroup();
		}

		// Position and Size controls (always visible, disabled when System or Fullscreen)
		RenderPositionSizeControls(state, disableControls);

		if (useSideBySide && !isSystem)
		{
			ImGui.EndGroup();
			ImGui.SameLine();
			ImGuiHelpers.ScaledDummy(10, 0);
			ImGui.SameLine();
		}

		// Draw position visualizer (hidden for System, shown for all others including Fullscreen)
		if (!isSystem)
		{
			ScreenPosition? clickedPosition;
			if (useSideBySide)
			{
				ImGui.BeginGroup();
				ImGui.Text("Preview");
				ImGuiHelpers.ScaledDummy(2);
				clickedPosition = PositionVisualizer.Draw(
					state.Position,
					state.PositionX,
					state.PositionY,
					state.PositionWidth,
					state.PositionHeight,
					previewWidth / ImGuiHelpers.GlobalScale);
				ImGui.EndGroup();
			}
			else
			{
				ImGuiHelpers.ScaledDummy(10);
				ImGui.Text("Preview");
				ImGuiHelpers.ScaledDummy(2);
				clickedPosition = PositionVisualizer.Draw(
					state.Position,
					state.PositionX,
					state.PositionY,
					state.PositionWidth,
					state.PositionHeight);
			}

			// Handle anchor click - update dropdown selection
			if (clickedPosition.HasValue)
			{
				state.Position = clickedPosition.Value;
			}
		}

		// Sync legacy fullscreen flag with Position enum
		// This ensures backward compatibility with old config format
		state.Fullscreen = state.Position == ScreenPosition.Fullscreen;

		ImGuiHelpers.ScaledDummy(5);

		ImGui.Unindent();
	}

	private static void RenderPositionSizeControls(OverlayEditState state, bool disabled)
	{
		ImGui.Text("Position and Size");
		ImGuiHelpers.ScaledDummy(2);

		if (disabled)
			ImGui.BeginDisabled();

		float posLabelWidth = 80 * ImGuiHelpers.GlobalScale;
		float posInputWidth = 100 * ImGuiHelpers.GlobalScale;
		float posBtnWidth = 24 * ImGuiHelpers.GlobalScale;
		float stepSize = 1f;

		// X Offset
		ImGui.Text("X Offset:");
		ImGui.SameLine(posLabelWidth);
		ImGui.SetNextItemWidth(posInputWidth);
		ImGui.InputFloat("##PositionX", ref state.PositionX, 0f, 0f, "%.1f%%");
		state.PositionX = Math.Clamp(state.PositionX, -100f, 100f);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Horizontal offset from anchor point (% of screen width)");
		ImGui.SameLine();
		if (ImGui.Button("-##PosX", new Vector2(posBtnWidth, 0)) || (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left)))
		{
			if (ShouldRepeatTrigger())
				state.PositionX = Math.Max(-100f, state.PositionX - stepSize);
		}
		ImGui.SameLine();
		if (ImGui.Button("+##PosX", new Vector2(posBtnWidth, 0)) || (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left)))
		{
			if (ShouldRepeatTrigger())
				state.PositionX = Math.Min(100f, state.PositionX + stepSize);
		}

		// Y Offset
		ImGui.Text("Y Offset:");
		ImGui.SameLine(posLabelWidth);
		ImGui.SetNextItemWidth(posInputWidth);
		ImGui.InputFloat("##PositionY", ref state.PositionY, 0f, 0f, "%.1f%%");
		state.PositionY = Math.Clamp(state.PositionY, -100f, 100f);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Vertical offset from anchor point (% of screen height)");
		ImGui.SameLine();
		if (ImGui.Button("-##PosY", new Vector2(posBtnWidth, 0)) || (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left)))
		{
			if (ShouldRepeatTrigger())
				state.PositionY = Math.Max(-100f, state.PositionY - stepSize);
		}
		ImGui.SameLine();
		if (ImGui.Button("+##PosY", new Vector2(posBtnWidth, 0)) || (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left)))
		{
			if (ShouldRepeatTrigger())
				state.PositionY = Math.Min(100f, state.PositionY + stepSize);
		}

		// Width
		ImGui.Text("Width:");
		ImGui.SameLine(posLabelWidth);
		ImGui.SetNextItemWidth(posInputWidth);
		ImGui.InputFloat("##PositionWidth", ref state.PositionWidth, 0f, 0f, "%.1f%%");
		state.PositionWidth = Math.Clamp(state.PositionWidth, 1f, 100f);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Overlay width (% of screen width)");
		ImGui.SameLine();
		if (ImGui.Button("-##PosW", new Vector2(posBtnWidth, 0)) || (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left)))
		{
			if (ShouldRepeatTrigger())
				state.PositionWidth = Math.Max(1f, state.PositionWidth - stepSize);
		}
		ImGui.SameLine();
		if (ImGui.Button("+##PosW", new Vector2(posBtnWidth, 0)) || (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left)))
		{
			if (ShouldRepeatTrigger())
				state.PositionWidth = Math.Min(100f, state.PositionWidth + stepSize);
		}

		// Height
		ImGui.Text("Height:");
		ImGui.SameLine(posLabelWidth);
		ImGui.SetNextItemWidth(posInputWidth);
		ImGui.InputFloat("##PositionHeight", ref state.PositionHeight, 0f, 0f, "%.1f%%");
		state.PositionHeight = Math.Clamp(state.PositionHeight, 1f, 100f);
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Overlay height (% of screen height)");
		ImGui.SameLine();
		if (ImGui.Button("-##PosH", new Vector2(posBtnWidth, 0)) || (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left)))
		{
			if (ShouldRepeatTrigger())
				state.PositionHeight = Math.Max(1f, state.PositionHeight - stepSize);
		}
		ImGui.SameLine();
		if (ImGui.Button("+##PosH", new Vector2(posBtnWidth, 0)) || (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left)))
		{
			if (ShouldRepeatTrigger())
				state.PositionHeight = Math.Min(100f, state.PositionHeight + stepSize);
		}

		if (disabled)
			ImGui.EndDisabled();
	}

	private void RenderAdvancedSection(OverlayEditState state)
	{
		ImGui.Separator();
		ImGui.TextColored(UIConstants.SectionHeaderColor, "Advanced");
		ImGui.Spacing();

		ImGui.Indent();
		ImGuiHelpers.ScaledDummy(5);

		// Action buttons
		if (ImGui.Button("Reload", new Vector2(UIConstants.ButtonWidth, 0)))
		{
			_overlayManager.NavigateOverlay(state.Guid, state.Url);
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Reload the overlay page");

		ImGui.SameLine();

		if (ImGui.Button("Dev Tools", new Vector2(UIConstants.LargeButtonWidth, 0)))
		{
			_overlayManager.OpenDevTools(state.Guid);
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Open browser developer tools");

		ImGuiHelpers.ScaledDummy(5);

		float labelWidth = 160 * ImGuiHelpers.GlobalScale;

		// Custom CSS
		bool hasCss = !string.IsNullOrWhiteSpace(state.CustomCss);
		ImGui.Text("Custom CSS:");
		ImGui.SameLine();
		ImGui.TextColored(hasCss ? UIConstants.EnabledIndicatorColor : UIConstants.DisabledIndicatorColor,
			hasCss ? "enabled" : "disabled");
		ImGui.SameLine(labelWidth);
		if (ImGui.Button("Edit##CustomCss", new Vector2(UIConstants.SmallButtonWidth, 0)))
		{
			OpenEditor($"Editing Custom CSS for {state.Name}", state.CustomCss, code => state.CustomCss = code);
		}

		// Custom JS
		bool hasJs = !string.IsNullOrWhiteSpace(state.CustomJs);
		ImGui.Text("Custom JS:");
		ImGui.SameLine();
		ImGui.TextColored(hasJs ? UIConstants.EnabledIndicatorColor : UIConstants.DisabledIndicatorColor,
			hasJs ? "enabled" : "disabled");
		ImGui.SameLine(labelWidth);
		if (ImGui.Button("Edit##CustomJs", new Vector2(UIConstants.SmallButtonWidth, 0)))
		{
			OpenEditor($"Editing Custom JS for {state.Name}", state.CustomJs, code => state.CustomJs = code);
		}

		ImGuiHelpers.ScaledDummy(5);

		ImGui.Unindent();
	}

	#endregion

	#region Helper Methods

	/// <summary>
	/// Checks if a repeat button should trigger.
	/// Initial click triggers immediately, then repeats after 0.5s hold with 0.1s interval.
	/// </summary>
	private static bool ShouldRepeatTrigger()
	{
		float holdDuration = ImGui.GetIO().MouseDownDuration[0];
		if (ImGui.IsItemActivated())
			return true;
		if (holdDuration > 0.5f)
		{
			// Trigger every 0.1 seconds (reduced from every frame)
			float repeatInterval = 0.1f;
			float timeSinceStart = holdDuration - 0.5f;
			float prevTime = timeSinceStart - ImGui.GetIO().DeltaTime;
			return (int)(timeSinceStart / repeatInterval) > (int)(prevTime / repeatInterval);
		}
		return false;
	}

	private static string GetScreenPositionDisplayName(ScreenPosition position)
	{
		return position switch
		{
			ScreenPosition.System => "System",
			ScreenPosition.Fullscreen => "Fullscreen",
			ScreenPosition.TopLeft => "Top Left",
			ScreenPosition.Top => "Top",
			ScreenPosition.TopRight => "Top Right",
			ScreenPosition.CenterLeft => "Center Left",
			ScreenPosition.Center => "Center",
			ScreenPosition.CenterRight => "Center Right",
			ScreenPosition.BottomLeft => "Bottom Left",
			ScreenPosition.BottomCenter => "Bottom Center",
			ScreenPosition.BottomRight => "Bottom Right",
			_ => position.ToString()
		};
	}

	private void OpenEditor(string title, string currentCode, Action<string> onSave)
	{
		var window = new TextEditorWindow(title, currentCode, onSave, () => { });
		_activeEditors.Add(window);
	}

	private void DrawActiveEditors()
	{
		for (int i = _activeEditors.Count - 1; i >= 0; i--)
		{
			var editor = _activeEditors[i];
			if (!editor.IsOpen)
			{
				_activeEditors.RemoveAt(i);
				continue;
			}

			bool open = true;
			editor.PreDraw();
			ImGui.SetNextWindowSize(editor.SizeConstraints!.Value.MinimumSize, ImGuiCond.FirstUseEver);
			if (ImGui.Begin(editor.WindowName, ref open, editor.Flags))
			{
				editor.Draw();
			}
			ImGui.End();

			// Close if X button was clicked (open became false) or editor closed itself
			if (!open)
				editor.IsOpen = false;
		}
	}

	#endregion

	#region Delete Confirmation

	private void RenderDeleteConfirmationPopup()
	{
		Vector2 center = ImGui.GetMainViewport().GetCenter();
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

		if (ImGui.BeginPopupModal("Delete Overlay?", ImGuiWindowFlags.AlwaysAutoResize))
		{
			float padding = UIConstants.StandardPadding;
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
			ImGui.TextColored(UIConstants.ModalSubtextColor, "This action cannot be undone.");
			ImGui.EndGroup();

			ImGui.SameLine();
			ImGui.Dummy(new Vector2(padding, 0));

			ImGui.Dummy(new Vector2(0, padding));
			ImGui.Separator();
			ImGui.Dummy(new Vector2(0, padding / 2));

			ImGui.Dummy(new Vector2(padding, 0));
			ImGui.SameLine();

			if (ImGui.Button("Delete", new Vector2(UIConstants.ButtonWidth, 0)))
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

			if (ImGui.Button("Cancel", new Vector2(UIConstants.ButtonWidth, 0)))
			{
				_pendingDeleteGuid = null;
				_pendingDeleteName = null;
				ImGui.CloseCurrentPopup();
			}

			ImGui.Dummy(new Vector2(0, padding / 2));

			ImGui.EndPopup();
		}
	}

	#endregion
}
