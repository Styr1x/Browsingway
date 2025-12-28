using Browsingway.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Browsingway.UI.Windows;

/// <summary>
/// Settings window with tabbed layout.
/// Layout: Left sidebar (logo/stats) | Main area (Settings tab / Overlays tab)
/// </summary>
internal sealed partial class SettingsWindow : Window, IDisposable
{
	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespaceRegex();

	private readonly IServiceContainer _services;
	private readonly IOverlayManager _overlayManager;
	private readonly Func<bool> _getActAvailable;
	private readonly Func<int> _getOverlayCount;
	private readonly ISharedImmediateTexture? _logoTexture;
	private readonly string _version;

	private Configuration _config;

	// Tab state
	private enum MainTab { Settings, Overlays }
	private MainTab _currentTab = MainTab.Settings;

	// Edit state - holds pending changes until Save is pressed
	private readonly Dictionary<Guid, OverlayEditState> _editStates = [];
	private Guid? _selectedOverlayGuid;
	private readonly HashSet<Guid> _newOverlays = []; // Track newly added overlays

	// Layout constants
	private const float SidebarWidth = 140f;

	public SettingsWindow(
		IServiceContainer services,
		IOverlayManager overlayManager,
		Configuration config,
		Func<bool> getActAvailable,
		Func<int> getOverlayCount,
		string pluginDir)
		: base("Browsingway Settings###BrowsingwaySettings")
	{
		_services = services;
		_overlayManager = overlayManager;
		_config = config;
		_getActAvailable = getActAvailable;
		_getOverlayCount = getOverlayCount;

		// Load logo texture
		_logoTexture = _services.TextureProvider.GetFromFile(Path.Combine(pluginDir, "icon.png"));

		// Get version from assembly
		var assembly = Assembly.GetExecutingAssembly();
		_version = assembly.GetName().Version?.ToString(3) ?? "1.0.0";

		// Initialize edit states from config
		RefreshEditStates();

		SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(600, 400),
			MaximumSize = new Vector2(1200, 800)
		};

		Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse;
		
