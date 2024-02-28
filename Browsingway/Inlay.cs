using Browsingway.Common;
using Browsingway.TextureHandlers;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using ImGuiNET;
using System.Numerics;

namespace Browsingway;

internal class Inlay : IDisposable
{
	private readonly Configuration? _config;
	private readonly InlayConfiguration _inlayConfig;

	private readonly RenderProcess _renderProcess;
	private bool _captureCursor;
	private ImGuiMouseCursor _cursor;
	
	private bool _mouseInWindow;

	private bool _resizing;
	private Vector2 _size;
	private ITextureHandler? _textureHandler;
	private Exception? _textureRenderException;
	private bool _windowFocused;
	private IPluginLog _pluginLog;
	private IClientState _clientState;
	private long _timeLastInCombat;
	
	public Inlay(RenderProcess renderProcess, Configuration? config, InlayConfiguration inlayConfig, IPluginLog pluginLog, IClientState clientState)
	{
		_renderProcess = renderProcess;
		// TODO: handle that the correct way
		_renderProcess.Crashed += (_, _) =>
		{
			_size = Vector2.Zero;
		};

		_config = config;
		_inlayConfig = inlayConfig;
		_pluginLog = pluginLog;
		_clientState = clientState;
	}

	public Guid RenderGuid { get; private set; } = Guid.NewGuid();

	public void Dispose()
	{
		_textureHandler?.Dispose();
		_renderProcess.Send(new RemoveInlayRequest { Guid = RenderGuid });
	}

	public void Navigate(string newUrl)
	{
		_renderProcess.Send(new NavigateInlayRequest { Guid = RenderGuid, Url = newUrl });
	}

	public void InjectUserCss(string css)
	{
		_renderProcess.Send(new InjectUserCssRequest { Guid = RenderGuid, Css = css });
	}

	public void Zoom(float zoom)
	{
		_renderProcess.Send(new ZoomInlayRequest { Guid = RenderGuid, Zoom = zoom });
	}

	public void Mute(bool mute)
	{
		_renderProcess.Send(new MuteInlayRequest() { Guid = RenderGuid, Mute = mute });
	}

	public void Debug()
	{
		_renderProcess.Send(new DebugInlayRequest { Guid = RenderGuid });
	}

	public void InvalidateTransport()
	{
		// Get old refs so we can clean up later
		ITextureHandler? oldTextureHandler = _textureHandler;
		Guid oldRenderGuid = RenderGuid;

		// Invalidate the handler, and reset the size to trigger a rebuild
		// Also need to generate a new renderer guid so we don't have a collision during the hand over
		// TODO: Might be able to tweak the logic in resize alongside this to shore up (re)builds
		_textureHandler = null;
		_size = Vector2.Zero;
		RenderGuid = Guid.NewGuid();

		// Clean up
		oldTextureHandler?.Dispose();
		_renderProcess.Send(new RemoveInlayRequest { Guid = oldRenderGuid });
	}

	public void SetCursor(Cursor cursor)
	{
		_captureCursor = cursor != Cursor.BrowsingwayNoCapture;
		_cursor = DecodeCursor(cursor);
	}

	public (bool, long) WndProcMessage(WindowsMessage msg, ulong wParam, long lParam)
	{
		if (msg == WindowsMessage.WM_LBUTTONDOWN)
		{
			// this message is only generated when someone clicked on an non ImGui window, meaning we want to loose focus here
			_windowFocused = false;
		}
		
		// Bail if we're not focused or we're typethrough
		// TODO: Revisit the focus check for UI stuff, might not hold
		if (!_windowFocused || _inlayConfig.TypeThrough) { return (false, 0); }

		KeyEventType? eventType = msg switch
		{
			WindowsMessage.WM_KEYDOWN => KeyEventType.KeyDown,
			WindowsMessage.WM_SYSKEYDOWN => KeyEventType.KeyDown,
			WindowsMessage.WM_KEYUP => KeyEventType.KeyUp,
			WindowsMessage.WM_SYSKEYUP => KeyEventType.KeyUp,
			WindowsMessage.WM_CHAR => KeyEventType.Character,
			WindowsMessage.WM_SYSCHAR => KeyEventType.Character,
			_ => null
		};

		// If the event isn't something we're tracking, bail early with no capture
		if (eventType == null) { return (false, 0); }
		
		_renderProcess.Send(new KeyEventRequest
		{
			Guid = RenderGuid,
			Msg = (int)msg,
			WParam = (int)wParam,
			LParam = (int)lParam
		});

		// We've handled the input, signal. For these message types, `0` signals a capture.
		return (true, 0);
	}

