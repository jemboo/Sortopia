using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ResultsExecutionProgressTests
{
    [Fact]
    public void Resumed_estimate_uses_only_generations_completed_in_this_attempt_even_when_updates_are_coalesced()
    {
        var old = SgdItem(100);
        var item = new runParameters(old.Id, old.ProjectId, old.RunId, old.Results, parameterSet.fromBindings([]), old.SetupInputs,
            1, null, null, null, null, Microsoft.FSharp.Core.FSharpOption<sgdRunConfig>.Some(new(10, 10)));
        var clock = new Clock();
        var row = new WorkItemStatusRow("resume.msgpack", item, null, timeProvider: clock);
        row.UpdateExecution(new(item.Id, workItemExecutionState.Running, ""));
        row.QueueGeneration(60);
        clock.Advance(TimeSpan.FromSeconds(10));
        row.QueueGeneration(70); row.RefreshGenerationProgress();
        Assert.Equal("70 / 100", row.GenerationsText);
        Assert.Equal(TimeSpan.FromSeconds(30), row.EstimatedRemaining);
        row.UpdateExecution(new(item.Id, workItemExecutionState.Cancelled, ""));
        row.UpdateExecution(new(item.Id, workItemExecutionState.Running, ""));
        row.QueueGeneration(70); row.RefreshGenerationProgress();
        Assert.Null(row.EstimatedRemaining);
        clock.Advance(TimeSpan.FromSeconds(5));
        row.QueueGeneration(80); row.RefreshGenerationProgress();
        Assert.Equal(TimeSpan.FromSeconds(10), row.EstimatedRemaining);
    }

    private static runParameters SgdItem(int generations) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new resultsConfig("Results", ResultsFunctionNames.standardMsuf4SimpleSgd, SgdFunctions.standardMsuf4Simple.Version, [], []),
        parameterSet.fromBindings([]), [], 1, null, null, null, null, Microsoft.FSharp.Core.FSharpOption<sgdRunConfig>.Some(new(1, generations)));

    [Theory]
    [InlineData(workItemExecutionState.Complete)]
    [InlineData(workItemExecutionState.Failed)]
    [InlineData(workItemExecutionState.Cancelled)]
    public void Remaining_time_tracks_elapsed_and_generations_and_resets_on_retry(workItemExecutionState terminal)
    {
        var item = SgdItem(100);
        var clock = new Clock();
        var row = new WorkItemStatusRow("test.msgpack", item, null, timeProvider: clock);
        Assert.Equal("—", row.EstimatedRemainingText);
        row.UpdateExecution(new(item.Id, workItemExecutionState.Running, ""));
        row.QueueGeneration(0); row.RefreshGenerationProgress();
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Null(row.EstimatedRemaining);
        row.QueueGeneration(10); row.RefreshGenerationProgress();
        Assert.Equal(TimeSpan.FromMinutes(27), row.EstimatedRemaining);
        Assert.Equal("00:27:00", row.EstimatedRemainingText);
        var notifications = new List<string?>();
        row.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
        clock.Advance(TimeSpan.FromMinutes(1)); row.RefreshElapsed();
        Assert.Contains(nameof(row.EstimatedRemainingText), notifications);
        Assert.Equal(TimeSpan.FromMinutes(36), row.EstimatedRemaining);
        notifications.Clear();
        row.QueueGeneration(20); row.RefreshGenerationProgress();
        Assert.Contains(nameof(row.EstimatedRemainingText), notifications);
        Assert.Equal(TimeSpan.FromMinutes(16), row.EstimatedRemaining);
        row.UpdateExecution(new(item.Id, terminal, ""));
        Assert.Equal(terminal == workItemExecutionState.Complete ? "00:00:00" : "—", row.EstimatedRemainingText);
        row.UpdateExecution(new(item.Id, workItemExecutionState.Running, ""));
        Assert.Null(row.EstimatedRemaining);
    }

    [Fact]
    public void Remaining_time_handles_zero_generations_long_durations_and_overflow()
    {
        var clock = new Clock();
        var item = SgdItem(3);
        var row = new WorkItemStatusRow("test.msgpack", item, null, timeProvider: clock);
        row.UpdateExecution(new(item.Id, workItemExecutionState.Running, ""));
        row.QueueGeneration(0); row.RefreshGenerationProgress();
        clock.Advance(TimeSpan.FromHours(14)); row.QueueGeneration(1); row.RefreshGenerationProgress();
        Assert.Equal("28:00:00", row.EstimatedRemainingText);
        row.QueueGeneration(3); row.RefreshGenerationProgress();
        Assert.Equal(TimeSpan.Zero, row.EstimatedRemaining);
        var zero = SgdItem(0);
        var zeroRow = new WorkItemStatusRow("zero.msgpack", zero, null, timeProvider: clock);
        zeroRow.UpdateExecution(new(zero.Id, workItemExecutionState.Running, ""));
        Assert.Null(zeroRow.EstimatedRemaining);
        zeroRow.QueueGeneration(0); zeroRow.RefreshGenerationProgress();
        Assert.Equal(TimeSpan.Zero, zeroRow.EstimatedRemaining);
        var huge = SgdItem(int.MaxValue);
        var hugeRow = new WorkItemStatusRow("huge.msgpack", huge, null, timeProvider: clock);
        hugeRow.UpdateExecution(new(huge.Id, workItemExecutionState.Running, ""));
        hugeRow.QueueGeneration(0); hugeRow.RefreshGenerationProgress();
        clock.Advance(TimeSpan.FromDays(3650)); hugeRow.QueueGeneration(1); hugeRow.RefreshGenerationProgress();
        Assert.Equal("—", hugeRow.EstimatedRemainingText);
    }

    [Fact]
    public async Task Generation_updates_are_coalesced_and_terminal_counts_survive_until_retry()
    {
        var item = SgdItem(6);
        var row = new WorkItemStatusRow("test.msgpack", item, null);
        Assert.Equal("— / 6", row.GenerationsText);
        row.UpdateExecution(new(item.Id, workItemExecutionState.Running, ""));
        var notifications = new List<string?>();
        row.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
        await Task.Run(() => { row.QueueGeneration(0); row.QueueGeneration(2); row.QueueGeneration(4); }, TestContext.Current.CancellationToken);
        Assert.Empty(notifications); Assert.Null(row.CompletedGenerations);
        row.RefreshGenerationProgress();
        Assert.Equal("4 / 6", row.GenerationsText);
        Assert.Single(notifications, name => name == nameof(row.GenerationsText));
        row.QueueGeneration(5);
        row.UpdateExecution(new(item.Id, workItemExecutionState.Cancelled, ""));
        Assert.Equal("5 / 6", row.GenerationsText); // Flush the last value even before the next UI tick.
        row.UpdateExecution(new(item.Id, workItemExecutionState.Running, ""));
        Assert.Equal("— / 6", row.GenerationsText);
        row.QueueGeneration(0); row.RefreshGenerationProgress();
        Assert.Equal("0 / 6", row.GenerationsText);
        row.UpdateExecution(new(item.Id, workItemExecutionState.Complete, ""));
        Assert.Equal("6 / 6", row.GenerationsText);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Completed_files_show_full_generations_without_a_live_attempt(int total)
    {
        var item = SgdItem(total);
        var row = new WorkItemStatusRow("test.msgpack", item, new(item.Id, item.RunId, new(outputState.Complete, [], 1, "")));
        Assert.Equal(total, row.CompletedGenerations);
        Assert.Equal($"{total} / {total}", row.GenerationsText);
        Assert.Equal("00:00:00", row.EstimatedRemainingText);
        Assert.Equal("—", new WorkItemStatusRow("unreadable.msgpack", null, null).GenerationsText);
        Assert.Equal("—", new WorkItemStatusRow("unreadable.msgpack", null, null).EstimatedRemainingText);
    }

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
