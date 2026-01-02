using Browsingway;
using Browsingway.Common.Ipc;
using Browsingway.Extensions;
using Browsingway.Interop;
using Browsingway.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System.Numerics;
using TerraFX.Interop.Windows;

namespace Browsingway.UI.Windows;

internal class OverlayWindow : Window, IDisposable
{
	private readonly OverlayConfiguration _overlayConfig;
	private readonly RenderProcessManager _renderProcessManager;
	private readonly IServiceContainer _services;
	private readonly ISharedImmediateTexture? _texErrorIcon;
	private readonly Action? _onSizeChanged;

	private bool _captureCursor;
	private ImGuiMouseCursor _cursor;
	private bool _mouseInWindow;
	private Vector2 _size;
	private bool _hasRenderError;
	private ImGuiSharedTexture? _textureHandler;
	private Exception? _textureRenderException;
	private bool _windowFocused;
	private BaseVisibility _computedVisibility;

	public OverlayWindow(IServiceContainer services, RenderProcessManager renderProcessManager, OverlayConfiguration overlayConfig, string pluginDir, Action? onSizeChanged = null)
		: base($"{overlayConfig.Name}###{overlayConfig.Guid}",
			ImGuiWindowFlags.NoTitleBar
			| ImGuiWindowFlags.NoCollapse
			| ImGuiWindowFlags.NoScrollbar
			| ImGuiWindowFlags.NoScrollWithMouse
			| ImGuiWindowFlags.NoBringToFrontOnFocus
			| ImGuiWindowFlags.NoFocusOnAppearing)
	{
		_services = services;
		_renderProcessManager = renderProcessManager;
		_onSizeChanged = onSizeChanged;
		_renderProcessManager.Crashed += (_, _) =>
		{
			_size = Vector2.Zero;
			_hasRenderError = true;
		};

		_overlayConfig = overlayConfig;
		_texErrorIcon = _services.TextureProvider.GetFromFile(Path.Combine(pluginDir, "dead.png"));

		// Configure window for borderless appearance
		Size = new Vector2(640, 480);
		SizeCondition = ImGuiCond.FirstUseEver;
		RespectCloseHotkey = false;
		DisableWindowSounds = true;

		// Start open
		IsOpen = true;
	}

	public Guid RenderGuid => _overlayConfig.Guid;
	public BaseVisibility ComputedVisibility => _computedVisibility;

	/// <summary>
	/// Gets the current overlay state for sync to renderer.
	/// Returns null if:
	/// - The overlay hasn't been sized yet (size is zero)
	/// - The computed visibility is Disabled (browser should not exist)
	/// </summary>
	public OverlayState? GetState()
	{
		// Don't sync if not yet sized
		if (_size == Vector2.Zero) return null;

		// Disabled = browser should not exist in renderer
		if (_computedVisibility == BaseVisibility.Disabled) return null;

		return new OverlayState
		{
			Guid = RenderGuid.ToByteArray(),
			Id = _overlayConfig.Name,
			Url = _overlayConfig.Url,
			Width = (int)_size.X,
			Height = (int)_size.Y,
			Framerate = _overlayConfig.Framerate,
			Zoom = _overlayConfig.Zoom,
			Muted = _overlayConfig.Muted,
			CustomCss = _overlayConfig.CustomCss ?? "",
			CustomJs = _overlayConfig.CustomJs ?? ""
		};
	}

	public void Dispose()
	{
		_textureHandler?.Dispose();
		// Overlay removal is handled by sync - when overlay is removed from manager,
		// next sync will not include it, and renderer will remove it
	}

	/// <summary>
	/// Imperatively navigate to a new URL (user action).
	/// </summary>
	public void Navigate(string newUrl)
	{
		_renderProcessManager.Rpc?.Navigate(RenderGuid, newUrl).FireAndForget(_services.PluginLog);
	}

	/// <summary>
	/// Imperatively open DevTools (user action).
	/// </summary>
	public void Debug()
	{
		_renderProcessManager.Rpc?.Debug(RenderGuid).FireAndForget(_services.PluginLog);
	}

	public void SetCursor(Cursor cursor)
	{
		_captureCursor = cursor != Cursor.BrowsingwayNoCapture;
		_cursor = DecodeCursor(cursor);
	}

