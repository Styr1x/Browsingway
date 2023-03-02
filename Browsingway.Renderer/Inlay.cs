using Browsingway.Common;
using Browsingway.Renderer.RenderHandlers;
using CefSharp;
using CefSharp.OffScreen;
using CefSharp.Structs;
using BrowserSettings = CefSharp.BrowserSettings;
using Cef = CefSharp.Cef;
using RequestContext = CefSharp.RequestContext;
using RequestContextSettings = CefSharp.RequestContextSettings;
using Size = System.Drawing.Size;
using WindowInfo = CefSharp.WindowInfo;

namespace Browsingway.Renderer;

internal class Inlay : IDisposable
{
	private readonly string _id;
	private readonly int _framerate;
	public readonly BaseRenderHandler RenderHandler;
	private ChromiumWebBrowser? _browser;
	private string _url;
	private float _zoom;
	private bool _muted;

	public Inlay(string id, string url, float zoom, bool muted, int framerate, BaseRenderHandler renderHandler)
	{
		_id = id;
		_url = url;
		_zoom = zoom;
		_framerate = framerate;
		_muted = muted;
		RenderHandler = renderHandler;
	}

	public void Dispose()
	{
		RenderHandler.Dispose();

		if (_browser is not null)
		{
			_browser.RenderHandler = null;
			_browser.Dispose();
		}
	}

	public void Initialise()
	{
		var requestContextSettings = new RequestContextSettings { CachePath = Path.Combine(Cef.GetGlobalRequestContext().CachePath, _id), PersistUserPreferences = true, PersistSessionCookies = true };
		var rc = new RequestContext(requestContextSettings);

		_browser = new ChromiumWebBrowser(_url, automaticallyCreateBrowser: false, requestContext: rc);
		_browser.RenderHandler = RenderHandler;
		_browser.MenuHandler = new CefMenuHandler();
		Rect size = RenderHandler.GetViewRect();

		// General _browser config
		WindowInfo windowInfo = new() { Width = size.Width, Height = size.Height };
		windowInfo.SetAsWindowless(IntPtr.Zero);

		// WindowInfo gets ignored sometimes, be super sure:
		_browser.BrowserInitialized += (_, _) =>
		{
			_browser.Size = new Size(size.Width, size.Height);
			Mute(_muted);
		};

		_browser.LoadingStateChanged += (_, args) =>
		{
			if (!args.IsLoading)
			{
				_browser.SetZoomLevel(ScaleZoomLevel(_zoom));
			}
		};

		BrowserSettings browserSettings = new() { WindowlessFrameRate = _framerate };

		// Ready, boot up the _browser
		_browser.CreateBrowser(windowInfo, browserSettings);

		browserSettings.Dispose();
		windowInfo.Dispose();
	}

	public void Navigate(string newUrl)
	{
		// If navigating to the same _url, force a clean reload
		if (_browser?.Address == newUrl)
		{
			_browser.Reload(true);
			return;
		}

		// Otherwise load regularly
		_url = newUrl;
		_browser?.Load(newUrl);
	}

	public void Zoom(float zoom)
	{
		_zoom = zoom;
		_browser?.SetZoomLevel(ScaleZoomLevel(zoom));
	}

	public void Mute(bool mute)
	{
		_muted = mute;
		_browser?.GetBrowserHost().SetAudioMuted(mute);
	}

	public void Debug()
	{
		_browser.ShowDevTools();
	}

	public void HandleMouseEvent(MouseEventRequest request)
	{
		// If the _browser isn't ready yet, noop
		if (_browser == null || !_browser.IsBrowserInitialized) { return; }

		var cursor = DpiScaling.ScaleViewPoint(request.X, request.Y);

		// Update the renderer's concept of the mouse cursor
		RenderHandler.SetMousePosition(cursor.X, cursor.Y);

		MouseEvent evt = new(cursor.X, cursor.Y, DecodeInputModifier(request.Modifier));

		IBrowserHost? host = _browser.GetBrowserHost();

		// Ensure the mouse position is up to date
		host.SendMouseMoveEvent(evt, request.Leaving);

		// Fire any relevant click events
		List<MouseButtonType> doubleClicks = DecodeMouseButtons(request.Double);
		DecodeMouseButtons(request.Down)
			.ForEach(button => host.SendMouseClickEvent(evt, button, false, doubleClicks.Contains(button) ? 2 : 1));
		DecodeMouseButtons(request.Up).ForEach(button => host.SendMouseClickEvent(evt, button, true, 1));

		// CEF treats the wheel delta as mode 0, pixels. Bump up the numbers to match typical in-_browser experience.
		int deltaMult = 100;
		host.SendMouseWheelEvent(evt, (int)request.WheelX * deltaMult, (int)request.WheelY * deltaMult);
	}

	public void HandleKeyEvent(KeyEventRequest request)
	{
		_browser.GetBrowserHost().SendKeyEvent(request.Msg, request.WParam, request.LParam);
	}

	public void Resize(Size size)
	{
		// Need to resize renderer first, the _browser will check it (and hence the texture) when _browser.Size is set.
		RenderHandler.Resize(size);
		if (_browser is not null)
		{
			_browser.Size = size;
		}
	}

	private List<MouseButtonType> DecodeMouseButtons(MouseButton buttons)
	{
		List<MouseButtonType> result = new();
		if ((buttons & MouseButton.Primary) == MouseButton.Primary) { result.Add(MouseButtonType.Left); }

		if ((buttons & MouseButton.Secondary) == MouseButton.Secondary) { result.Add(MouseButtonType.Right); }

		if ((buttons & MouseButton.Tertiary) == MouseButton.Tertiary) { result.Add(MouseButtonType.Middle); }

		return result;
	}

	private CefEventFlags DecodeInputModifier(InputModifier modifier)
	{
		CefEventFlags result = CefEventFlags.None;
		if ((modifier & InputModifier.Shift) == InputModifier.Shift) { result |= CefEventFlags.ShiftDown; }

		if ((modifier & InputModifier.Control) == InputModifier.Control) { result |= CefEventFlags.ControlDown; }

		if ((modifier & InputModifier.Alt) == InputModifier.Alt) { result |= CefEventFlags.AltDown; }

		return result;
	}

	private double ScaleZoomLevel(float zoom)
	{
		if (Math.Abs(zoom - 100f) < 0.5f)
		{
			return 0;
		}

		return (5.46149645 * Math.Log(_zoom)) - 25.12;
	}
}