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
    public async Task Oversized_file_runs_alone_then_releases_the_next_file_even_above_target()
    {
        var token = TestContext.Current.CancellationToken;
        var gate = new ExecutionMemoryGate(GiB, FuncConvert.FromFunc(() => 2 * GiB));
        var first = gate.Acquire(long.MaxValue, token);
        using var attempted = new ManualResetEventSlim();
        var waiting = Task.Run(() => { attempted.Set(); return gate.Acquire(GiB, token); }, token);
        try
        {
            Assert.True(attempted.Wait(TimeSpan.FromSeconds(5), token));
            await Assert.ThrowsAsync<TimeoutException>(async () => await waiting.WaitAsync(TimeSpan.FromMilliseconds(100), token));
        }
        finally { first.Dispose(); }
        using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(5), token);
    }

    [Fact]
    public void Files_that_fit_the_full_target_can_overlap_without_a_gc_allowance()
    {
        var gate = new ExecutionMemoryGate(GiB, FuncConvert.FromFunc(() => GiB / 4));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var first = gate.Acquire(GiB / 2, timeout.Token);
        using var second = gate.Acquire(GiB / 4, timeout.Token);
    }

    [Fact]
    public void No_target_does_not_gate_memory_but_still_honors_cancellation()
    {
        var gate = new ExecutionMemoryGate(0, FuncConvert.FromFunc(() => long.MaxValue));
        using var first = gate.Acquire(long.MaxValue, TestContext.Current.CancellationToken);
        using var second = gate.Acquire(long.MaxValue, TestContext.Current.CancellationToken);
        Assert.ThrowsAny<OperationCanceledException>(() => gate.Acquire(1, new CancellationToken(true)));
    }
}
