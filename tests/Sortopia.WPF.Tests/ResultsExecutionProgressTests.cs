using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ResultsExecutionProgressTests
{
    private static runParameters SgdItem(int generations) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new resultsConfig("Results", ResultsFunctionNames.standardMsuf4SimpleSgd, SgdFunctions.standardMsuf4Simple.Version, [], []),
        parameterSet.fromBindings([]), [], 1, null, null, null, null, Microsoft.FSharp.Core.FSharpOption<sgdRunConfig>.Some(new(1, generations, null)));

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Completed_files_use_saved_snapshot_metadata_without_inferring_a_generation(int total)
    {
        var item = SgdItem(total);
        var row = new WorkItemStatusRow("test.msgpack", item, new(item.Id, item.RunId, new(outputState.Complete, [], 1, "")));
        Assert.Null(row.CompletedGenerations);
        row.SetSnapshotGeneration(total);
        Assert.Equal(total, row.CompletedGenerations);
        Assert.Equal($"{total} / {total}", row.GenerationsText);
        Assert.Equal("—", new WorkItemStatusRow("unreadable.msgpack", null, null).GenerationsText);
    }

    [Fact]
    public void Temporary_snapshot_marks_pending_outputs_partial_and_can_be_removed()
    {
        var item = SgdItem(6);
        var row = new WorkItemStatusRow("test.msgpack", item, new(item.Id, item.RunId, new(outputState.Pending, [], 1, "")));
        Assert.Equal("— / 6", row.GenerationsText);
        Assert.Equal(outputState.Pending, row.State);
        row.SetSnapshotGeneration(0);
        Assert.Equal("0 / 6", row.GenerationsText);
        Assert.Equal(outputState.Partial, row.State);
        row.SetSnapshotGeneration(4);
        Assert.Equal("4 / 6", row.GenerationsText);
        row.SetSnapshotGeneration(null);
        Assert.Equal("— / 6", row.GenerationsText);
        Assert.Equal(outputState.Pending, row.State);
    }

    [Fact]
    public void Changing_the_saved_target_notifies_the_grid_without_changing_completed_generation()
    {
        var row = new WorkItemStatusRow("test.msgpack", SgdItem(6), null);
        row.SetSnapshotGeneration(4);
        var notifications = new List<string?>();
        row.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
        row.SetSnapshotControls(SgdItem(10));
        Assert.Equal(4, row.CompletedGenerations);
        Assert.Equal("4 / 10", row.GenerationsText);
        Assert.Contains(nameof(row.GenerationsText), notifications);
    }
}
