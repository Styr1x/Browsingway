using CefSharp;
using CefSharp.OffScreen;

namespace Browsingway.Renderer;

internal static class CefHandler
{
	public static void Initialise(string cefAssemblyPath, string cefCacheDir)
	{
		CefSettings settings = new()
		{
			BrowserSubprocessPath = Path.Combine(cefAssemblyPath, "CefSharp.BrowserSubprocess.exe"), CachePath = cefCacheDir,
#if !DEBUG
				LogSeverity = LogSeverity.Fatal,
#endif
		};
		settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";
		settings.EnableAudio();
		settings.SetOffScreenRenderingBestPerformanceArgs();

		Cef.Initialize(settings, false, browserProcessHandler: null);
	}

	public static void Shutdown()
	{
		Cef.Shutdown();
	}
}