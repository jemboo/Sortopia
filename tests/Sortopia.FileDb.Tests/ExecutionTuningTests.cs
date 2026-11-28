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
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(8, 8)]
    [InlineData(16, 8)]
    [InlineData(32, 8)]
    public void Worker_ceiling_respects_cpu_budget(int cpu, int workers)
    {
        Assert.Equal(workers, ExecutionTuning.workersPerFile(cpu));
        var options = new resultsExecutionOptions(4, cpu, 1073741824L);
        Assert.Equal(cpu, options.CpuWorkers);
        Assert.Equal(workers, options.MaxWorkersPerFile);
    }

    [Fact]
    public void Soft_target_allows_a_single_file_larger_than_the_target()
    {
        const long budget = 1073741824L;
        var gate = new ExecutionMemoryGate(budget, Microsoft.FSharp.Core.FuncConvert.FromFunc(() => 0L));
        var managed = ExecutionResources.managedHeapLimitBytes(budget);
        using (gate.Acquire(managed, TestContext.Current.CancellationToken)) { }
        using var oversized = gate.Acquire(budget + 1, TestContext.Current.CancellationToken);
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
        using var oversized = gate.Acquire(long.MaxValue, TestContext.Current.CancellationToken);
    }
}