	public void Render()
	{
		if (_inlayConfig.Hidden || _inlayConfig.Disabled || DisabledByCombatFlags())
		{
			_mouseInWindow = false;
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(640, 480), ImGuiCond.FirstUseEver);
		ImGui.Begin($"{_inlayConfig.Name}###{_inlayConfig.Guid}", GetWindowFlags());

		if (_inlayConfig.Fullscreen) {
			var screen = ImGui.GetMainViewport();

			// ImGui always leaves a 1px transparent border around the window, so we need to account for that.
			var fsPos  = new Vector2(screen.WorkPos.X - 1, screen.WorkPos.Y - 1);
			var fsSize = new Vector2(screen.Size.X + 2 - fsPos.X, screen.Size.Y + 2 - fsPos.Y);

			if (ImGui.GetWindowPos() != fsPos) {
				ImGui.SetWindowPos(fsPos, ImGuiCond.Always);
			}

			if (_size.X != fsSize.X || _size.Y != fsSize.Y) {
				ImGui.SetWindowSize(fsSize, ImGuiCond.Always);
			}
		}

		HandleWindowSize();

		// TODO: Browsingway.Renderer can take some time to spin up properly, should add a loading state.
		if (_textureHandler != null)
		{
			HandleMouseEvent();

			ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _inlayConfig.Opacity / 100f);
			_textureHandler.Render();
			ImGui.PopStyleVar();
		}
		else if (_textureRenderException != null)
		{
			ImGui.PushStyleColor(ImGuiCol.Text, 0xFF0000FF);
			ImGui.Text("An error occured while building the browser inlay texture:");
			ImGui.Text(_textureRenderException.ToString());
			ImGui.PopStyleColor();
		}

