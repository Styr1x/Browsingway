using Browsingway.Common.Ipc;
using Browsingway.Extensions;
using Browsingway.Interop;
using Browsingway.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Net.Http;
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
	public string? FaviconUrl;
	public IDalamudTextureWrap? FaviconTexture;
}

internal sealed class BrowserWindow : Window, IDisposable
{
	private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

	private readonly ServiceContainer _services;
	private readonly OverlayManager _overlayManager;
	private readonly RenderProcessManager _renderProcessManager;
	private readonly Configuration _config;
	private readonly ISharedImmediateTexture? _logoTexture;

	private readonly List<BrowserTab> _tabs = [];
	private int _activeTabIndex = -1;
	private bool _forceSelectTab;
	private bool _windowFocused;
	private bool _mouseInContent;
	private bool _urlBarFocused;
	private int _urlBarFocusFrames;
	private Vector2 _contentSize = Vector2.Zero;
	private Vector2 _contentPos = Vector2.Zero;

	public BrowserWindow(
		ServiceContainer services,
		OverlayManager overlayManager,
		RenderProcessManager renderProcessManager,
		Configuration config,
		string pluginDir)
		: base("Browser###BrowsingwayBrowser")
	{
		_services = services;
		_overlayManager = overlayManager;
		_renderProcessManager = renderProcessManager;
		_config = config;

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
		float iconSize = ImGui.GetFrameHeight() * 1.25f * ImGuiHelpers.GlobalScale;

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

		// Calculate extra padding to make tabs match icon height
		float defaultFrameHeight = ImGui.GetFrameHeight();
		float extraPadding = (iconSize - defaultFrameHeight) / 2f;
		Vector2 currentPadding = ImGui.GetStyle().FramePadding;

		// Use native ImGui tab bar with close buttons
		using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(currentPadding.X, currentPadding.Y + extraPadding)))
		{
			if (ImGui.BeginTabBar("BrowserTabs", ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.FittingPolicyScroll))
			{
				// Track which tabs to close after iteration
				int? tabToClose = null;

				float faviconSize = ImGui.GetFrameHeight() * 0.8f;

				for (int i = 0; i < _tabs.Count; i++)
				{
					BrowserTab tab = _tabs[i];
					string tabLabel = tab.Name.Length > 20 ? tab.Name[..17] + "..." : tab.Name;

					// Add space for favicon - calculate actual width needed
					string faviconPadding = tab.FaviconTexture != null
						? new string(' ', (int)Math.Ceiling(faviconSize / ImGui.CalcTextSize(" ").X) + 1)
						: "";
					string displayLabel = faviconPadding + tabLabel;

					// p_open parameter adds the close button inside the tab
					bool tabOpen = true;
					ImGuiTabItemFlags flags = ImGuiTabItemFlags.None;
					if (i == _activeTabIndex && _forceSelectTab)
					{
						flags |= ImGuiTabItemFlags.SetSelected;
					}

					if (ImGui.BeginTabItem($"{displayLabel}##Tab{i}", ref tabOpen, flags))
					{
						_activeTabIndex = i;
						ImGui.EndTabItem();
					}

					// Draw favicon inside the tab
					if (tab.FaviconTexture != null)
					{
						Vector2 tabMin = ImGui.GetItemRectMin();
						Vector2 tabMax = ImGui.GetItemRectMax();
						float tabHeight = tabMax.Y - tabMin.Y;
						float faviconY = tabMin.Y + (tabHeight - faviconSize) / 2f;
						float faviconX = tabMin.X + ImGui.GetStyle().FramePadding.X;

						ImGui.GetWindowDrawList().AddImage(
							tab.FaviconTexture.Handle,
							new Vector2(faviconX, faviconY),
							new Vector2(faviconX + faviconSize, faviconY + faviconSize));
					}

					// Check if tab was clicked (even if not the content tab)
					if (ImGui.IsItemClicked())
					{
						_activeTabIndex = i;
					}

					// Tab tooltip
					if (ImGui.IsItemHovered())
					{
						ImGui.SetTooltip(tab.Url);
					}

					// Mark for closing if close button was clicked
					if (!tabOpen)
					{
						tabToClose = i;
					}
				}

				// Reset force select flag after processing tabs
				_forceSelectTab = false;

				// Add tab button as a special tab item
				if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Trailing | ImGuiTabItemFlags.NoTooltip))
				{
					AddTab();
				}
				if (ImGui.IsItemHovered())
				{
					ImGui.SetTooltip("Open new tab");
				}

				ImGui.EndTabBar();

				// Close tab after iteration to avoid index issues
				if (tabToClose.HasValue)
				{
					CloseTab(tabToClose.Value);
				}
			}
		}
	}

	private void DrawNavigationBar()
	{
		// Ensure we have at least one tab
		if (_tabs.Count == 0)
		{
			AddTab();
		}

		if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
		{
			_activeTabIndex = 0;
		}

		BrowserTab activeTab = _tabs[_activeTabIndex];

		// Back button
		if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowLeft))
		{
			_overlayManager.GoBack(activeTab.OverlayGuid);
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Go back");
		}

		ImGui.SameLine();

		// Forward button
		if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowRight))
		{
			_overlayManager.GoForward(activeTab.OverlayGuid);
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Go forward");
		}

		ImGui.SameLine();

		// Reload button
		if (ImGuiComponents.IconButton(FontAwesomeIcon.Redo))
		{
			_overlayManager.Reload(activeTab.OverlayGuid);
		}
		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Reload (Ctrl+click to ignore cache)");
		}

		ImGui.SameLine();

		// URL input with styled domain display
		float urlWidth = ImGui.GetContentRegionAvail().X;
		ImGui.SetNextItemWidth(urlWidth);

		if (_urlBarFocused)
		{
			// Set focus on the input for first few frames
			if (_urlBarFocusFrames > 0)
			{
				ImGui.SetKeyboardFocusHere();
				_urlBarFocusFrames--;
			}

			// Editing mode - regular input
			if (ImGui.InputText("##UrlInput", ref activeTab.UrlInput, 2048, ImGuiInputTextFlags.EnterReturnsTrue))
			{
				NavigateCurrentTab(activeTab.UrlInput);
				_urlBarFocused = false;
			}

			// Check if we lost focus (but not during initial focus frames)
			if (_urlBarFocusFrames == 0 && !ImGui.IsItemActive())
			{
				_urlBarFocused = false;
				activeTab.UrlInput = activeTab.Url; // Reset to actual URL
			}
		}
		else
		{
			// Display mode - styled URL
			DrawStyledUrlBar(activeTab, urlWidth);
		}
	}

	private void DrawStyledUrlBar(BrowserTab tab, float width)
	{
		Vector2 pos = ImGui.GetCursorScreenPos();
		float height = ImGui.GetFrameHeight();
		Vector2 padding = ImGui.GetStyle().FramePadding;

		// Draw frame background
		uint bgColor = ImGui.GetColorU32(ImGuiCol.FrameBg);
		uint bgHoverColor = ImGui.GetColorU32(ImGuiCol.FrameBgHovered);

		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		bool hovered = ImGui.IsMouseHoveringRect(pos, new Vector2(pos.X + width, pos.Y + height));

		drawList.AddRectFilled(pos, new Vector2(pos.X + width, pos.Y + height),
			hovered ? bgHoverColor : bgColor, ImGui.GetStyle().FrameRounding);

		// Parse URL to find domain parts
		string url = tab.Url;
		string scheme = "";
		string subdomain = "";
		string mainDomain = "";
		string path = "";

		try
		{
			Uri uri = new(url);

			// Handle special schemes that don't use "://" (about:, javascript:, data:, etc.)
			if (uri.Scheme is "about" or "javascript" or "data" or "blob")
			{
				scheme = uri.Scheme + ":";
				mainDomain = uri.AbsoluteUri[(uri.Scheme.Length + 1)..];
			}
			else
			{
				scheme = uri.Scheme + "://";
				string host = uri.Host;

				// Extract subdomain (www, www2, m, mobile, etc.)
				string[] parts = host.Split('.');
				if (parts.Length > 2)
				{
					// Check for common subdomains
					string firstPart = parts[0].ToLowerInvariant();
					if (firstPart is "www" or "www2" or "www3" or "m" or "mobile" or "app" or "api")
					{
						subdomain = parts[0] + ".";
						mainDomain = string.Join(".", parts[1..]);
					}
					else
					{
						mainDomain = host;
					}
				}
				else
				{
					mainDomain = host;
				}

				path = uri.PathAndQuery;
				if (path == "/") path = "";
			}
		}
		catch
		{
			mainDomain = url;
		}

		// Calculate text positions
		float textY = pos.Y + padding.Y;
		float textX = pos.X + padding.X;

		// Colors
		uint greyColor = ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
		uint domainColor = ImGui.GetColorU32(ImGuiCol.Text);

		// Draw scheme (grey)
		if (!string.IsNullOrEmpty(scheme))
		{
			drawList.AddText(new Vector2(textX, textY), greyColor, scheme);
			textX += ImGui.CalcTextSize(scheme).X;
		}

		// Draw subdomain (grey)
		if (!string.IsNullOrEmpty(subdomain))
		{
			drawList.AddText(new Vector2(textX, textY), greyColor, subdomain);
			textX += ImGui.CalcTextSize(subdomain).X;
		}

		// Draw main domain (normal color)
		if (!string.IsNullOrEmpty(mainDomain))
		{
			drawList.AddText(new Vector2(textX, textY), domainColor, mainDomain);
			textX += ImGui.CalcTextSize(mainDomain).X;
		}

		// Draw path (grey) - clip if too long
		if (!string.IsNullOrEmpty(path))
		{
			float availableWidth = pos.X + width - padding.X - textX;
			if (availableWidth > 20)
			{
				string displayPath = path;
				Vector2 pathSize = ImGui.CalcTextSize(path);
				if (pathSize.X > availableWidth)
				{
					// Truncate with ellipsis
					while (displayPath.Length > 3 && ImGui.CalcTextSize(displayPath + "...").X > availableWidth)
					{
						displayPath = displayPath[..^1];
					}
					displayPath += "...";
				}
				drawList.AddText(new Vector2(textX, textY), greyColor, displayPath);
			}
		}

		// Invisible button for click detection
		ImGui.SetCursorScreenPos(pos);
		if (ImGui.InvisibleButton("##UrlDisplay", new Vector2(width, height)))
		{
			_urlBarFocused = true;
			_urlBarFocusFrames = 3; // Give a few frames for focus to settle
			tab.UrlInput = tab.Url;
		}
	}

	private void DrawContentArea(float toolbarHeight)
	{
		Vector2 contentRegion = ImGui.GetContentRegionAvail();
		_contentPos = ImGui.GetCursorScreenPos();
		_contentSize = contentRegion;

		// Tab is guaranteed to exist due to DrawNavigationBar ensuring at least one tab
		BrowserTab activeTab = _tabs[_activeTabIndex];

		// Update overlay size so it renders at the correct size for this content area
		_overlayManager.SetOverlaySize(activeTab.OverlayGuid, contentRegion);

		// Content child without padding so texture fills exactly
		using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero))
		{
			ImGui.BeginChild("BrowserContent", contentRegion, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

			// Update content position after entering child (for mouse input)
			_contentPos = ImGui.GetCursorScreenPos();

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
		}

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
		string startPage = _config.BrowserStartPage;
		if (string.IsNullOrWhiteSpace(startPage))
		{
			startPage = "about:blank";
		}

		// Check if a "New Tab" already exists - if so, just switch to it
		int existingNewTabIndex = _tabs.FindIndex(t => t.Url == startPage && t.Name == "New Tab");
		if (existingNewTabIndex >= 0)
		{
			_activeTabIndex = existingNewTabIndex;
			return;
		}

		// Create ephemeral overlay with Hidden visibility (renders but doesn't display in its own window)
		OverlayConfiguration config = new()
		{
			Guid = Guid.NewGuid(),
			Name = "Browser Tab",
			Url = startPage,
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
			Url = startPage,
			UrlInput = startPage
		};

		_tabs.Add(tab);
		_activeTabIndex = _tabs.Count - 1;
		_forceSelectTab = true;

		// Trigger sync to create the overlay in the renderer
		_overlayManager.SyncNow();
	}

	private void CloseTab(int index)
	{
		if (index < 0 || index >= _tabs.Count)
		{
			return;
		}

		// Don't close the last tab - instead reset it to "New Tab"
		if (_tabs.Count == 1)
		{
			string startPage = _config.BrowserStartPage;
			if (string.IsNullOrWhiteSpace(startPage))
			{
				startPage = "about:blank";
			}

			BrowserTab lastTab = _tabs[0];
			// Navigate to start page and reset name
			lastTab.Name = "New Tab";
			lastTab.Url = startPage;
			lastTab.UrlInput = startPage;
			_overlayManager.NavigateOverlay(lastTab.OverlayGuid, startPage);
			return;
		}

		BrowserTab tab = _tabs[index];

		// Remove ephemeral overlay
		_overlayManager.RemoveEphemeralOverlay(tab.OverlayGuid);

		// Dispose textures
		tab.Texture?.Dispose();
		tab.FaviconTexture?.Dispose();

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
		// Close all tabs except the first one
		for (int i = _tabs.Count - 1; i > 0; i--)
		{
			BrowserTab tab = _tabs[i];
			_overlayManager.RemoveEphemeralOverlay(tab.OverlayGuid);
			tab.Texture?.Dispose();
			tab.FaviconTexture?.Dispose();
			_tabs.RemoveAt(i);
		}

		// Reset the first tab to "New Tab"
		if (_tabs.Count > 0)
		{
			string startPage = _config.BrowserStartPage;
			if (string.IsNullOrWhiteSpace(startPage))
			{
				startPage = "about:blank";
			}

			BrowserTab firstTab = _tabs[0];
			firstTab.Name = "New Tab";
			firstTab.Url = startPage;
			firstTab.UrlInput = startPage;
			_overlayManager.NavigateOverlay(firstTab.OverlayGuid, startPage);
		}

		_activeTabIndex = 0;
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

			// Ignore empty URLs or about:blank to preserve "New Tab" name
			if (string.IsNullOrEmpty(msg.Url) || msg.Url == "about:blank")
			{
				return;
			}

			bool isNewUrl = tab.Url != msg.Url;
			tab.Url = msg.Url;
			tab.UrlInput = msg.Url;

			// Clear favicon when navigating to a new URL
			if (isNewUrl)
			{
				tab.FaviconTexture?.Dispose();
				tab.FaviconTexture = null;
				tab.FaviconUrl = null;
			}

			// Use page title if available, otherwise fall back to domain only if URL changed
			if (!string.IsNullOrEmpty(msg.Title))
			{
				tab.Name = msg.Title;
			}
			else if (isNewUrl || tab.Name == "New Tab")
			{
				// Only reset to domain if navigating to a new URL or we don't have a name yet
				tab.Name = GetDomainFromUrl(msg.Url);
			}

			// Load favicon if provided
			if (!string.IsNullOrEmpty(msg.FaviconUrl) && msg.FaviconUrl != tab.FaviconUrl)
			{
				tab.FaviconUrl = msg.FaviconUrl;
				LoadFaviconAsync(tab, msg.FaviconUrl);
			}
		});
	}

	private async void LoadFaviconAsync(BrowserTab tab, string faviconUrl)
	{
		try
		{
			string urlToFetch = faviconUrl;

			// SVG favicons aren't supported - try /favicon.ico fallback
			if (faviconUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					Uri uri = new(faviconUrl);
					urlToFetch = $"{uri.Scheme}://{uri.Host}/favicon.ico";
				}
				catch
				{
					return; // Invalid URL, skip favicon
				}
			}

			// Download favicon into memory
			byte[] data = await HttpClient.GetByteArrayAsync(urlToFetch);

			// Create texture from image data
			IDalamudTextureWrap newTexture = await _services.TextureProvider.CreateFromImageAsync(data);

			// Update on framework thread
			_ = _services.Framework.RunOnFrameworkThread(() =>
			{
				// Verify the tab still wants this favicon
				if (tab.FaviconUrl == faviconUrl)
				{
					// Dispose old texture
					tab.FaviconTexture?.Dispose();
					tab.FaviconTexture = newTexture;
				}
				else
				{
					// Tab no longer needs this favicon, dispose it
					newTexture.Dispose();
				}
			});
		}
		catch (Exception ex)
		{
			_services.PluginLog.Warning(ex, "Failed to load favicon from {Url}", faviconUrl);
		}
	}

}
