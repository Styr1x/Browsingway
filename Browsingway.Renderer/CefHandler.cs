using CefSharp;
using CefSharp.OffScreen;
using System.Reflection;

namespace Browsingway.Renderer;

internal static class CefHandler
{
	public static string RootCachePath { get; private set; } = null!;
	
	public static void Initialise(string cefAssemblyPath, string cefCacheDir, int parentPid)
	{
		CefSettings settings = new()
		{
			BrowserSubprocessPath = Path.Combine(cefAssemblyPath, "CefSharp.BrowserSubprocess.exe"), RootCachePath = cefCacheDir,
#if !DEBUG
			LogSeverity = LogSeverity.Fatal,
#endif
		};
		RootCachePath = settings.RootCachePath;
		settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";
		settings.EnableAudio();
		settings.SetOffScreenRenderingBestPerformanceArgs();
		settings.UserAgentProduct = $"Chrome/{Cef.ChromiumVersion} Browsingway/{Assembly.GetEntryAssembly()?.GetName().Version} (ffxiv_pid {parentPid}; renderer_pid {Environment.ProcessId})";

		Cef.Initialize(settings, false, browserProcessHandler: null);
	}

	public static void Shutdown()
	{
		Cef.Shutdown();
	}
}