		ImGui.End();
	}

	private bool DisabledByCombatFlags()
	{
		if (!_inlayConfig.DisableOutOfCombat)
		{
			return false;
		}

		if (_clientState.LocalPlayer == null)
		{
			return true;
		}

		if (_clientState.LocalPlayer.StatusFlags.HasFlag(StatusFlags.InCombat))
		{
			_timeLastInCombat = DateTimeOffset.Now.ToUnixTimeMilliseconds();
			return false;
		}
			
		if (!_clientState.LocalPlayer.StatusFlags.HasFlag(StatusFlags.InCombat) && _inlayConfig.CombatDelay > 0)
		{
			return DateTimeOffset.Now.ToUnixTimeMilliseconds() >= _timeLastInCombat + (_inlayConfig.CombatDelay * 1000);
		}
		return true;
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
		bool locked = _inlayConfig.Locked || _inlayConfig.ClickThrough || _inlayConfig.Fullscreen;

		if (locked)
		{
			flags |= ImGuiWindowFlags.None
			         | ImGuiWindowFlags.NoMove
			         | ImGuiWindowFlags.NoResize
			         | ImGuiWindowFlags.NoBackground;
		}

		if (_inlayConfig.ClickThrough || (!_captureCursor && locked))
		{
			flags |= ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNav;
		}

		// don't think user wants a background when they decrease opacity
		if (_inlayConfig.Opacity < 100f)
			flags |= ImGuiWindowFlags.NoBackground;

		return flags;
	}

	private void HandleMouseEvent()
	{
		// Render proc won't be ready on first boot
		// Totally skip mouse handling for click through inlays, as well
		if (_renderProcess == null || _inlayConfig.ClickThrough) { return; }

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
				_renderProcess.Send(new MouseEventRequest { Guid = RenderGuid, X = mousePos.X, Y = mousePos.Y, Leaving = true });
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

		// TODO: Either this or the entire handler function should be asynchronous so we're not blocking the entire draw thread
		_renderProcess.Send(new MouseEventRequest
		{
			Guid = RenderGuid,
			X = mousePos.X,
			Y = mousePos.Y,
			Down = down,
			Double = double_,
			Up = up,
			WheelX = wheelX,
			WheelY = wheelY,
			Modifier = modifier
		});
	}

	private async void HandleWindowSize()
	{
		Vector2 currentSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
		if (currentSize == _size || _resizing) { return; }

		// If there isn't a size yet, we haven't rendered at all - boot up an inlay in the render process
		// TODO: Edge case - if a user _somehow_ makes the size zero, this will freak out and generate a new render inlay
		// TODO: Maybe consolidate the request types? dunno.
		DownstreamIpcRequest request = _size == Vector2.Zero
			? new NewInlayRequest
			{
				Guid = RenderGuid,
				Id = _inlayConfig.Name,
				FrameTransportMode = _config?.FrameTransportMode ?? FrameTransportMode.None,
				Url = _inlayConfig.Url,
				Width = (int)currentSize.X,
				Height = (int)currentSize.Y,
				Zoom = _inlayConfig.Zoom,
				Framerate = _inlayConfig.Framerate,
				Muted = _inlayConfig.Muted,
				CustomCss = _inlayConfig.CustomCss
			}
			: new ResizeInlayRequest { Guid = RenderGuid, Width = (int)currentSize.X, Height = (int)currentSize.Y };

		_resizing = true;

		IpcResponse<FrameTransportResponse> response = await _renderProcess.Send<FrameTransportResponse>(request);
		if (!response.Success)
		{
			_pluginLog.Error("Texture build failure, retrying...");
			_resizing = false;
			return;
		}

		_size = currentSize;
		_resizing = false;

		ITextureHandler? oldTextureHandler = _textureHandler;
		try
		{
			_textureHandler = response.Data switch
			{
				TextureHandleResponse textureHandleResponse => new SharedTextureHandler(textureHandleResponse),
				BitmapBufferResponse bitmapBufferResponse => new BitmapBufferTextureHandler(bitmapBufferResponse),
				_ => throw new Exception($"Unhandled frame transport {response.GetType().Name}")
			};
		}
		catch (Exception e) { _textureRenderException = e; }

		if (oldTextureHandler != null) { oldTextureHandler.Dispose(); }
	}

	#region serde

	private MouseButton EncodeMouseButtons(RangeAccessor<bool> buttons)
	{
		MouseButton result = MouseButton.None;
		if (buttons[0]) { result |= MouseButton.Primary; }

		if (buttons[1]) { result |= MouseButton.Secondary; }

		if (buttons[2]) { result |= MouseButton.Tertiary; }

		if (buttons[3]) { result |= MouseButton.Fourth; }

		if (buttons[4]) { result |= MouseButton.Fifth; }

		return result;
	}

	private ImGuiMouseCursor DecodeCursor(Cursor cursor)
	{
		// ngl kinda disappointed at the lack of options here
		switch (cursor)
		{
			case Cursor.Default: return ImGuiMouseCursor.Arrow;
			case Cursor.None: return ImGuiMouseCursor.None;
			case Cursor.Pointer: return ImGuiMouseCursor.Hand;

			case Cursor.Text:
			case Cursor.VerticalText:
				return ImGuiMouseCursor.TextInput;

			case Cursor.NResize:
			case Cursor.SResize:
			case Cursor.NsResize:
				return ImGuiMouseCursor.ResizeNS;

			case Cursor.EResize:
			case Cursor.WResize:
			case Cursor.EwResize:
				return ImGuiMouseCursor.ResizeEW;

			case Cursor.NeResize:
			case Cursor.SwResize:
			case Cursor.NeswResize:
				return ImGuiMouseCursor.ResizeNESW;

			case Cursor.NwResize:
			case Cursor.SeResize:
			case Cursor.NwseResize:
				return ImGuiMouseCursor.ResizeNWSE;
		}

		return ImGuiMouseCursor.Arrow;
	}

	#endregion
}