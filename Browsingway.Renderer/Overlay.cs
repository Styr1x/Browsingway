using Browsingway.Common.Ipc;
using CefSharp;
using CefSharp.Handler;
using CefSharp.OffScreen;
using CefSharp.Structs;
using BrowserSettings = CefSharp.BrowserSettings;
using RequestContext = CefSharp.RequestContext;
using RequestContextSettings = CefSharp.RequestContextSettings;
using Size = System.Drawing.Size;
using WindowInfo = CefSharp.WindowInfo;

namespace Browsingway.Renderer;

internal class Overlay : IDisposable
{
	private readonly string _id;
	private readonly int _framerate;
	public readonly TextureRenderHandler RenderHandler;
	private ChromiumWebBrowser? _browser;
	private string _url;
	private float _zoom;
	private bool _muted;
	private string _customCss;
	private string _customJs;

	public event EventHandler<string>? AddressChanged;
	public event EventHandler<string>? TitleChanged;
	public event EventHandler<string>? FaviconUrlChanged;

	public Overlay(string id, string url, float zoom, bool muted, int framerate, string customCss, string customJs,
		TextureRenderHandler renderHandler)
	{
		_id = id;
		_url = url;
		_zoom = zoom;
		_framerate = framerate;
		_muted = muted;
		_customCss = customCss;
		_customJs = customJs;
		RenderHandler = renderHandler;
	}

	public int Framerate => _framerate;

	public void Dispose()
	{
		RenderHandler.Dispose();

		if (_browser is not null)
		{
			_browser.RenderHandler = null;
			_browser.Dispose();
		}
	}

	/// <summary>
	/// Updates the overlay with new state. Returns true if resize occurred (texture changed).
	/// </summary>
	public bool Update(OverlayState state)
	{
		bool resized = false;

		// Handle resize
		var newSize = new Size(state.Width, state.Height);
		if (newSize != RenderHandler.Size)
		{
			Resize(newSize);
			resized = true;
		}

		// Handle URL change
		if (state.Url != _url)
		{
			Navigate(state.Url);
		}

		// Handle zoom change
		if (Math.Abs(state.Zoom - _zoom) > 0.01f)
		{
			Zoom(state.Zoom);
		}

		// Handle mute change
		if (state.Muted != _muted)
		{
			Mute(state.Muted);
		}

		// Handle CSS change
		if (state.CustomCss != _customCss)
		{
			InjectUserCss(state.CustomCss);
		}

		// Handle JS change
		if (state.CustomJs != _customJs)
		{
			InjectUserJs(state.CustomJs);
		}

		return resized;
	}

