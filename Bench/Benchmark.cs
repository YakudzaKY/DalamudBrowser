using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DalamudBrowser.Services;

public class Benchmark
{
    public static void Run()
    {
        var testUri = new Uri("http://example.com/path?param1=value1+test&OVERLAY_WS=ws://127.0.0.1:10501/ws&param3=value3+more+test");
        var testUri2 = new Uri("http://example.com/path?param1=value1+test&param3=value3+more+test&OVERLAY_WS=ws://127.0.0.1:10501/ws");
        var testUri3 = new Uri("http://example.com/path?OVERLAY_WS=ws://127.0.0.1:10501/ws&param1=value1+test&param3=value3+more+test");
        var testUri4 = new Uri("http://example.com/path?param1=value1+test&param3=value3+more+test"); // No match

        int iter = 5000000;

        // Warm up
        for (int i = 0; i < 10000; i++)
        {
            BrowserUrlUtility.TryGetActOverlayWebSocket(testUri.AbsoluteUri, out _);
            BrowserUrlUtility.TryGetActOverlayWebSocket(testUri2.AbsoluteUri, out _);
            BrowserUrlUtility.TryGetActOverlayWebSocket(testUri3.AbsoluteUri, out _);
            BrowserUrlUtility.TryGetActOverlayWebSocket(testUri4.AbsoluteUri, out _);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iter; i++)
        {
            BrowserUrlUtility.TryGetActOverlayWebSocket(testUri.AbsoluteUri, out _);
            BrowserUrlUtility.TryGetActOverlayWebSocket(testUri2.AbsoluteUri, out _);
            BrowserUrlUtility.TryGetActOverlayWebSocket(testUri3.AbsoluteUri, out _);
            BrowserUrlUtility.TryGetActOverlayWebSocket(testUri4.AbsoluteUri, out _);
        }
        sw.Stop();
        Console.WriteLine($"TryGetActOverlayWebSocket (baseline) took {sw.ElapsedMilliseconds} ms");

        // Warm up
        for (int i = 0; i < 10000; i++)
        {
            BrowserUrlUtilitySpan.TryGetActOverlayWebSocket(testUri.AbsoluteUri, out _);
            BrowserUrlUtilitySpan.TryGetActOverlayWebSocket(testUri2.AbsoluteUri, out _);
            BrowserUrlUtilitySpan.TryGetActOverlayWebSocket(testUri3.AbsoluteUri, out _);
            BrowserUrlUtilitySpan.TryGetActOverlayWebSocket(testUri4.AbsoluteUri, out _);
        }

        sw.Restart();
        for (int i = 0; i < iter; i++)
        {
            BrowserUrlUtilitySpan.TryGetActOverlayWebSocket(testUri.AbsoluteUri, out _);
            BrowserUrlUtilitySpan.TryGetActOverlayWebSocket(testUri2.AbsoluteUri, out _);
            BrowserUrlUtilitySpan.TryGetActOverlayWebSocket(testUri3.AbsoluteUri, out _);
            BrowserUrlUtilitySpan.TryGetActOverlayWebSocket(testUri4.AbsoluteUri, out _);
        }
        sw.Stop();
        Console.WriteLine($"TryGetActOverlayWebSocket (optimized) took {sw.ElapsedMilliseconds} ms");
    }
}
