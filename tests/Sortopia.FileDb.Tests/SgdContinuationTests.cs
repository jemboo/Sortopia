using Microsoft.FSharp.Core;
using Sortopia.Core.Serialization;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    private static void SetTarget(string folder, Guid runId, int interval, int count)
    {
        var path = Path.Combine(folder, "runs", runId + ".msgpack");
        var dto = MessagePackSetup.deserialize<runDto>(File.ReadAllBytes(path));
        dto.sgd = FSharpOption<sgdRunConfig>.Some(new(interval, count));
        File.WriteAllBytes(path, MessagePackSetup.serialize(dto));
    }

    private runExecutionReport ExecuteWithGenerations(string path, List<int> generations, CancellationToken token, Action<int>? observe = null)
    {
        var queued = true;
        return RunParameterExecution.executeQueuedFilesWithProgress([path], () => { if (!queued) return null!; queued = false; return path; },
            new(1), null!, null!, new GenerationUpdates(value => { generations.Add(value.Generation); observe?.Invoke(value.Generation); }), token);
    }

    [Theory]
    [InlineData("Random")]
    [InlineData("Best")]
    public void Snapshot_count_extension_preserves_files_and_matches_uninterrupted_evolution(string selection)
    {
        var (world, consumer) = SgdWorkspace(sgd: new(2, 2), reporting: new("Constant(2)", selection == "Best" ? "Constant(3)" : "Constant(1)"),
            resultsSpans: [new() { keyName = "InitialPoolSelection", values = [selection] }, new() { keyName = "AdjustCeLengthFrequency", values = ["3"] }]);
        var generated = Prepare(world, consumer); Completed(generated.Execution);
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, consumer.Id));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        var item = RunParameterFiles.loadFile(path);
        Assert.Null(item.Parameters.TryFindText("GenerationCount"));
        Assert.Equal(new sgdRunConfig(2, 2), item.Sgd.Value);
        Assert.Equal(new[] { 0, 2, 4 }, RunOutputFiles.samples(item).Snapshots.Select(s => s.Item1));
        var originalParameters = File.ReadAllBytes(path);
        var baseline = Path.Combine(temporary.FullName, "Uninterrupted");
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(baseline, Path.GetRelativePath(folder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        List<int> progress = [];
        Completed(ExecuteWithGenerations(path, progress, Token));
        Assert.Equal(new[] { 0, 2, 4 }, progress);
        var oldResults = Directory.GetFiles(Path.Combine(folder, "data", "results"), "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        var oldKeys = RunOutputFiles.samples(item).Snapshots.Select(s => s.Item2.Id).ToArray();
        SetTarget(folder, item.RunId, 2, 4);
        var extended = RunParameterFiles.loadFile(path);
        Assert.Equal(item.Id, extended.Id);
        Assert.Equal(originalParameters, File.ReadAllBytes(path));
        Assert.Equal(oldKeys, RunOutputFiles.samples(extended).Snapshots.Take(3).Select(s => s.Item2.Id));
        Assert.Equal(outputState.Partial, RunParameterExecution.inspectFile(path, Token).Outputs.State);
        progress.Clear(); Completed(ExecuteWithGenerations(path, progress, Token));
        Assert.Equal(new[] { 4, 6, 8 }, progress);
        foreach (var saved in oldResults) Assert.Equal(saved.Value, File.ReadAllBytes(saved.Key));
        Assert.Equal(outputState.Complete, RunParameterExecution.inspectFile(path, Token).Outputs.State);
        var baselinePath = Path.Combine(baseline, "runParameters", Path.GetFileName(path));
        SetTarget(baseline, item.RunId, 2, 4);
        Completed(RunParameterExecution.executeFile(baselinePath, null!, Token));
        var baselineItem = RunParameterFiles.loadFile(baselinePath);
        foreach (var key in RunOutputFiles.keys(extended))
        {
            var actual = new SortopiaFileDb(folder, item.ProjectId).GetPath(key.Reference);
            var expected = new SortopiaFileDb(baseline, item.ProjectId).GetPath(key.Reference);
            Assert.Equal(File.ReadAllBytes(expected), File.ReadAllBytes(actual));
        }
        var export = SorterEvalReportExport.exportFiles([path], Path.Combine(temporary.FullName, "Extended report"), Token);
        Assert.True(export.SummaryRows > 0);
        Assert.Equal(5, RunOutputFiles.loadSamples(folder, extended, Token).Item1.Snapshots.Length);
    }

    [Fact]
    public void Snapshot_count_zero_saves_initial_population_and_cancellation_resumes_at_checkpoint()
    {
        var (world, consumer) = SgdWorkspace(sgd: new(2, 0));
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, consumer.Id));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        List<int> progress = [];
        Completed(ExecuteWithGenerations(path, progress, Token));
        Assert.Equal(new[] { 0 }, progress);
        var item = RunParameterFiles.loadFile(path);
        SetTarget(folder, item.RunId, 2, 3);
        using var cancellation = new CancellationTokenSource();
        progress.Clear();
        var interrupted = ExecuteWithGenerations(path, progress, cancellation.Token, generation => { if (generation == 2) cancellation.Cancel(); });
        Assert.Equal(runExecutionStatus.Cancelled, interrupted.Status);
        Assert.Equal(new[] { 0, 2 }, progress);
        progress.Clear(); Completed(ExecuteWithGenerations(path, progress, Token));
        Assert.Equal(new[] { 2, 4, 6 }, progress);
        SetTarget(folder, item.RunId, 3, 3);
        Assert.Contains("SnapshotInterval is locked", Assert.ThrowsAny<Exception>(() => RunParameterFiles.loadFile(path)).Message);
    }

    [Fact]
    public void Saved_run_locks_interval_and_allows_only_count_increases_after_generation()
    {
        var (world, consumer) = SgdWorkspace(sgd: new(2, 2));
        Completed(Prepare(world, consumer).Execution);
        var saved = WorkspaceFileStore.LoadDocument(Root);
        WorkspaceDocument Change(int interval, int count)
        {
            var next = MessagePackSetup.deserialize<WorkspaceDocument>(MessagePackSetup.serialize(saved));
            var run = next.Modules.SelectMany(m => m.Projects).Single(p => p.Definition.id == consumer.Id).Runs[0];
            run.sgd = FSharpOption<sgdRunConfig>.Some(new(interval, count));
            return next;
        }
        Assert.Contains("SnapshotInterval is locked", Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.SaveDocument(Root, Change(3, 2), saved, Token)).Message);
        Assert.Contains("only increase", Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.SaveDocument(Root, Change(2, 1), saved, Token)).Message);
        var result = WorkspaceFileStore.SaveDocument(Root, Change(2, 4), saved, Token);
        Assert.Equal(4, result.Definition.Modules.SelectMany(m => m.Projects).Single(p => p.Definition.id == consumer.Id).Runs[0].sgd.Value.SnapshotCount);
        var repeatWorld = WorkspaceFileStore.LoadDefinition(Root);
        var repeat = Prepare(repeatWorld, repeatWorld.ProjectIndex[consumer.Id]); Completed(repeat.Execution);
        Assert.Single(Directory.GetFiles(Path.Combine(WorkspaceData.projectFolder(world, Root, consumer.Id), "runParameters"), "*.msgpack"));
    }
}
