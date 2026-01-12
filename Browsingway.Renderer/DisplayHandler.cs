using CefSharp;
using CefDisplayHandler = CefSharp.Handler.DisplayHandler;

namespace Browsingway.Renderer;

internal class DisplayHandler : CefDisplayHandler
{
	public event EventHandler<string>? TitleChanged;
	public event EventHandler<IList<string>>? FaviconUrlChanged;

	protected override void OnTitleChanged(IWebBrowser chromiumWebBrowser, TitleChangedEventArgs titleChangedArgs)
	{
		TitleChanged?.Invoke(this, titleChangedArgs.Title);
	}

	protected override void OnFaviconUrlChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IList<string> urls)
	{
		FaviconUrlChanged?.Invoke(this, urls);
	}
}
