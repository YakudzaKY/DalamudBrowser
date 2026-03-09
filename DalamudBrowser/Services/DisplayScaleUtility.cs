using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DalamudBrowser.Services;

internal static class DisplayScaleUtility
{
    private const float DefaultScaleFactor = 1f;
    private const float MinScaleFactor = 0.5f;
    private const float MaxScaleFactor = 4f;
    private const uint DefaultDpi = 96;

    private static readonly object SyncRoot = new();
    private static DateTimeOffset nextRefreshUtc = DateTimeOffset.MinValue;
    private static float cachedScaleFactor = DefaultScaleFactor;

    public static float GetScaleFactor()
    {
        lock (SyncRoot)
        {
            if (DateTimeOffset.UtcNow < nextRefreshUtc)
            {
                return cachedScaleFactor;
            }

            cachedScaleFactor = QueryScaleFactor();
            nextRefreshUtc = DateTimeOffset.UtcNow.AddSeconds(1);
            return cachedScaleFactor;
        }
    }

    private static float QueryScaleFactor()
    {
        try
        {
            var hwnd = Process.GetCurrentProcess().MainWindowHandle;
            uint dpi = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) : 0;
            if (dpi == 0)
            {
                dpi = GetDpiForSystem();
            }

            if (dpi == 0)
            {
                dpi = DefaultDpi;
            }

            return Math.Clamp(dpi / (float)DefaultDpi, MinScaleFactor, MaxScaleFactor);
        }
        catch (EntryPointNotFoundException)
        {
            return DefaultScaleFactor;
        }
        catch
        {
            return DefaultScaleFactor;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
