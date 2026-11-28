using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class WindowsExecutionLimitsTests
{
    [Fact]
    public async Task Windows_enforces_cpu_and_committed_memory_limits_in_an_independent_process()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string probe = "SORTOPIA_RESOURCE_LIMIT_TEST";
        var mode = Environment.GetEnvironmentVariable(probe);
        const string budgetVariable = "SORTOPIA_RESOURCE_LIMIT_BYTES";
        var requested = long.TryParse(Environment.GetEnvironmentVariable(budgetVariable), out var value) ? value : 1024L * 1024 * 1024;
        if (mode is "1" or "inherited")
        {
            var budget = requested - requested % Environment.SystemPageSize;
            var originalHeapLimit = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            using var limits = mode == "1" ? Sortopia.Cli.WindowsExecutionLimits.applyTracked(80, requested) : null;
            if (limits is not null) Assert.Equal(budget, limits.MemoryBytes);
            var cpu = Query<CpuRate>(15);
            Assert.Equal(5u, cpu.Flags); Assert.Equal(8000u, cpu.Rate);
            var memory = Query<ExtendedLimits>(9);
            Assert.Equal(budget > 0 ? 0x200u : 0u, memory.Basic.Flags & 0x200u);
            Assert.Equal((nuint)budget, memory.JobMemory);
            Assert.Equal(ProcessPriorityClass.BelowNormal, Process.GetCurrentProcess().PriorityClass);
            if (mode == "1")
            {
                if (budget > 0) Assert.InRange(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, 1, budget * 3 / 4);
                else Assert.Equal(originalHeapLimit, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
            }
            // Commit without touching pages. Windows must reject this allocation,
            // even though it bypasses the managed heap and its separate GC limit.
            var allocation = VirtualAlloc(0, (nuint)(budget + 65536), 0x3000, 0x04);
            try
            {
                if (budget > 0) Assert.Equal(nint.Zero, allocation);
                else Assert.NotEqual(nint.Zero, allocation);
            }
            finally { if (allocation != 0) VirtualFree(allocation, 0, 0x8000); }
            if (mode == "inherited") return;
        }

        // Each raw budget gets a fresh launcher so job caps do not accumulate across cases.
        var budgets = mode == "1" ? new[] { requested } : new[] {
            0L, 1073741824L, 1073741825L, 1073745919L,
            ExecutionResources.memoryBytes(13L * 1073741824 + Environment.SystemPageSize, "30%") };
        foreach (var rawBudget in budgets)
        {
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(typeof(WindowsExecutionLimitsTests).Assembly.Location);
            start.ArgumentList.Add("--filter-class"); start.ArgumentList.Add(typeof(WindowsExecutionLimitsTests).FullName!);
            start.Environment[probe] = mode == "1" ? "inherited" : "1";
            start.Environment[budgetVariable] = rawBudget.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var child = Process.Start(start)!;
            var stdout = child.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = child.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try { await child.WaitForExitAsync(timeout.Token); }
            finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(CancellationToken.None); } }
            var output = await stdout + await stderr;
            Assert.True(child.ExitCode == 0, $"Requested budget: {rawBudget}. {output}");
            Assert.Contains("total: 1", output);
        }
    }

    private static T Query<T>(int kind) where T : struct
    {
        var length = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            Assert.True(QueryInformationJobObject(0, kind, buffer, (uint)length, 0), $"Query failed: {Marshal.GetLastWin32Error()}");
            return Marshal.PtrToStructure<T>(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CpuRate { public uint Flags, Rate; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(nint job, int informationClass, nint information, uint length, nint returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protection);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(nint address, nuint size, uint freeType);
}