	public void Initialise()
	{
		var requestContextSettings = new RequestContextSettings {CachePath = Path.Combine(CefHandler.RootCachePath, _id), PersistSessionCookies = true};
		var rc = new RequestContext(requestContextSettings);

		_browser = new ChromiumWebBrowser(_url, automaticallyCreateBrowser: false, requestContext: rc);
		_browser.RenderHandler = RenderHandler;
		_browser.MenuHandler = new CefMenuHandler();

		var displayHandler = new DisplayHandler();
		displayHandler.TitleChanged += (_, title) => TitleChanged?.Invoke(this, title);
		displayHandler.FaviconUrlChanged += (_, urls) =>
		{
			// Use the first favicon URL if available
			if (urls.Count > 0)
			{
				FaviconUrlChanged?.Invoke(this, urls[0]);
			}
		};
		_browser.DisplayHandler = displayHandler;

		Rect size = RenderHandler.GetViewRect();

		// General _browser config
		WindowInfo windowInfo = new() {Width = size.Width, Height = size.Height};
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
				InjectUserCss(_customCss);
				InjectUserJs(_customJs);
			}
		};

		_browser.AddressChanged += (_, args) =>
		{
			_url = args.Address;
			AddressChanged?.Invoke(this, args.Address);
		};

		BrowserSettings browserSettings = new() {WindowlessFrameRate = _framerate};

		// Ready, boot up the _browser
		_browser.CreateBrowser(windowInfo, browserSettings);

		browserSettings.Dispose();
		windowInfo.Dispose();
	}

	public void InjectUserCss(string css)
	{
		if (css.Length == 0 && _customCss.Length == 0)
			return; // nothing to do

		_customCss = css; // to reapply correctly on load

		// escape rules
		// ` -> \` to prevent end of string
		// ${ -> \${ to prevent variable injection
		// Using a template string (``) instead of a quoted string ('') to not have to deal with javascript
		// newline weirdness (plus it behaves a bit like a verbatim string)
		css = css.Replace("`", @"\'");
		css = css.Replace("${", @"\${");

		// (()=>{...})() self executable function to prevent scope issues
		_browser?.GetMainFrame()?.ExecuteJavaScriptAsync(
			"(()=>{const style = document.getElementById('user-css') ?? document.createElement('style');"
			+ "style.id = 'user-css'; style.textContent =`" + css + " `;document.head.append(style);})()");
	}

	public void InjectUserJs(string js)
	{
		if (js.Length == 0 && _customJs.Length == 0)
			return; // nothing to do

		_customJs = js; // to reapply correctly on load
		_browser?.GetMainFrame()?.ExecuteJavaScriptAsync(js);
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
		_browser?.ShowDevTools();
	}

	public void GoBack()
	{
		_browser?.Back();
	}

	public void GoForward()
	{
		_browser?.Forward();
	}

	public void Reload(bool ignoreCache)
	{
		_browser?.Reload(ignoreCache);
	}

	public void HandleMouseEvent(MouseButtonMessage msg)
	{
		// If the _browser isn't ready yet, noop
		if (_browser == null || !_browser.IsBrowserInitialized) { return; }

		var cursor = DpiScaling.ScaleViewPoint(msg.X, msg.Y);

		// Update the renderer's concept of the mouse cursor
		RenderHandler.SetMousePosition(cursor.X, cursor.Y);

		MouseEvent evt = new(cursor.X, cursor.Y, DecodeInputModifier(msg.Modifier));

		IBrowserHost? host = _browser.GetBrowserHost();

		// Ensure the mouse position is up to date
		host.SendMouseMoveEvent(evt, msg.Leaving);

		// Fire any relevant click events
		List<MouseButtonType> doubleClicks = DecodeMouseButtons(msg.Double);
		DecodeMouseButtons(msg.Down)
			.ForEach(button => host.SendMouseClickEvent(evt, button, false, doubleClicks.Contains(button) ? 2 : 1));
		DecodeMouseButtons(msg.Up).ForEach(button => host.SendMouseClickEvent(evt, button, true, 1));

		// CEF treats the wheel delta as mode 0, pixels. Bump up the numbers to match typical in-_browser experience.
		int deltaMult = 100;
		host.SendMouseWheelEvent(evt, (int)msg.WheelX * deltaMult, (int)msg.WheelY * deltaMult);
	}

	public void HandleKeyEvent(KeyEventMessage request)
	{
		_browser?.GetBrowserHost()?.SendKeyEvent(request.Msg, request.WParam, request.LParam);
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

	/// <summary>
	/// Converts a percentage zoom level (e.g., 100 = 100%) to CEF's logarithmic zoom level.
	/// CEF uses a logarithmic scale where 0 = 100%, positive = zoom in, negative = zoom out.
	/// Formula derived from CEF's zoom level calculation: level = log(percent/100) / log(1.2)
	/// See: https://magpcss.org/ceforum/viewtopic.php?f=6&t=11491
	/// </summary>
	private double ScaleZoomLevel(float zoom)
	{
		if (Math.Abs(zoom - 100f) < 0.5f)
		{
			return 0;
		}

		// Constants: 5.46149645 ≈ 1/log(1.2), and 25.12 ≈ log(100)/log(1.2)
		return (5.46149645 * Math.Log(_zoom)) - 25.12;
	}
}