using Browsingway.Common.Ipc;
using Browsingway.Extensions;
using Browsingway.Interop;
using Browsingway.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Numerics;
using TerraFX.Interop.Windows;

namespace Browsingway.UI.Windows;

internal sealed class BrowserTab
{
	public Guid OverlayGuid;
	public string Name = "New Tab";
	public string Url = "about:blank";
	public string UrlInput = "about:blank";
	public ImGuiSharedTexture? Texture;
	public ImGuiMouseCursor Cursor = ImGuiMouseCursor.Arrow;
	public bool CaptureCursor;
}

internal sealed class BrowserWindow : Window, IDisposable
{
	private readonly ServiceContainer _services;
	private readonly OverlayManager _overlayManager;
	private readonly RenderProcessManager _renderProcessManager;
	private readonly ISharedImmediateTexture? _logoTexture;

	private readonly List<BrowserTab> _tabs = [];
	private int _activeTabIndex = -1;
	private bool _windowFocused;
	private bool _mouseInContent;
	private Vector2 _contentSize = Vector2.Zero;
	private Vector2 _contentPos = Vector2.Zero;

	public BrowserWindow(
		ServiceContainer services,
		OverlayManager overlayManager,
		RenderProcessManager renderProcessManager,
		string pluginDir)
		: base("Browser###BrowsingwayBrowser")
	{
		_services = services;
		_overlayManager = overlayManager;
		_renderProcessManager = renderProcessManager;

		_logoTexture = _services.TextureProvider.GetFromFile(Path.Combine(pluginDir, "icon.png"));

		SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(400, 300),
			MaximumSize = new Vector2(1920, 1080)
		};

		Size = new Vector2(800, 600);
		SizeCondition = ImGuiCond.FirstUseEver;
		IsOpen = false;

		// Subscribe to texture and cursor updates
		if (_renderProcessManager.Rpc != null)
		{
			_renderProcessManager.Rpc.UpdateTexture += OnUpdateTexture;
			_renderProcessManager.Rpc.SetCursor += OnSetCursor;
			_renderProcessManager.Rpc.UrlChanged += OnUrlChanged;
		}

