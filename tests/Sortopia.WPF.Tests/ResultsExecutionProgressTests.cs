using Sortopia.FileDb;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ResultsExecutionProgressTests
{
    internal sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref ticks, elapsed.Ticks);
    }

    [Theory]
    [InlineData(workItemExecutionState.Complete)]
    [InlineData(workItemExecutionState.Failed)]
    [InlineData(workItemExecutionState.Cancelled)]
    public void Elapsed_updates_while_running_freezes_on_terminal_states_and_resets_on_retry(workItemExecutionState terminal)
    {
        var clock = new Clock();
        var row = new WorkItemStatusRow("test.msgpack", null, null, timeProvider: clock);
        var id = Guid.NewGuid();
        Assert.Null(row.Elapsed); Assert.Equal("—", row.ElapsedText);
        row.UpdateExecution(new(id, workItemExecutionState.Running, ""));
        clock.Advance(TimeSpan.FromMilliseconds(1250));
        var notifications = new List<string?>();
        row.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        row.RefreshElapsed();
        Assert.Contains(nameof(row.ElapsedText), notifications);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), row.Elapsed); Assert.Equal("00:00:01.2", row.ElapsedText);
        row.UpdateExecution(new(id, terminal, ""));
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromMilliseconds(1250), row.Elapsed);
        row.UpdateExecution(new(id, workItemExecutionState.Running, ""));
        Assert.Equal(TimeSpan.Zero, row.Elapsed);
        clock.Advance(TimeSpan.FromHours(27) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3.4));
        Assert.Equal("27:02:03.4", row.ElapsedText);
    }

    [Fact]
    public void Validation_failure_has_no_elapsed_time_and_saved_duration_does_not_start_a_timer()
    {
        var clock = new Clock();
        var row = new WorkItemStatusRow("test.msgpack", null, null, timeProvider: clock);
        row.UpdateExecution(new(Guid.NewGuid(), workItemExecutionState.Failed, "Invalid input"));
        Assert.Null(row.Elapsed);
        row.RestoreElapsed(TimeSpan.FromSeconds(4));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromSeconds(4), row.Elapsed);
    }

    [Fact]
    public void Batch_counts_track_overlapping_jobs_failures_and_cancellation_without_counting_unstarted_work()
    {
        var batch = new ResultsExecutionProgress(4);
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        Assert.Equal(4, batch.NotStarted); Assert.Equal(0, batch.Percent);
        batch.Record(a, workItemExecutionState.Running);
        batch.Record(b, workItemExecutionState.Running);
        batch.Record(a, workItemExecutionState.Running); // Repeated notifications cannot inflate counts.
        Assert.Equal(2, batch.Running); Assert.Equal(2, batch.NotStarted); Assert.Equal(0, batch.Finished);
        Assert.Contains("2 running", batch.Summary);
        batch.Record(a, workItemExecutionState.Complete);
        Assert.Equal(25, batch.Percent); Assert.Equal(1, batch.Running);
        batch.Record(c, workItemExecutionState.Running);
        batch.Record(b, workItemExecutionState.Failed);
        batch.Record(c, workItemExecutionState.Cancelled);
        Assert.Equal(0, batch.Running); Assert.Equal(3, batch.Finished); Assert.Equal(1, batch.NotStarted);
        Assert.Equal(1, batch.Failed); Assert.Equal(1, batch.Cancelled); Assert.Equal(75, batch.Percent);
        Assert.Contains("1 failed", batch.Summary); Assert.Contains("1 cancelled", batch.Summary);
    }
}
