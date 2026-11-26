using Microsoft.FSharp.Core;
using Sortopia.Evaluation.Sgd;
using Sortopia.Functions;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Fact]
    public void Summary_history_combines_sparse_generations_and_pools_only_for_the_selected_invocation()
    {
        var (world, consumer) = SgdWorkspace(sgd: new(3, 3, null), reporting: new("Constant(3)", "Constant(2)"),
            resultsSpans: [new() { keyName = "SorterPoolCount", values = ["2"] }, new() { keyName = "MutationMod", values = ["0", "1"] }]);
        Completed(Prepare(world, consumer).Execution);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        var items = RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token);
        Assert.Equal(2, items.Length);
        var selected = items[0];
        Assert.Null(RunOutputFiles.loadSummaryHistory(folder, selected, Token));
        foreach (var item in items) Completed(RunParameterExecution.executeFile(Path.Combine(folder, "runParameters", item.Id + ".msgpack"), null!, Token));
        var history = RunOutputFiles.loadSummaryHistory(folder, selected, Token).Value;
        Assert.Equal(new[] { 0, 2, 4, 6, 8 }, history.Summary.Rows.Select(row => row.Generation).Distinct());
        Assert.Equal(10, history.Summary.Rows.Length);
        Assert.Equal(RunOutputFiles.loadSamples(folder, selected, Token).Item2, history.Summary.Rows);
        Assert.Equal(4, history.Files.Length);
        Assert.Empty(history.MissingGenerations);
        Assert.All(history.Summary.Rows.GroupBy(row => row.Generation), group => Assert.Equal(new[] { 0, 1 }, group.Select(row => row.PoolIndex)));
        Assert.All(history.Summary.Rows, row =>
        {
            Assert.Equal(row.SuccessfulCount, row.SuccessfulLengthHistogram.Value.Sum(bin => bin.Count));
            Assert.Equal(row.MemberCount - row.SuccessfulCount, row.UnsuccessfulLengthHistogram.Value.Sum(bin => bin.Count));
        });
        File.SetLastWriteTimeUtc(history.Files[0].Item2, DateTime.UtcNow.AddDays(1));
        File.Delete(history.Files[1].Item2); // A gap before the latest published batch is reported.
        File.Delete(history.Files[^1].Item2); // A future batch is not reported as a gap in a partial history.
        var partial = RunOutputFiles.loadSummaryHistory(folder, selected, Token).Value;
        Assert.Equal(new[] { 2 }, partial.MissingGenerations);
        Assert.Equal(new[] { 0, 4, 6 }, partial.Summary.Rows.Select(row => row.Generation).Distinct());
        Assert.Equal(2, partial.Files.Length);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => RunOutputFiles.loadSummaryHistory(folder, selected, cancelled.Token));
        File.WriteAllBytes(history.Files[0].Item2, [0xc1]);
        var error = Assert.Throws<InvalidDataException>(() => RunOutputFiles.loadSummaryHistory(folder, selected, Token));
        Assert.Contains(history.Files[0].Item2, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Summary_history_rejects_mismatched_generations_or_test_sets(bool differentTests)
    {
        var (world, consumer) = SgdWorkspace(sgd: new(3, 2, null));
        Completed(Prepare(world, consumer).Execution);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        var item = Assert.Single(RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token));
        Completed(RunParameterExecution.executeFile(Path.Combine(folder, "runParameters", item.Id + ".msgpack"), null!, Token));
        var key = RunOutputFiles.samples(item).Summaries[0].Item2;
        var store = new SortopiaFileDb(folder, item.ProjectId);
        var path = store.GetPath(key.Reference);
        var altered = FileInspection.loadPoolSummary(path, Token);
        if (differentTests) altered.TestsId = Guid.NewGuid();
        else foreach (var row in altered.Rows) row.Generation += 100;
        File.Delete(path);
        var saved = ((ISortopiaDb)store).GetOrCreate(key, FuncConvert.FromFunc(() =>
            FSharpResult<sortopiaStorageData, string>.NewOk(sortopiaStorageData.NewSorterPoolSummarySet(altered))), Token);
        Assert.True(saved.IsOk);
        var error = Assert.ThrowsAny<Exception>(() => RunOutputFiles.loadSummaryHistory(folder, item, Token));
        Assert.Contains(differentTests ? "different test set" : "Pool summary samples", error.Message);
    }
}
