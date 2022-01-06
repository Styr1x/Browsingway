using CefSharp;
using CefSharp.OffScreen;

namespace Browsingway.Renderer;

internal static class CefHandler
{
	public static void Initialise(string cefAssemblyPath, string cefCacheDir, int parentProcessId)
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
		settings.UserAgent = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{Cef.CefSharpVersion} Safari/537.36 ProcessID/{parentProcessId}";

		Cef.Initialize(settings, false, browserProcessHandler: null);
	}

	public static void Shutdown()
	{
		Cef.Shutdown();
	}
}