	public WndProcResult WndProcMessage(WindowsMessage msg, ulong wParam, long lParam)
	{
		if (msg == WindowsMessage.WM_LBUTTONDOWN)
		{
			// this message is only generated when someone clicked on a non-ImGui window, meaning we want to lose focus here
			_windowFocused = false;
		}

		// Bail if we're not focused or we're typethrough
		// TODO: Revisit the focus check for UI stuff, might not hold
		if (!_windowFocused || _overlayConfig.TypeThrough)
		{
			return WndProcResult.NotHandled;
		}

		KeyEventType? eventType = msg switch
		{
			WindowsMessage.WM_KEYDOWN or WindowsMessage.WM_SYSKEYDOWN => KeyEventType.KeyDown,
			WindowsMessage.WM_KEYUP or WindowsMessage.WM_SYSKEYUP => KeyEventType.KeyUp,
			WindowsMessage.WM_CHAR or WindowsMessage.WM_SYSCHAR => KeyEventType.Character,
			_ => null
		};

		// If the event isn't something we're tracking, bail early with no capture
		if (eventType == null)
		{
			return WndProcResult.NotHandled;
		}

		_renderProcessManager.Rpc?.KeyEvent(RenderGuid, (int)msg, (int)wParam, (int)lParam).FireAndForget(_services.PluginLog);

		// We've handled the input, signal. For these message types, `0` signals a capture.
		return WndProcResult.HandledWith(0);
	}

	public override bool DrawConditions()
	{
		// Check computed visibility (which factors in base visibility + rules)
		if (_computedVisibility != BaseVisibility.Visible)
		{
			_mouseInWindow = false;
			return false;
		}
		return true;
	}

	/// <summary>
	/// Updates the computed visibility based on the current environment.
	/// Called by OverlayManager when visibility environment changes.
	/// </summary>
	public void UpdateVisibility(GameEnvironment environment)
	{
		_computedVisibility = VisibilityEvaluator.ComputeVisibility(
			_overlayConfig.BaseVisibility,
			_overlayConfig.VisibilityRules,
			environment);
	}

	public override void PreDraw()
	{
		// Disable window padding
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
		
		// Update flags dynamically based on config state
		Flags = GetWindowFlags();
		
		// Handle fullscreen positioning
		if (_overlayConfig.Fullscreen)
		{
			var screen = ImGui.GetMainViewport();

			// ImGui always leaves a 1px transparent border around the window, so we need to account for that.
			var fsPos = new Vector2(screen.WorkPos.X - 1, screen.WorkPos.Y - 1);
			var fsSize = new Vector2(screen.Size.X + 2 - fsPos.X, screen.Size.Y + 2 - fsPos.Y);

			Position = fsPos;
			PositionCondition = ImGuiCond.Always;
			Size = fsSize;
			SizeCondition = ImGuiCond.Always;
		}
		else
		{
			// Reset conditions so user can move/resize
			PositionCondition = ImGuiCond.FirstUseEver;
			SizeCondition = ImGuiCond.FirstUseEver;
		}
	}
	
