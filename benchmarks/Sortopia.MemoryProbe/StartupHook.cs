using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

// Opt-in benchmark hook; not referenced or shipped by the application.
public static class StartupHook
{
    private static StreamWriter? output;
    private static Timer? timer;
    private static readonly object gate = new();
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static int recordingOom;
    private static bool closed;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    public static void Initialize()
    {
        var folder = Environment.GetEnvironmentVariable("SORTOPIA_MEMORY_PROBE");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        output = new StreamWriter(Path.Combine(folder, $"memory-{Environment.ProcessId}.csv"), false) { AutoFlush = true };
        output.WriteLine("Seconds,PrivateBytes,PeakPrivateBytes,LiveManagedBytes,GcCommittedBytes,HeapBytes,FragmentedBytes,HeapLimitBytes,AllocatedBytes,Gen2,ServerGc,Gen0,Gen1,GcPauseSeconds,ThreadPoolWorkItems,ThreadPoolThreads,UserCpuSeconds,KernelCpuSeconds,MemoryLoadBytes,HighMemoryLoadThresholdBytes,SystemIdleTicks,SystemKernelTicks,SystemUserTicks");
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            timer?.Dispose();
            lock (gate) { Sample(null); closed = true; output.Dispose(); }
        };
        timer = new Timer(Sample, null, 0, 250);
    }

    private static void Sample(object? _)
    {
        lock (gate)
        {
            if (closed) return;
            using var process = Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            ulong idle = 0, kernel = 0, user = 0;
            if (OperatingSystem.IsWindows()) GetSystemTimes(out idle, out kernel, out user);
            output!.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{clock.Elapsed.TotalSeconds:F3},{process.PrivateMemorySize64},{process.PeakPagedMemorySize64},{GC.GetTotalMemory(false)},{gc.TotalCommittedBytes},{gc.HeapSizeBytes},{gc.FragmentedBytes},{gc.TotalAvailableMemoryBytes},{GC.GetTotalAllocatedBytes(false)},{GC.CollectionCount(2)},{GCSettings.IsServerGC},{GC.CollectionCount(0)},{GC.CollectionCount(1)},{GC.GetTotalPauseDuration().TotalSeconds:F3},{ThreadPool.CompletedWorkItemCount},{ThreadPool.ThreadCount},{process.UserProcessorTime.TotalSeconds:F3},{process.PrivilegedProcessorTime.TotalSeconds:F3},{gc.MemoryLoadBytes},{gc.HighMemoryLoadThresholdBytes},{idle},{kernel},{user}"));
        }
    }

    private static void OnException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (args.Exception is not OutOfMemoryException || Interlocked.Exchange(ref recordingOom, 1) != 0) return;
        try
        {
            var folder = Environment.GetEnvironmentVariable("SORTOPIA_MEMORY_PROBE")!;
            File.WriteAllText(Path.Combine(folder, $"oom-{Environment.ProcessId}.txt"),
                $"{clock.Elapsed.TotalSeconds:F3} seconds\n{new StackTrace(1, true)}");
        }
        catch { /* Do not interfere with the exception being investigated. */ }
    }
}
