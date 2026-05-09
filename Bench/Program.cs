using System;
using System.Diagnostics;

namespace DalamudBrowser.Services;

class Program
{
    static void Main()
    {
        Console.WriteLine("--- BrowserWorkspace DrawViews Benchmark ---");
        var bench1 = new DalamudBrowser.Bench.BrowserWorkspace_Bench();
        int iterations1 = 100000;

        // Warmup
        bench1.RunBaseline(100);
        bench1.RunOptimized(100);

        var sw1_1 = Stopwatch.StartNew();
        bench1.RunBaseline(iterations1);
        sw1_1.Stop();
        Console.WriteLine($"Baseline: {sw1_1.ElapsedMilliseconds} ms");

        var sw1_2 = Stopwatch.StartNew();
        bench1.RunOptimized(iterations1);
        sw1_2.Stop();
        Console.WriteLine($"Optimized: {sw1_2.ElapsedMilliseconds} ms");

        Console.WriteLine("\n--- TryFindView Search Benchmark (50 views) ---");
        var bench2 = new DalamudBrowser.Bench.TryFindView_Bench(5, 10);
        int iterations2 = 1000000;

        // Warmup
        bench2.RunBaseline(1000);
        bench2.BuildCache();
        bench2.RunOptimized(1000);

        var sw2_1 = Stopwatch.StartNew();
        bench2.RunBaseline(iterations2);
        sw2_1.Stop();
        Console.WriteLine($"Baseline: {sw2_1.ElapsedMilliseconds} ms");

        var sw2_2 = Stopwatch.StartNew();
        bench2.RunOptimized(iterations2);
        sw2_2.Stop();
        Console.WriteLine($"Optimized: {sw2_2.ElapsedMilliseconds} ms");

        Console.WriteLine("\n--- TryFindView Search Benchmark (500 views) ---");
        var bench3 = new DalamudBrowser.Bench.TryFindView_Bench(10, 50);
        bench3.RunBaseline(1000);
        bench3.BuildCache();
        bench3.RunOptimized(1000);

        var sw3_1 = Stopwatch.StartNew();
        bench3.RunBaseline(iterations2);
        sw3_1.Stop();
        Console.WriteLine($"Baseline: {sw3_1.ElapsedMilliseconds} ms");

        var sw3_2 = Stopwatch.StartNew();
        bench3.RunOptimized(iterations2);
        sw3_2.Stop();
        Console.WriteLine($"Optimized: {sw3_2.ElapsedMilliseconds} ms");
    }
}
