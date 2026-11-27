using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class ExecutionResourcesTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Theory]
    [InlineData("8GiB", 8)]
    [InlineData("8", 8)]
    [InlineData("50%", 8)]
    [InlineData("12.5%", 2)]
    public void Memory_percent_uses_available_memory_and_gib_uses_bytes(string input, int expected)
        => Assert.Equal(expected * GiB, ExecutionResources.memoryBytes(16 * GiB, input));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101%")]
    [InlineData("0.01GiB")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Invalid_memory_budgets_are_rejected(string value)
        => Assert.Throws<ArgumentException>(() => ExecutionResources.validateMemory(value));

    [Fact]
    public void Resolved_budgets_cannot_exceed_available_memory_or_be_too_small_to_start()
    {
        Assert.Throws<ArgumentException>(() => ExecutionResources.memoryBytes(GiB, "2GiB"));
        Assert.Throws<ArgumentException>(() => ExecutionResources.memoryBytes(GiB, "1%"));
        Assert.Equal(4, ExecutionResources.cpuWorkers(16, 25));
        Assert.Equal(1, ExecutionResources.cpuWorkers(4, 1));
        Assert.Equal(4, ExecutionResources.parallelJobs(16, "auto"));
        Assert.Equal(2, ExecutionResources.parallelJobs(2, "8"));
    }

    [Fact]
    public async Task Memory_admission_waits_for_active_work_and_can_be_cancelled()
    {
        var gate = new ExecutionMemoryGate(GiB, FuncConvert.FromFunc(() => GiB / 4));
        using var first = gate.Acquire(GiB / 2, TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var waiting = Task.Run(() => gate.Acquire(GiB / 2, cancellation.Token), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var ignored = await waiting; });
    }

    [Fact]
    public void Memory_admission_refuses_a_file_that_cannot_fit_even_when_no_other_file_is_running()
    {
        var gate = new ExecutionMemoryGate(GiB, FuncConvert.FromFunc(() => GiB / 2));
        var error = Assert.Throws<InvalidOperationException>(() => gate.Acquire(GiB / 2, TestContext.Current.CancellationToken));
        Assert.Contains("memory budget", error.Message);
        Assert.Contains("estimated reservation 0.50 GiB", error.Message);
        Assert.Contains("0.30 GiB available after headroom", error.Message);
        Assert.Contains("1.00 GiB worker budget", error.Message);
    }
}
