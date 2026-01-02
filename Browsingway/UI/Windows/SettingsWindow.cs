using Browsingway.Services;
using Browsingway.UI.Windows.SettingsTabs;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
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
	private readonly OverlayManager _overlayManager;
	private readonly Func<bool> _getActAvailable;
	private readonly Func<int> _getOverlayCount;
	private readonly ISharedImmediateTexture? _logoTexture;
	private readonly string _version;

	private Configuration _config;

	// Tabs
	private enum MainTab { Settings, Overlays }

	private MainTab _currentTab = MainTab.Settings;

	private readonly GeneralSettingsTab _generalTab;
	private OverlaySettingsTab _overlayTab;
	private readonly ISharedImmediateTexture? _warningTexture;

	// Layout constants
	private static float SidebarWidth => 120f * ImGuiHelpers.GlobalScale;

	public SettingsWindow(
		IServiceContainer services,
		OverlayManager overlayManager,
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

		// Load textures
		_logoTexture = _services.TextureProvider.GetFromFile(Path.Combine(pluginDir, "icon.png"));
		_warningTexture = _services.TextureProvider.GetFromFile(Path.Combine(pluginDir, "dead.png"));

		// Get version from assembly
		var assembly = Assembly.GetExecutingAssembly();
		_version = assembly.GetName().Version?.ToString(3) ?? "1.0.0";

		// Initialize tabs
		_generalTab = new GeneralSettingsTab(_config);
		_overlayTab = new OverlaySettingsTab(_config, _overlayManager, _getActAvailable, _warningTexture);

		SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(600, 400), MaximumSize = new Vector2(1200, 800) };

		_services.PluginInterface.UiBuilder.OpenConfigUi += Open;
	}

	public void Dispose()
	{
		_services.PluginInterface.UiBuilder.OpenConfigUi -= Open;
	}

	public void Open() => IsOpen = true;

	public override void OnClose()
	{
		_overlayTab.CloseEditors();
		base.OnClose();
	}

	public override void Draw()
	{
		bool dirty = IsDirty();
		WindowName = dirty ? "Browsingway Settings *###BrowsingwaySettings" : "Browsingway Settings###BrowsingwaySettings";

		float windowHeight = ImGui.GetContentRegionAvail().Y;
		float padding = 10.0f * ImGuiHelpers.GlobalScale;
		float bottomBarHeight = ImGui.GetFrameHeightWithSpacing() + (padding * 2) + ImGui.GetStyle().ItemSpacing.Y;

		// Main layout: Sidebar | Content
		RenderSidebar(windowHeight - bottomBarHeight);

		ImGui.SameLine();

		// Content area
		ImGui.BeginChild("ContentArea", new Vector2(0, windowHeight - bottomBarHeight));
		RenderTabBar();
		RenderTabContent();
		ImGui.EndChild();

		// Bottom bar with Save/Revert
		RenderBottomBar(dirty);
	}

	private void RenderSidebar(float height)
	{
		ImGui.BeginChild("Sidebar", new Vector2(SidebarWidth, height));
		ImGui.BeginGroup();

		// Logo
		if (_logoTexture != null)
		{
			float logoSize = Math.Min(SidebarWidth - 5 * ImGuiHelpers.GlobalScale, 120 * ImGuiHelpers.GlobalScale);
			float logoX = (SidebarWidth - logoSize) / 2 - ImGui.GetStyle().WindowPadding.X;
			ImGui.SetCursorPosX(logoX);
			ImGui.Image(_logoTexture.GetWrapOrEmpty().Handle, new Vector2(logoSize, logoSize));
		}

		ImGui.Spacing();
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

		int activeCount = _config.Overlays.Count(o => o.BaseVisibility != BaseVisibility.Disabled);
		ImGui.Text($"Active: {activeCount}");

		bool actRunning = _getActAvailable();
		ImGui.TextColored(
			actRunning ? new Vector4(0.3f, 0.8f, 0.3f, 1f) : new Vector4(0.5f, 0.5f, 0.5f, 1f),
			actRunning ? "ACT: Running" : "ACT: Not found");

		ImGui.EndGroup();
		ImGui.EndChild();
	}

	private void RenderTabBar()
	{
		if (ImGui.BeginTabBar("MainTabs"))
		{
			if (ImGui.BeginTabItem("Settings"))
			{
				if (_currentTab != MainTab.Settings)
				{
					_overlayTab.CloseEditors();
				}

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
		ImGui.BeginGroup();

		switch (_currentTab)
		{
			case MainTab.Settings:
				_generalTab.Draw();
				break;
			case MainTab.Overlays:
				_overlayTab.Draw();
				break;
		}

		ImGui.EndGroup();
		ImGui.EndChild();
	}

	private void RenderBottomBar(bool dirty)
	{
		ImGuiHelpers.ScaledDummy(5);
		ImGui.Separator();
		ImGuiHelpers.ScaledDummy(5);

		float buttonWidth = 80 * ImGuiHelpers.GlobalScale;

		if (!dirty)
		{
			ImGui.BeginDisabled();
		}

		if (ImGui.Button("Save", new Vector2(buttonWidth, 0)))
		{
			SaveAndApply();
		}

		ImGui.SameLine();
		if (ImGui.Button("Revert", new Vector2(buttonWidth, 0)))
		{
			RevertChanges();
		}

		if (!dirty)
		{
			ImGui.EndDisabled();
		}
	}

	private bool IsDirty()
	{
		return _overlayTab.IsDirty();
	}

	private void SaveAndApply()
	{
		_overlayTab.ApplyChanges();

		// Save configuration to disk
		_services.PluginInterface.SavePluginConfig(_config);

		// Reload all overlays
		_overlayManager.ReloadAllFromConfig(_config, _getActAvailable());
	}

	private void RevertChanges()
	{
		// Reload config from disk
		_config = _services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

		// Re-initialize overlay tab with new config
		// Note: We're not recreating the tab object to keep things simple, just refreshing its state
		// Ideally we should probably pass the new config to the tab, but for now we might need to recreate it 
		// or add a method to update config. 
		// Let's modify OverlaySettingsTab to support state refresh or just create a new one.
		// Since we can't easily modify the tab constructor arguments without changing the class, let's just make sure
		// the tab uses the config reference we passed. Wait, if we change _config reference here, the tab still holds the old one.
		// We need to update the tab's config reference.

		// Correction: I should update OverlaySettingsTab to allow updating config or just look at the code I wrote for OverlaySettingsTab.
		// OverlaySettingsTab takes Configuration in constructor and stores it in _config field.
		// So if I replace _config here, I need to propagate it.
		// Easier approach: Re-instantiate the tab.
		_overlayTab = new OverlaySettingsTab(_config, _overlayManager, _getActAvailable, _warningTexture);
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
		_overlayTab.RefreshEditStates();
	}
}