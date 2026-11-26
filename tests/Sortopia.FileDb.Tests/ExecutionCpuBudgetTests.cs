using Sortopia.FileDb;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class ExecutionCpuBudgetTests
{
    private CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Active_files_share_remainders_and_reclaim_workers_as_files_finish()
    {
        var budget = new ExecutionCpuBudget(10);
        using var first = budget.Register();
        using var second = budget.Register();
        using var third = budget.Register();
        using (var a = first.Acquire(10, Token))
        using (var b = second.Acquire(10, Token))
        using (var c = third.Acquire(10, Token))
        {
            Assert.Equal(4, a.WorkerCount);
            Assert.Equal(3, b.WorkerCount);
            Assert.Equal(3, c.WorkerCount);
        }
        third.Dispose();
        using (var a = first.Acquire(10, Token)) Assert.Equal(5, a.WorkerCount);
        second.Dispose();
        using (var a = first.Acquire(10, Token)) Assert.Equal(10, a.WorkerCount);
        using (var a = first.Acquire(2, Token)) Assert.Equal(2, a.WorkerCount);
    }

    [Fact]
    public async Task Joining_file_waits_for_in_flight_phase_without_overcommitting()
    {
        var budget = new ExecutionCpuBudget(8);
        using var first = budget.Register();
        using var oldPhase = first.Acquire(8, Token);
        using var second = budget.Register();
        using var entered = new ManualResetEventSlim();
        var waiting = Task.Run(() => { entered.Set(); return second.Acquire(8, Token); }, Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Token));
        Assert.False(waiting.IsCompleted);
        oldPhase.Dispose();
        using var newPhase = await waiting.WaitAsync(TimeSpan.FromSeconds(5), Token);
        using var nextPhase = first.Acquire(8, Token);
        Assert.Equal(4, newPhase.WorkerCount);
        Assert.Equal(4, nextPhase.WorkerCount);
    }

    [Fact]
    public async Task Cancellation_and_repeated_disposal_do_not_leak_or_create_workers()
    {
        var budget = new ExecutionCpuBudget(4);
        using var first = budget.Register();
        using var phase = first.Acquire(4, Token);
        using var second = budget.Register();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var waiting = Task.Run(() => second.Acquire(4, cancellation.Token), Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var ignored = await waiting; });
        phase.Dispose();
        phase.Dispose();
        first.Dispose();
        first.Dispose();
        using var all = second.Acquire(20, Token);
        Assert.Equal(4, all.WorkerCount);
        Assert.Throws<ObjectDisposedException>(() => first.Acquire(1, Token));
    }

    [Fact]
    public async Task Concurrent_phases_never_exceed_the_total_worker_budget()
    {
        var budget = new ExecutionCpuBudget(7);
        var inUse = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            using var file = budget.Register();
            for (var i = 0; i < 100; i++)
            {
                using var phase = file.Acquire(7, Token);
                var current = Interlocked.Add(ref inUse, phase.WorkerCount);
                Assert.InRange(current, 1, 7);
                Thread.Yield();
                Interlocked.Add(ref inUse, -phase.WorkerCount);
            }
        }, Token)));
        Assert.Equal(0, inUse);
        using var last = budget.Register();
        using var final = last.Acquire(7, Token);
        Assert.Equal(7, final.WorkerCount);
    }
}
