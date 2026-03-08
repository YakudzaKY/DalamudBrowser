using CefSharp;
using CefSharp.OffScreen;
using System.Reflection;

namespace DalamudBrowser.Renderer;

internal static class CefRuntime
{
    private static bool initialized;
    public static string RootCachePath { get; private set; } = string.Empty;

    public static void Initialize(string cacheDirectory, int parentProcessId)
    {
        if (initialized)
        {
            return;
        }

        var runtimeDirectory = AppContext.BaseDirectory;
        var settings = new CefSettings
        {
            BrowserSubprocessPath = Path.Combine(runtimeDirectory, "CefSharp.BrowserSubprocess.exe"),
            ResourcesDirPath = runtimeDirectory,
            LocalesDirPath = Path.Combine(runtimeDirectory, "locales"),
            RootCachePath = cacheDirectory,
#if !DEBUG
            LogSeverity = LogSeverity.Fatal,
#endif
        };

        settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";
        settings.EnableAudio();
        settings.SetOffScreenRenderingBestPerformanceArgs();
        settings.UserAgentProduct = $"Chrome/{Cef.ChromiumVersion} DalamudBrowser/{Assembly.GetEntryAssembly()?.GetName().Version} (ffxiv_pid {parentProcessId}; renderer_pid {Environment.ProcessId})";

        if (Environment.IsPrivilegedProcess)
        {
            settings.CefCommandLineArgs["do-not-de-elevate"] = "1";
        }

        Directory.CreateDirectory(cacheDirectory);
        RootCachePath = cacheDirectory;

        if (!Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null))
        {
            throw new InvalidOperationException("CEF initialization returned false in the renderer process.");
        }

        initialized = true;
    }

    public static void Shutdown()
    {
        if (!initialized)
        {
            return;
        }

        Cef.Shutdown();
        initialized = false;
    }
}
