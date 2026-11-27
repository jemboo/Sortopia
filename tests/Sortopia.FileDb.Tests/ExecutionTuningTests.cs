using System.Diagnostics;
using Sortopia.Cli;
using Sortopia.FileDb;
using Sortopia.Runs;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class ExecutionTuningTests
{
    [Theory]
    [InlineData(1073741824L)]
    [InlineData(1073741825L)]
    [InlineData(1073745919L)]
    [InlineData(3983572993L)]
    [InlineData(long.MaxValue)]
    public void Windows_memory_cap_uses_whole_pages_without_exceeding_the_selected_budget(long requested)
    {
        var actual = WindowsExecutionLimits.memoryLimitBytes(requested);
        Assert.Equal(0, actual % Environment.SystemPageSize);
        Assert.InRange(requested - actual, 0, Environment.SystemPageSize - 1);
        Assert.Equal(actual, WindowsExecutionLimits.memoryLimitBytes(actual));
    }

    [Theory]
    [InlineData(1, 8, 1, 1)]
    [InlineData(2, 8, 2, 1)]
    [InlineData(8, 8, 8, 4)]
    [InlineData(16, 8, 8, 8)]
    [InlineData(32, 2, 8, 4)]
    [InlineData(16, 1, 8, 2)]
    [InlineData(8, 0.5, 8, 1)]
    [InlineData(2, 0.25, 2, 1)]
    public void Tuning_respects_both_budgets(int cpu, double gib, int workers, int heaps)
    {
        Assert.Equal(workers, ExecutionTuning.workersPerFile(cpu));
        Assert.Equal(heaps, ExecutionTuning.gcHeapCount(cpu, (long)(gib * 1073741824)));
        var options = new resultsExecutionOptions(4, cpu, (long)(gib * 1073741824));
        Assert.Equal(cpu, options.CpuWorkers);
        Assert.Equal(workers, options.MaxWorkersPerFile);
    }

    [Fact]
    public void Child_gc_configuration_overrides_conflicting_inherited_limits()
    {
        var start = new ProcessStartInfo();
        start.Environment["COMPlus_GCHeapHardLimitSOH"] = "10000000";
        start.Environment["DOTNET_GCHeapHardLimitPercent"] = "90";
        start.Environment["COMPlus_gcServer"] = "0";
        var request = new ExecutionWorkerRequest([], 50, 9L * 1073741824, 16, 4, 8L * 1073741824, 8);
        ExecutionWorker.configureEnvironment(start, request);
        Assert.False(start.Environment.ContainsKey("COMPlus_GCHeapHardLimitSOH"));
        Assert.False(start.Environment.ContainsKey("DOTNET_GCHeapHardLimitPercent"));
        Assert.False(start.Environment.ContainsKey("COMPlus_gcServer"));
        Assert.Equal("1", start.Environment["DOTNET_gcServer"]);
        Assert.Equal("8", start.Environment["DOTNET_GCHeapCount"]);
        Assert.Equal("180000000", start.Environment["DOTNET_GCHeapHardLimit"]);
    }

    [Fact]
    public void Concurrent_files_can_use_more_than_the_single_file_ceiling()
    {
        var budget = new ExecutionCpuBudget(16);
        using var first = budget.Register();
        using var second = budget.Register();
        using var a = first.Acquire(ExecutionTuning.workersPerFile(16), TestContext.Current.CancellationToken);
        using var b = second.Acquire(ExecutionTuning.workersPerFile(16), TestContext.Current.CancellationToken);
        Assert.Equal(8, a.WorkerCount);
        Assert.Equal(8, b.WorkerCount);
    }

    [Fact]
    public void Larger_populations_reserve_memory_even_when_the_source_is_shared()
    {
        var parameters = parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.sorterPoolCountKey, 2),
            ParameterBinding.create(ParamKeys.sorterCountPerPoolKey, 512),
            ParameterBinding.create(ParamKeys.sorterChildCountKey, 1)]);
        Assert.Equal(1073741824L, ExecutionTuning.populationReservationBytes(parameters));
        var huge = parameters.WithBinding(ParameterBinding.create(ParamKeys.sorterCountPerPoolKey, int.MaxValue))
            .WithBinding(ParameterBinding.create(ParamKeys.sorterChildCountKey, int.MaxValue));
        Assert.Equal(long.MaxValue, ExecutionTuning.populationReservationBytes(huge));
        var gate = new ExecutionMemoryGate(1073741824L, Microsoft.FSharp.Core.FuncConvert.FromFunc(() => 0L));
        var error = Assert.Throws<InvalidOperationException>(() => gate.Acquire(long.MaxValue, TestContext.Current.CancellationToken));
        Assert.Contains("memory budget", error.Message);
    }
}