		// Register as main UI window
		_services.PluginInterface.UiBuilder.OpenMainUi += Open;
	}

	public void Open() => IsOpen = true;

	public void Dispose()
	{
		// Unsubscribe from main UI callback
		_services.PluginInterface.UiBuilder.OpenMainUi -= Open;

		// Unsubscribe from RPC events
		if (_renderProcessManager.Rpc != null)
		{
			_renderProcessManager.Rpc.UpdateTexture -= OnUpdateTexture;
			_renderProcessManager.Rpc.SetCursor -= OnSetCursor;
			_renderProcessManager.Rpc.UrlChanged -= OnUrlChanged;
		}

		// Close all tabs and clean up
		CloseAllTabs();
	}

	public override void Draw()
	{
		float toolbarHeight = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().ItemSpacing.Y;

		// Top area: Icon menu + Tab bar
		DrawTabBar();

		// Navigation bar
		DrawNavigationBar();

		// Content area
		DrawContentArea(toolbarHeight);
	}

	private void DrawTabBar()
	{
		float iconSize = ImGui.GetFrameHeight();

		// Icon button with dropdown
		if (_logoTexture != null)
		{
			ImGui.Image(_logoTexture.GetWrapOrEmpty().Handle, new Vector2(iconSize, iconSize));
			if (ImGui.IsItemClicked())
			{
				ImGui.OpenPopup("BrowserMenuPopup");
			}
		}
		else
		{
			if (ImGui.Button("Menu", new Vector2(iconSize * 2, iconSize)))
			{
				ImGui.OpenPopup("BrowserMenuPopup");
			}
		}

		// Menu popup
		if (ImGui.BeginPopup("BrowserMenuPopup"))
		{
			if (ImGui.MenuItem("Close All Tabs", "", false, _tabs.Count > 0))
			{
				CloseAllTabs();
			}
			ImGui.EndPopup();
		}

		ImGui.SameLine();

		// Tab bar
		float availableWidth = ImGui.GetContentRegionAvail().X - iconSize - ImGui.GetStyle().ItemSpacing.X;
		using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(2, 0)))
		{
			for (int i = 0; i < _tabs.Count; i++)
			{
				int index = i;
				BrowserTab tab = _tabs[i];
				bool isActive = i == _activeTabIndex;

				// Tab button style
				if (isActive)
				{
					ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
				}

				// Limit tab width
				float maxTabWidth = 150 * ImGuiHelpers.GlobalScale;
				string tabLabel = tab.Name.Length > 15 ? tab.Name[..12] + "..." : tab.Name;

				if (ImGui.Button($"{tabLabel}##Tab{i}", new Vector2(0, iconSize)))
				{
					_activeTabIndex = index;
				}

				if (isActive)
				{
					ImGui.PopStyleColor();
				}

				// Tab tooltip
				if (ImGui.IsItemHovered())
				{
					ImGui.SetTooltip(tab.Url);
				}

				// Close button on the same line
				ImGui.SameLine(0, 0);
				if (ImGui.Button($"x##CloseTab{i}", new Vector2(iconSize * 0.6f, iconSize)))
				{
					CloseTab(index);
					// Adjust index if we're iterating
					if (index < _tabs.Count)
					{
						i--;
					}
				}

				ImGui.SameLine();
			}
		}

		// Add tab button
		if (ImGui.Button("+", new Vector2(iconSize, iconSize)))
		{
			AddTab();
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Open new tab");
		}
	}

	private void DrawNavigationBar()
	{
		if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
		{
			ImGui.Text("No tab selected");
			return;
		}

		BrowserTab activeTab = _tabs[_activeTabIndex];
		float buttonSize = ImGui.GetFrameHeight();

		// Back button
		if (ImGui.Button("<", new Vector2(buttonSize, buttonSize)))
		{
			_overlayManager.GoBack(activeTab.OverlayGuid);
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Go back");
		}

		ImGui.SameLine();

		// Forward button
		if (ImGui.Button(">", new Vector2(buttonSize, buttonSize)))
		{
			_overlayManager.GoForward(activeTab.OverlayGuid);
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Go forward");
		}

		ImGui.SameLine();

		// Reload button
		if (ImGui.Button("R", new Vector2(buttonSize, buttonSize)))
		{
			_overlayManager.Reload(activeTab.OverlayGuid);
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Reload (Ctrl+click to ignore cache)");
		}

		ImGui.SameLine();

		// URL input
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
		if (ImGui.InputText("##UrlInput", ref activeTab.UrlInput, 2048, ImGuiInputTextFlags.EnterReturnsTrue))
		{
			NavigateCurrentTab(activeTab.UrlInput);
		}
	}

	private void DrawContentArea(float toolbarHeight)
	{
		Vector2 contentRegion = ImGui.GetContentRegionAvail();
		_contentPos = ImGui.GetCursorScreenPos();
		_contentSize = contentRegion;

		if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
		{
			ImGui.BeginChild("EmptyContent", contentRegion, true);
			ImGuiHelpers.CenteredText("Click + to open a new tab");
			ImGui.EndChild();
			return;
		}

		BrowserTab activeTab = _tabs[_activeTabIndex];

		// Update overlay size so it renders at the correct size for this content area
		_overlayManager.SetOverlaySize(activeTab.OverlayGuid, contentRegion);

		// Content child with border
		ImGui.BeginChild("BrowserContent", contentRegion, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

		if (activeTab.Texture != null)
		{
			// Render the texture
			activeTab.Texture.Render();

			// Handle mouse input
			HandleMouseInput(activeTab);

			// Set cursor
			if (_mouseInContent && activeTab.CaptureCursor)
			{
				ImGui.SetMouseCursor(activeTab.Cursor);
			}
		}
		else
		{
			ImGuiHelpers.CenteredText("Loading...");
		}

		ImGui.EndChild();

		// Handle focus
		if (ImGui.IsItemClicked())
		{
			_windowFocused = true;
		}
	}

	private void HandleMouseInput(BrowserTab tab)
	{
		ImGuiIOPtr io = ImGui.GetIO();
		Vector2 windowPos = _contentPos;
		Vector2 mousePos = io.MousePos - windowPos;

		// Check if mouse is in content area
		bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);

		// Mouse leave detection
		if (!hovered)
		{
			if (_mouseInContent)
			{
				_mouseInContent = false;
				_renderProcessManager.Rpc?.MouseButton(new MouseButtonMessage
				{
					Guid = tab.OverlayGuid.ToByteArray(),
					X = mousePos.X,
					Y = mousePos.Y,
					Leaving = true
				}).FireAndForget(_services.PluginLog);
			}
			return;
		}

		_mouseInContent = true;

		// Encode mouse buttons
		MouseButton down = BrowserInputHelper.EncodeMouseButtons(io.MouseClicked);
		MouseButton double_ = BrowserInputHelper.EncodeMouseButtons(io.MouseDoubleClicked);
		MouseButton up = BrowserInputHelper.EncodeMouseButtons(io.MouseReleased);
		float wheelX = io.MouseWheelH;
		float wheelY = io.MouseWheel;

		// Handle focus on click
		if (down.HasFlag(MouseButton.Primary) || down.HasFlag(MouseButton.Secondary) || down.HasFlag(MouseButton.Tertiary))
		{
			_windowFocused = true;
		}

		// Skip if no changes
		if (io.MouseDelta == Vector2.Zero && down == MouseButton.None && double_ == MouseButton.None && up == MouseButton.None && wheelX == 0 && wheelY == 0)
		{
			return;
		}

		InputModifier modifier = InputModifier.None;
		if (io.KeyShift) modifier |= InputModifier.Shift;
		if (io.KeyCtrl) modifier |= InputModifier.Control;
		if (io.KeyAlt) modifier |= InputModifier.Alt;

		_renderProcessManager.Rpc?.MouseButton(new MouseButtonMessage
		{
			Guid = tab.OverlayGuid.ToByteArray(),
			X = mousePos.X,
			Y = mousePos.Y,
			Down = down,
			Double = double_,
			Up = up,
			WheelX = wheelX,
			WheelY = wheelY,
			Modifier = modifier
		}).FireAndForget(_services.PluginLog);
	}

	public WndProcResult WndProcMessage(WindowsMessage msg, ulong wParam, long lParam)
	{
		// Only handle keyboard if we're focused and have an active tab
		if (!_windowFocused || _activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
		{
			return WndProcResult.NotHandled;
		}

		if (msg == WindowsMessage.WM_LBUTTONDOWN)
		{
			// Click outside - lose focus
			if (!_mouseInContent)
			{
				_windowFocused = false;
				return WndProcResult.NotHandled;
			}
		}

		KeyEventType? eventType = msg switch
		{
			WindowsMessage.WM_KEYDOWN or WindowsMessage.WM_SYSKEYDOWN => KeyEventType.KeyDown,
			WindowsMessage.WM_KEYUP or WindowsMessage.WM_SYSKEYUP => KeyEventType.KeyUp,
			WindowsMessage.WM_CHAR or WindowsMessage.WM_SYSCHAR => KeyEventType.Character,
			_ => null
		};

		if (eventType == null)
		{
			return WndProcResult.NotHandled;
		}

		BrowserTab activeTab = _tabs[_activeTabIndex];
		_renderProcessManager.Rpc?.KeyEvent(activeTab.OverlayGuid, (int)msg, (int)wParam, (int)lParam)
			.FireAndForget(_services.PluginLog);

		return WndProcResult.HandledWith(0);
	}

	private void AddTab()
	{
		// Create ephemeral overlay with Hidden visibility (renders but doesn't display in its own window)
		OverlayConfiguration config = new()
		{
			Guid = Guid.NewGuid(),
			Name = "Browser Tab",
			Url = "about:blank",
			Zoom = 100f,
			Opacity = 100f,
			Framerate = 60,
			BaseVisibility = BaseVisibility.Hidden,
			PositionMode = ScreenPositionMode.System,
			PositionWidth = 50f,
			PositionHeight = 50f,
			ClickThrough = true,
			CustomCss = "",
			CustomJs = ""
		};

		Guid guid = _overlayManager.AddEphemeralOverlay(config);

		BrowserTab tab = new()
		{
			OverlayGuid = guid,
			Name = "New Tab",
			Url = "about:blank",
			UrlInput = "about:blank"
		};

		_tabs.Add(tab);
		_activeTabIndex = _tabs.Count - 1;

		// Trigger sync to create the overlay in the renderer
		_overlayManager.SyncNow();
	}

	private void CloseTab(int index)
	{
		if (index < 0 || index >= _tabs.Count)
		{
			return;
		}

		BrowserTab tab = _tabs[index];

		// Remove ephemeral overlay
		_overlayManager.RemoveEphemeralOverlay(tab.OverlayGuid);

		// Dispose texture
		tab.Texture?.Dispose();

		// Remove from list
		_tabs.RemoveAt(index);

		// Adjust active index
		if (_activeTabIndex >= _tabs.Count)
		{
			_activeTabIndex = _tabs.Count - 1;
		}
	}

	private void CloseAllTabs()
	{
		for (int i = _tabs.Count - 1; i >= 0; i--)
		{
			CloseTab(i);
		}
		_activeTabIndex = -1;
	}

	private void NavigateCurrentTab(string url)
	{
		if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
		{
			return;
		}

		BrowserTab tab = _tabs[_activeTabIndex];

		// Add protocol if missing
		if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("about:"))
		{
			url = "https://" + url;
		}

		tab.Url = url;
		tab.UrlInput = url;
		tab.Name = GetDomainFromUrl(url);

		_overlayManager.NavigateOverlay(tab.OverlayGuid, url);
	}

	private static string GetDomainFromUrl(string url)
	{
		try
		{
			Uri uri = new(url);
			return uri.Host;
		}
		catch
		{
			return url.Length > 20 ? url[..17] + "..." : url;
		}
	}

	private void OnUpdateTexture(UpdateTextureMessage msg)
	{
		_services.Framework.RunOnFrameworkThread(() =>
		{
			Guid guid = new(msg.Guid.Span);
			BrowserTab? tab = _tabs.FirstOrDefault(t => t.OverlayGuid == guid);
			if (tab == null) return;

			// Create new texture first, only dispose old one on success
			ImGuiSharedTexture? oldTexture = tab.Texture;

			try
			{
				tab.Texture = new ImGuiSharedTexture((HANDLE)msg.TextureHandle);
				oldTexture?.Dispose();
			}
			catch (Exception e)
			{
				_services.PluginLog.Error(e, "Failed to create texture for browser tab");
				// Keep using old texture if new one failed
			}
		});
	}

	private void OnSetCursor(SetCursorMessage msg)
	{
		_services.Framework.RunOnFrameworkThread(() =>
		{
			Guid guid = new(msg.Guid.Span);
			BrowserTab? tab = _tabs.FirstOrDefault(t => t.OverlayGuid == guid);
			if (tab == null) return;

			tab.CaptureCursor = msg.Cursor != Cursor.BrowsingwayNoCapture;
			tab.Cursor = BrowserInputHelper.DecodeCursor(msg.Cursor);
		});
	}

	private void OnUrlChanged(Common.Ipc.UrlChangedMessage msg)
	{
		_services.Framework.RunOnFrameworkThread(() =>
		{
			Guid guid = new(msg.Guid.Span);
			BrowserTab? tab = _tabs.FirstOrDefault(t => t.OverlayGuid == guid);
			if (tab == null) return;

			tab.Url = msg.Url;
			tab.UrlInput = msg.Url;
			tab.Name = GetDomainFromUrl(msg.Url);
		});
	}

}
