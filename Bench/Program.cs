using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

class Program
{
    static readonly string[] ActProcessNames =
    [
        "Advanced Combat Tracker",
        "AdvancedCombatTracker",
        "ACTx64",
        "ACTx86",
        "NonExistentProcess1",
        "NonExistentProcess2"
    ];

    static void Main()
    {
        // Warmup
        Method1();
        Method4();

        int iters = 100;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iters; i++)
        {
            Method1();
        }
        sw.Stop();
        Console.WriteLine($"Method1 (GetProcessesByName loop): {sw.ElapsedMilliseconds}ms");

        sw = Stopwatch.StartNew();
        for (int i = 0; i < iters; i++)
        {
            Method4();
        }
        sw.Stop();
        Console.WriteLine($"Method4 (GetProcesses + HashSet proposed): {sw.ElapsedMilliseconds}ms");
    }

    static bool Method1()
    {
        foreach (var processName in ActProcessNames)
        {
            Process[] matches = null;
            try
            {
                matches = Process.GetProcessesByName(processName);
                if (matches.Length > 0)
                {
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                if (matches != null)
                {
                    foreach (var match in matches)
                    {
                        match.Dispose();
                    }
                }
            }
        }
        return false;
    }

    static bool Method4()
    {
        Process[] allProcesses = null;
        try
        {
            allProcesses = Process.GetProcesses();
            var runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var process in allProcesses)
            {
                try
                {
                    runningNames.Add(process.ProcessName);
                }
                catch
                {
                    // Ignore processes that cannot be accessed
                }
            }

            foreach (var processName in ActProcessNames)
            {
                if (runningNames.Contains(processName))
                {
                    return true;
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (allProcesses != null)
            {
                foreach (var process in allProcesses)
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }
}