	public override void Draw()
	{
		HandleWindowSize();

		// TODO: Browsingway.Renderer can take some time to spin up properly, should add a loading state.
		if (_textureHandler != null && !_hasRenderError)
		{
			HandleMouseEvent();

			ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _overlayConfig.Opacity / 100f);
			_textureHandler.Render();
			ImGui.PopStyleVar();
		}
		else
		{
			if (_texErrorIcon is not null)
			{
				float lineHeight = ImGui.GetTextLineHeight();
				float size = float.Min(_size.X - lineHeight * 3, _size.Y - lineHeight * 3);
				ImGui.NewLine();
				ImGuiHelpers.CenterCursorFor(size);
				ImGui.Image(_texErrorIcon.GetWrapOrEmpty().Handle, new Vector2(size, size));

				ImGui.PushStyleColor(ImGuiCol.Text, 0xFF0000FF);
				if (_textureRenderException is not null)
				{
					ImGuiHelpers.CenteredText("An error occured while building the browser overlay texture:");
					ImGuiHelpers.CenteredText(_textureRenderException.ToString());
				}
				else
				{
					ImGuiHelpers.CenteredText("An error occured while building the browser overlay texture. Check the log for more details.");
				}

				ImGui.PopStyleColor();
			}
		}
	}
	
	public override void PostDraw()
	{
		ImGui.PopStyleVar();
	}

	private ImGuiWindowFlags GetWindowFlags()
	{
		ImGuiWindowFlags flags = ImGuiWindowFlags.None
								 | ImGuiWindowFlags.NoTitleBar
								 | ImGuiWindowFlags.NoCollapse
								 | ImGuiWindowFlags.NoScrollbar
								 | ImGuiWindowFlags.NoScrollWithMouse
								 | ImGuiWindowFlags.NoBringToFrontOnFocus
								 | ImGuiWindowFlags.NoFocusOnAppearing;

		// ClickThrough / fullscreen is implicitly locked
		bool locked = _overlayConfig.Locked || _overlayConfig.ClickThrough || _overlayConfig.Fullscreen;

		if (locked)
		{
			flags |= ImGuiWindowFlags.None
					 | ImGuiWindowFlags.NoMove
					 | ImGuiWindowFlags.NoResize
					 | ImGuiWindowFlags.NoBackground;
		}

		if (_overlayConfig.ClickThrough || (!_captureCursor && locked))
		{
			flags |= ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNav;
		}

		// don't think user wants a background when they decrease opacity
		if (_overlayConfig.Opacity < 100f)
			flags |= ImGuiWindowFlags.NoBackground;

		return flags;
	}

	public void SetTexture(HANDLE handle)
	{
		_hasRenderError = false;

		ImGuiSharedTexture? oldTextureHandler = _textureHandler;
		try
		{
			_textureHandler = new ImGuiSharedTexture(handle);
		}
		catch (Exception e) { _textureRenderException = e; }

		if (oldTextureHandler != null) { oldTextureHandler.Dispose(); }
	}

	private void HandleMouseEvent()
	{
		// Render proc won't be ready on first boot
		// Totally skip mouse handling for click through overlays, as well
		if (_renderProcessManager == null || _overlayConfig.ClickThrough) { return; }

		ImGuiIOPtr io = ImGui.GetIO();
		Vector2 windowPos = ImGui.GetWindowPos();
		Vector2 mousePos = io.MousePos - windowPos - ImGui.GetWindowContentRegionMin();

		// Generally we want to use IsWindowHovered for hit checking, as it takes z-stacking into account -
		// but when cursor isn't being actively captured, imgui will always return false - so fall back
		// so a slightly more naive hover check, just to maintain a bit of flood prevention.
		// TODO: Need to test how this will handle overlaps... fully transparent _shouldn't_ be accepting
		//       clicks so shouuulllddd beee fineee???
		bool hovered = _captureCursor
			? ImGui.IsWindowHovered()
			: ImGui.IsMouseHoveringRect(windowPos, windowPos + ImGui.GetWindowSize());

		// manage focus
		MouseButton down = EncodeMouseButtons(io.MouseClicked);
		MouseButton double_ = EncodeMouseButtons(io.MouseDoubleClicked);
		MouseButton up = EncodeMouseButtons(io.MouseReleased);
		float wheelX = io.MouseWheelH;
		float wheelY = io.MouseWheel;
		if (down.HasFlag(MouseButton.Primary) || down.HasFlag(MouseButton.Secondary) || down.HasFlag(MouseButton.Tertiary))
		{
			_windowFocused = _mouseInWindow;
		}

		// If the cursor is outside the window, send a final mouse leave then noop
		if (!hovered)
		{
			if (_mouseInWindow)
			{
				_mouseInWindow = false;
				_renderProcessManager.Rpc?.MouseButton(new MouseButtonMessage { Guid = RenderGuid.ToByteArray(), X = (int)mousePos.X, Y = (int)mousePos.Y, Leaving = true })
					.FireAndForget(_services.PluginLog);
			}

			return;
		}

		_mouseInWindow = true;

		ImGui.SetMouseCursor(_cursor);

		// If the event boils down to no change, bail before sending
		if (io.MouseDelta == Vector2.Zero && down == MouseButton.None && double_ == MouseButton.None && up == MouseButton.None && wheelX == 0 && wheelY == 0)
		{
			return;
		}

		InputModifier modifier = InputModifier.None;
		if (io.KeyShift) { modifier |= InputModifier.Shift; }

		if (io.KeyCtrl) { modifier |= InputModifier.Control; }

		if (io.KeyAlt) { modifier |= InputModifier.Alt; }

		_renderProcessManager.Rpc?.MouseButton(new MouseButtonMessage
		{
			Guid = RenderGuid.ToByteArray(),
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

	private void HandleWindowSize()
	{
		Vector2 currentSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
		if (currentSize == _size) { return; }

		_size = currentSize;

		// Notify that size changed - OverlayManager will trigger sync
		_onSizeChanged?.Invoke();
	}

	#region serde

	private static MouseButton EncodeMouseButtons(Span<bool> buttons)
	{
		MouseButton result = MouseButton.None;
		if (buttons[0]) result |= MouseButton.Primary;
		if (buttons[1]) result |= MouseButton.Secondary;
		if (buttons[2]) result |= MouseButton.Tertiary;
		if (buttons[3]) result |= MouseButton.Fourth;
		if (buttons[4]) result |= MouseButton.Fifth;
		return result;
	}

	private static ImGuiMouseCursor DecodeCursor(Cursor cursor) => cursor switch
	{
		Cursor.Default => ImGuiMouseCursor.Arrow,
		Cursor.None => ImGuiMouseCursor.None,
		Cursor.Pointer => ImGuiMouseCursor.Hand,
		Cursor.Text or Cursor.VerticalText => ImGuiMouseCursor.TextInput,
		Cursor.NResize or Cursor.SResize or Cursor.NsResize => ImGuiMouseCursor.ResizeNs,
		Cursor.EResize or Cursor.WResize or Cursor.EwResize => ImGuiMouseCursor.ResizeEw,
		Cursor.NeResize or Cursor.SwResize or Cursor.NeswResize => ImGuiMouseCursor.ResizeNesw,
		Cursor.NwResize or Cursor.SeResize or Cursor.NwseResize => ImGuiMouseCursor.ResizeNwse,
		_ => ImGuiMouseCursor.Arrow
	};



	#endregion
}