		_services.PluginInterface.UiBuilder.OpenConfigUi += Open;
	}

	public void Dispose()
	{
		_services.PluginInterface.UiBuilder.OpenConfigUi -= Open;
	}

	public void Open() => IsOpen = true;

	public override void Draw()
	{
		bool dirty = IsDirty();
		WindowName = dirty ? "Browsingway Settings *###BrowsingwaySettings" : "Browsingway Settings###BrowsingwaySettings";
		Flags = ImGuiWindowFlags.None
		        | ImGuiWindowFlags.NoTitleBar
		        | ImGuiWindowFlags.NoCollapse
		        | ImGuiWindowFlags.NoScrollbar
		        | ImGuiWindowFlags.NoScrollWithMouse
		        | ImGuiWindowFlags.NoBringToFrontOnFocus
		        | ImGuiWindowFlags.NoFocusOnAppearing;

		float windowHeight = ImGui.GetContentRegionAvail().Y;
		float bottomBarHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;

		// Main layout: Sidebar | Content
		RenderSidebar(windowHeight - bottomBarHeight);

		ImGui.SameLine();

		// Content area
		ImGui.BeginChild("ContentArea", new Vector2(0, windowHeight - bottomBarHeight), false);
		RenderTabBar();
		RenderTabContent();
		ImGui.EndChild();

		// Bottom bar with Save/Revert
		RenderBottomBar(dirty);
	}

	private void RenderSidebar(float height)
	{
		ImGui.BeginChild("Sidebar", new Vector2(SidebarWidth, height), true);

		// Logo
		if (_logoTexture != null)
		{
			float logoSize = Math.Min(SidebarWidth - 20, 80);
			float logoX = (SidebarWidth - logoSize) / 2 - ImGui.GetStyle().WindowPadding.X;
			ImGui.SetCursorPosX(logoX);
			ImGui.Image(_logoTexture.GetWrapOrEmpty().Handle, new Vector2(logoSize, logoSize));
		}

		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();

		// Version
		ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Version");
		ImGui.Text(_version);

		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();

		// Stats
		ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Stats");

		int overlayCount = _getOverlayCount();
		ImGui.Text($"Overlays: {overlayCount}");

		int activeCount = _config.Inlays.Count(o => !o.Disabled);
		ImGui.Text($"Active: {activeCount}");

		bool actRunning = _getActAvailable();
		ImGui.TextColored(
			actRunning ? new Vector4(0.3f, 0.8f, 0.3f, 1f) : new Vector4(0.5f, 0.5f, 0.5f, 1f),
			actRunning ? "ACT: Running" : "ACT: Not found");

		ImGui.EndChild();
	}

	private void RenderTabBar()
	{
		if (ImGui.BeginTabBar("MainTabs"))
		{
			if (ImGui.BeginTabItem("Settings"))
			{
				_currentTab = MainTab.Settings;
				ImGui.EndTabItem();
			}

			if (ImGui.BeginTabItem("Overlays"))
			{
				_currentTab = MainTab.Overlays;
				ImGui.EndTabItem();
			}

			ImGui.EndTabBar();
		}
	}

	private void RenderTabContent()
	{
		ImGui.BeginChild("TabContent", Vector2.Zero, true);

		switch (_currentTab)
		{
			case MainTab.Settings:
				RenderSettingsTab();
				break;
			case MainTab.Overlays:
				RenderOverlaysTab();
				break;
		}

		ImGui.EndChild();
	}

	private void RenderSettingsTab()
	{
		ImGui.TextWrapped("Configure Browsingway settings and view command help.");

		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();

		if (ImGui.CollapsingHeader("Command Help", ImGuiTreeNodeFlags.DefaultOpen))
		{
			ImGui.Indent();

			ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "/bw config");
			ImGui.TextWrapped("Open this configuration window.");

			ImGui.Spacing();

			ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "/bw overlay [name] [setting] [value]");
			ImGui.TextWrapped(
				"Change a setting for an overlay.\n\n" +
				"Settings:\n" +
				"  url <string>         - Set overlay URL\n" +
				"  disabled <bool>      - Enable/disable overlay\n" +
				"  muted <bool>         - Mute/unmute audio\n" +
				"  act <bool>           - ACT optimizations\n" +
				"  locked <bool>        - Lock position/size\n" +
				"  hidden <bool>        - Hide overlay\n" +
				"  typethrough <bool>   - Pass keyboard input\n" +
				"  clickthrough <bool>  - Pass mouse input\n" +
				"  fullscreen <bool>    - Fullscreen mode\n" +
				"  reload               - Reload the overlay\n\n" +
				"Boolean values: on, off, toggle");

			ImGui.Unindent();
		}
	}

	private void RenderOverlaysTab()
	{
		Guid? overlayToDelete = null;

		if (ImGui.BeginTabBar("OverlayTabs", ImGuiTabBarFlags.FittingPolicyScroll))
		{
			foreach (var overlay in _config.Inlays)
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
				if (ImGui.BeginTabItem($"{editState.Name}##{overlay.Guid}", ref tabOpen, flags))
				{
					_selectedOverlayGuid = overlay.Guid;

					ImGui.BeginChild("OverlaySettings", Vector2.Zero, false);
					RenderOverlaySettings(editState);
					ImGui.EndChild();

					ImGui.EndTabItem();
				}

				if (!tabOpen)
				{
					overlayToDelete = overlay.Guid;
				}
			}

			// Add button
			if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Trailing))
			{
				var newOverlay = new InlayConfiguration();
				_config.Inlays.Add(newOverlay);
				_editStates[newOverlay.Guid] = OverlayEditState.FromConfig(newOverlay);
				_selectedOverlayGuid = newOverlay.Guid;
				_newOverlays.Add(newOverlay.Guid);
			}
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Add new overlay");

			ImGui.EndTabBar();
		}

		if (overlayToDelete.HasValue)
		{
			var overlay = _config.Inlays.Find(o => o.Guid == overlayToDelete.Value);
			if (overlay != null)
			{
				_config.Inlays.Remove(overlay);
				_editStates.Remove(overlayToDelete.Value);
				if (_selectedOverlayGuid == overlayToDelete.Value)
					_selectedOverlayGuid = _config.Inlays.FirstOrDefault()?.Guid;
			}
		}

		if (_config.Inlays.Count == 0)
		{
			ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Click + to create your first overlay.");
		}
	}

	private void RenderOverlaySettings(OverlayEditState state)
	{
		ImGui.PushID(state.Guid.ToString());

		// Basic settings
		ImGui.InputText("Name", ref state.Name, 100);

		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
		string commandName = WhitespaceRegex().Replace(state.Name, "").ToLower();
		ImGui.InputText("Command Name", ref commandName, 100);
		ImGui.PopStyleVar();

		ImGui.InputText("URL", ref state.Url, 1000);

		ImGui.Spacing();

		// Rendering settings
		if (ImGui.CollapsingHeader("Rendering", ImGuiTreeNodeFlags.DefaultOpen))
		{
			ImGui.Indent();

			ImGui.SliderFloat("Zoom", ref state.Zoom, 10f, 500f, "%.0f%%");
			ImGui.SliderFloat("Opacity", ref state.Opacity, 10f, 100f, "%.0f%%");
			ImGui.SliderInt("Framerate", ref state.Framerate, 1, 144);

			ImGui.Unindent();
		}

		// Behavior settings
		if (ImGui.CollapsingHeader("Behavior", ImGuiTreeNodeFlags.DefaultOpen))
		{
			ImGui.Indent();

			float availWidth = ImGui.GetContentRegionAvail().X;
			float checkboxWidth = 120;
			bool useColumns = availWidth > checkboxWidth * 2 + 20;
			float columnOffset = useColumns ? availWidth / 2 : 0;

			ImGui.Checkbox("Disabled", ref state.Disabled);
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Completely disable this overlay");

			if (useColumns)
				ImGui.SameLine(columnOffset);

			ImGui.Checkbox("Muted", ref state.Muted);
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Mute audio playback");

			ImGui.Checkbox("Hidden", ref state.Hidden);
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Hide but keep running");

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

			ImGui.Unindent();
		}

		// Visibility settings
		if (ImGui.CollapsingHeader("Visibility"))
		{
			ImGui.Indent();

			ImGui.Checkbox("ACT/IINACT optimizations", ref state.ActOptimizations);
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Only show when ACT/IINACT is running");

			ImGui.Checkbox("Hide out of combat", ref state.HideOutOfCombat);

			if (state.HideOutOfCombat)
			{
				ImGui.SameLine();
				ImGui.SetNextItemWidth(80);
				ImGui.InputInt("##HideDelay", ref state.HideDelay);
				ImGui.SameLine();
				ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "sec delay");
			}

			ImGui.Checkbox("Hide in PvP", ref state.HideInPvP);

			ImGui.Unindent();
		}

		// Advanced settings
		if (ImGui.CollapsingHeader("Advanced"))
		{
			ImGui.Indent();

			ImGui.Checkbox("Fullscreen", ref state.Fullscreen);
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Make overlay cover the entire screen");

			ImGui.Spacing();

			ImGui.Text("Custom CSS:");
			ImGui.InputTextMultiline("##CustomCSS", ref state.CustomCss, 1000000,
				new Vector2(-1, ImGui.GetTextLineHeight() * 8));

			ImGui.Unindent();
		}

		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();

		// Action buttons
		if (ImGui.Button("Reload", new Vector2(80, 0)))
		{
			_overlayManager.NavigateOverlay(state.Guid, state.Url);
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Reload the overlay page");

		ImGui.SameLine();

		if (ImGui.Button("Dev Tools", new Vector2(80, 0)))
		{
			_overlayManager.OpenDevTools(state.Guid);
		}
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Open browser developer tools");

		ImGui.PopID();
	}

	private void RenderBottomBar(bool dirty)
	{
		ImGui.Separator();

		float buttonWidth = 80;
		float spacing = ImGui.GetStyle().ItemSpacing.X;
		float totalWidth = buttonWidth * 2 + spacing;

		ImGui.SetCursorPosX(ImGui.GetWindowWidth() - totalWidth - ImGui.GetStyle().WindowPadding.X);

		if (dirty)
		{
			if (ImGui.Button("Save", new Vector2(buttonWidth, 0)))
			{
				SaveAndApply();
			}
			ImGui.SameLine();
			if (ImGui.Button("Revert", new Vector2(buttonWidth, 0)))
			{
				RevertChanges();
			}
		}
		else
		{
			ImGui.BeginDisabled();
			ImGui.Button("Save", new Vector2(buttonWidth, 0));
			ImGui.SameLine();
			ImGui.Button("Revert", new Vector2(buttonWidth, 0));
			ImGui.EndDisabled();
		}
	}

	private bool IsDirty()
	{
		// Check for new overlays that don't exist in original config
		if (_newOverlays.Count > 0)
			return true;

		// Check each edit state against its original config
		foreach (var (guid, editState) in _editStates)
		{
			var config = _config.Inlays.Find(o => o.Guid == guid);
			if (config == null || editState.IsDifferentFrom(config))
				return true;
		}

		return false;
	}

	private void SaveAndApply()
	{
		// Apply all edit states to their configs
		foreach (var (guid, editState) in _editStates)
		{
			var config = _config.Inlays.Find(o => o.Guid == guid);
			if (config != null)
			{
				editState.ApplyTo(config);
			}
		}

		// Save configuration to disk
		_services.PluginInterface.SavePluginConfig(_config);

		// Reload all overlays
		_overlayManager.ReloadAllFromConfig(_config, _getActAvailable());

		// Clear new overlay tracking
		_newOverlays.Clear();
	}

	private void RevertChanges()
	{
		// Reload config from disk
		_config = _services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
		_selectedOverlayGuid = null;
		_newOverlays.Clear();

		// Refresh edit states from config
		RefreshEditStates();
	}

	private void RefreshEditStates()
	{
		_editStates.Clear();
		foreach (var config in _config.Inlays)
		{
			_editStates[config.Guid] = OverlayEditState.FromConfig(config);
		}
	}

	/// <summary>
	/// Called when ACT availability changes. Triggers a reload of overlays.
	/// </summary>
	public void OnActAvailabilityChanged()
	{
		_overlayManager.ReloadAllFromConfig(_config, _getActAvailable());
	}

	/// <summary>
	/// Called when config changes externally (e.g., via command).
	/// </summary>
	public void OnConfigChanged()
	{
		RefreshEditStates();
	}
}

