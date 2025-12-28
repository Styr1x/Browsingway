using Browsingway.Common.Ipc;
using Browsingway.Extensions;
using Browsingway.Models;
using Browsingway.Services;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace Browsingway;

internal class Overlay : IDisposable
{
	private readonly InlayConfiguration _overlayConfig;
	private readonly RenderProcess _renderProcess;
	private readonly IServiceContainer _services;
	private readonly ISharedImmediateTexture? _texErrorIcon;

	private bool _captureCursor;
	private ImGuiMouseCursor _cursor;
	private bool _mouseInWindow;
	private bool _resizing;
	private Vector2 _size;
	private bool _hasRenderError;
	private SharedTextureHandler? _textureHandler;
	private Exception? _textureRenderException;
	private bool _windowFocused;
	private long _timeLastInCombat;

	public Overlay(IServiceContainer services, RenderProcess renderProcess, InlayConfiguration overlayConfig, string pluginDir)
	{
		_services = services;
		_renderProcess = renderProcess;
		// TODO: handle that the correct way
		_renderProcess.Crashed += (_, _) =>
		{
			_size = Vector2.Zero;
			_hasRenderError = true;
		};

		_overlayConfig = overlayConfig;
		_texErrorIcon = _services.TextureProvider.GetFromFile(Path.Combine(pluginDir, "dead.png"));
	}

	public Guid RenderGuid => _overlayConfig.Guid;

	public void Dispose()
	{
		_textureHandler?.Dispose();
		_renderProcess.Rpc?.RemoveOverlay(RenderGuid).FireAndForget(_services.PluginLog);
	}

	public void Navigate(string newUrl)
	{
		_renderProcess.Rpc?.Navigate(RenderGuid, newUrl).FireAndForget(_services.PluginLog);
	}

	public void InjectUserCss(string css)
	{
		_renderProcess.Rpc?.InjectUserCss(RenderGuid, css).FireAndForget(_services.PluginLog);
	}

	public void Zoom(float zoom)
	{
		_renderProcess.Rpc?.Zoom(RenderGuid, zoom).FireAndForget(_services.PluginLog);
	}

	public void Mute(bool mute)
	{
		_renderProcess.Rpc?.Mute(RenderGuid, mute).FireAndForget(_services.PluginLog);
	}

	public void Debug()
	{
		_renderProcess.Rpc?.Debug(RenderGuid).FireAndForget(_services.PluginLog);
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

		_renderProcess.Rpc?.KeyEvent(RenderGuid, (int)msg, (int)wParam, (int)lParam).FireAndForget(_services.PluginLog);

		// We've handled the input, signal. For these message types, `0` signals a capture.
		return WndProcResult.HandledWith(0);
	}

	public void Render()
	{
		if (_overlayConfig.Hidden || _overlayConfig.Disabled || HiddenByCombatFlags() ||
		    (_overlayConfig.HideInPvP && _services.ClientState.IsPvP))
		{
			_mouseInWindow = false;
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(640, 480), ImGuiCond.FirstUseEver);
		ImGui.Begin($"{_overlayConfig.Name}###{_overlayConfig.Guid}", GetWindowFlags());

		if (_overlayConfig.Fullscreen)
		{
			var screen = ImGui.GetMainViewport();

			// ImGui always leaves a 1px transparent border around the window, so we need to account for that.
			var fsPos = new Vector2(screen.WorkPos.X - 1, screen.WorkPos.Y - 1);
			var fsSize = new Vector2(screen.Size.X + 2 - fsPos.X, screen.Size.Y + 2 - fsPos.Y);

			if (ImGui.GetWindowPos() != fsPos)
			{
				ImGui.SetWindowPos(fsPos, ImGuiCond.Always);
			}

			if (_size.X != fsSize.X || _size.Y != fsSize.Y)
			{
				ImGui.SetWindowSize(fsSize, ImGuiCond.Always);
			}
		}

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

		ImGui.End();
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

	public void SetTexture(IntPtr handle)
	{
		_resizing = false;
		_hasRenderError = false;

		SharedTextureHandler? oldTextureHandler = _textureHandler;
		try
		{
			_textureHandler = new SharedTextureHandler(handle);
		}
		catch (Exception e) { _textureRenderException = e; }

		if (oldTextureHandler != null) { oldTextureHandler.Dispose(); }
	}

	private void HandleMouseEvent()
	{
		// Render proc won't be ready on first boot
		// Totally skip mouse handling for click through overlays, as well
		if (_renderProcess == null || _overlayConfig.ClickThrough) { return; }

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
				_renderProcess.Rpc?.MouseButton(new MouseButtonMessage { Guid = RenderGuid.ToByteArray(), X = (int)mousePos.X, Y = (int)mousePos.Y, Leaving = true })
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

		_renderProcess.Rpc?.MouseButton(new MouseButtonMessage
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
		if (currentSize == _size || _resizing) { return; }

		if (_size == Vector2.Zero)
		{
			_renderProcess.Rpc?.NewOverlay(new NewOverlayMessage
			{
				Guid = RenderGuid.ToByteArray(),
				Id = _overlayConfig.Name,
				Url = _overlayConfig.Url,
				Width = (int)currentSize.X,
				Height = (int)currentSize.Y,
				Zoom = _overlayConfig.Zoom,
				Framerate = _overlayConfig.Framerate,
				Muted = _overlayConfig.Muted,
				CustomCss = _overlayConfig.CustomCss
			}).FireAndForget(_services.PluginLog);
		}
		else
		{
			_renderProcess.Rpc?.ResizeOverlay(RenderGuid, (int)currentSize.X, (int)currentSize.Y)
				.FireAndForget(_services.PluginLog);
		}

		_resizing = true;
		_size = currentSize;
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

	private bool HiddenByCombatFlags()
	{
		if (!_overlayConfig.HideOutOfCombat)
		{
			return false;
		}

		if (_services.ObjectTable.LocalPlayer == null)
		{
			return true;
		}

		if (_services.ObjectTable.LocalPlayer.StatusFlags.HasFlag(StatusFlags.InCombat))
		{
			_timeLastInCombat = DateTimeOffset.Now.ToUnixTimeMilliseconds();
			return false;
		}

		if (!_services.ObjectTable.LocalPlayer.StatusFlags.HasFlag(StatusFlags.InCombat) && _overlayConfig.HideDelay > 0)
		{
			return DateTimeOffset.Now.ToUnixTimeMilliseconds() >= _timeLastInCombat + (_overlayConfig.HideDelay * 1000);
		}

		return true;
	}

	#endregion
}