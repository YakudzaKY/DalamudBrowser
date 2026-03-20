using System;
using System.Diagnostics;

namespace DalamudBrowser.Services;

class Program
{
    static void Main()
    {
        var bench = new DalamudBrowser.Bench.BrowserWorkspace_Bench();
        int iterations = 100000;

        // Warmup
        bench.RunBaseline(100);
        bench.RunOptimized(100);

        var sw1 = Stopwatch.StartNew();
        bench.RunBaseline(iterations);
        sw1.Stop();
        Console.WriteLine($"Baseline: {sw1.ElapsedMilliseconds} ms");

        var sw2 = Stopwatch.StartNew();
        bench.RunOptimized(iterations);
        sw2.Stop();
        Console.WriteLine($"Optimized: {sw2.ElapsedMilliseconds} ms");
    }
}
