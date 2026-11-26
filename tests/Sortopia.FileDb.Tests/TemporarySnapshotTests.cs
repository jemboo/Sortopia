using Microsoft.FSharp.Core;
using Microsoft.FSharp.Collections;
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
    [Theory]
    [InlineData("Random", "Constant(1)", false)]
    [InlineData("Best", "Constant(3)", false)]
    [InlineData("Random", "Constant(1)", true)]
    [InlineData("Best", "Constant(3)", true)]
    public void Temporary_snapshots_replace_resume_and_preserve_all_report_bytes(string selection, string summarySchedule, bool msrs)
    {
        var (world, consumer) = SgdWorkspace(sgd: new(5, 2, FSharpOption<int>.Some(2)), reporting: new("Constant(5)", summarySchedule),
            resultsSpans: [new() { keyName = "InitialPoolSelection", values = [selection] }], msrs: msrs);
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        var item = RunParameterFiles.loadFile(path);
        var originalParameters = File.ReadAllBytes(path);
        var baseline = Path.Combine(temporary.FullName, "Uninterrupted temporary snapshots");
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(baseline, Path.GetRelativePath(folder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
        }
        var tempPath = RunOutputFiles.temporarySnapshotPath(folder, item);
        Assert.Null(RunOutputFiles.completedGeneration(folder, item, Token));
        using var cancellation = new CancellationTokenSource();
        List<int> observed = [];
        byte[]? previous = null;
        var report = ExecuteWithGenerations(path, observed, cancellation.Token, generation =>
        {
            Assert.Equal(generation, RunOutputFiles.completedGeneration(folder, item, Token).Value);
            if (generation == 2) previous = File.ReadAllBytes(tempPath);
            if (generation == 4)
            {
                Assert.NotEqual(previous, File.ReadAllBytes(tempPath));
                Assert.Single(Directory.GetFiles(Path.GetDirectoryName(tempPath)!, "*.msgpack"));
                cancellation.Cancel();
            }
        });
        Assert.True(report.Status == runExecutionStatus.Cancelled, report.Error);
        Assert.Equal(new[] { 0, 2, 4 }, observed);
        File.WriteAllText(tempPath + ".unfinished.tmp", "unpublished write");
        Assert.Equal(4, RunOutputFiles.completedGeneration(folder, item, Token).Value);
        var neverKeep = FuncConvert.FromFunc<storedDataReference, FSharpList<storedDataReference>, bool>((_, _) => false);
        Assert.DoesNotContain(tempPath, RunDataCleanup.plan(folder, item.ProjectId, SetModule.Empty<Guid>(), neverKeep, Token));
        Assert.Contains(tempPath, RunDataCleanup.plan(folder, item.ProjectId, SetModule.Singleton(item.RunId), neverKeep, Token));
        Assert.False(File.Exists(RunOutputFiles.snapshotPath(folder, item, 5)));
        observed.Clear();
        Completed(ExecuteWithGenerations(path, observed, Token, generation =>
        {
            if (generation is 5 or 10) Assert.False(File.Exists(tempPath));
        }));
        Assert.Equal(new[] { 4, 5, 6, 8, 10 }, observed);
        Assert.Equal(10, RunOutputFiles.completedGeneration(folder, item, Token).Value);
        Assert.False(File.Exists(tempPath));
        Assert.Equal(originalParameters, File.ReadAllBytes(path));
        Completed(RunParameterExecution.executeFile(Path.Combine(baseline, "runParameters", Path.GetFileName(path)), null!, Token));
        foreach (var key in RunOutputFiles.keys(item))
            Assert.Equal(File.ReadAllBytes(new SortopiaFileDb(baseline, item.ProjectId).GetPath(key.Reference)),
                File.ReadAllBytes(new SortopiaFileDb(folder, item.ProjectId).GetPath(key.Reference)));
    }

    [Fact]
    public void Temporary_resume_replays_a_missing_preceding_summary_instead_of_skipping_it()
    {
        var (world, consumer) = SgdWorkspace(sgd: new(5, 2, FSharpOption<int>.Some(2)));
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        var item = RunParameterFiles.loadFile(path);
        using var cancellation = new CancellationTokenSource();
        List<int> observed = [];
        var interrupted = ExecuteWithGenerations(path, observed, cancellation.Token, generation => { if (generation == 8) cancellation.Cancel(); });
        Assert.True(interrupted.Status == runExecutionStatus.Cancelled, interrupted.Error);
        var missing = RunOutputFiles.samples(item).Summaries.Single(batch => batch.Item1.Last() == 5).Item2;
        var missingPath = new SortopiaFileDb(folder, item.ProjectId).GetPath(missing.Reference);
        var expected = File.ReadAllBytes(missingPath); File.Delete(missingPath);
        observed.Clear(); Completed(ExecuteWithGenerations(path, observed, Token));
        Assert.Equal(0, observed[0]); Assert.Equal(expected, File.ReadAllBytes(missingPath));
        Assert.Equal(outputState.Complete, RunParameterExecution.inspectFile(path, Token).Outputs.State);
    }

    [Fact]
    public void Damaged_temporary_snapshot_is_reported_and_does_not_overwrite_permanent_results()
    {
        var (world, consumer) = SgdWorkspace(sgd: new(5, 1, FSharpOption<int>.Some(2)));
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        var item = RunParameterFiles.loadFile(path);
        using var cancellation = new CancellationTokenSource();
        var interrupted = ExecuteWithGenerations(path, [], cancellation.Token, generation => { if (generation == 2) cancellation.Cancel(); });
        Assert.True(interrupted.Status == runExecutionStatus.Cancelled, interrupted.Error);
        var tempPath = RunOutputFiles.temporarySnapshotPath(folder, item);
        var original = File.ReadAllBytes(RunOutputFiles.snapshotPath(folder, item, 0));
        var damaged = File.ReadAllBytes(tempPath); damaged[^1] ^= 1; File.WriteAllBytes(tempPath, damaged);
        var failed = RunParameterExecution.executeFile(path, null!, Token);
        Assert.Equal(runExecutionStatus.Failed, failed.Status); Assert.Contains("checksum", failed.Error);
        Assert.Equal(original, File.ReadAllBytes(RunOutputFiles.snapshotPath(folder, item, 0)));
        Assert.Equal(damaged, File.ReadAllBytes(tempPath));
    }

    [Fact]
    public void Temp_interval_is_live_run_metadata_and_does_not_change_parameter_or_permanent_output_identities()
    {
        var legacy = MessagePackSetup.deserialize<sgdRunConfig>(MessagePackSetup.serialize(new { SnapshotInterval = 5, SnapshotCount = 2 }));
        Assert.Equal(10, SgdRun.tempInterval(SgdRun.validate(legacy)));
        var (world, consumer) = SgdWorkspace(sgd: new(5, 2, null));
        Completed(Prepare(world, consumer).Execution);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        var before = RunParameterFiles.loadFile(file);
        Assert.Equal(10, SgdRun.tempInterval(before.Sgd.Value));
        var bytes = File.ReadAllBytes(file);
        var saved = WorkspaceFileStore.LoadWorkspace(Root);
        var edited = saved.CreateEditDocument();
        new WorkspaceDocumentIndex(edited).TryFindRun(before.RunId).Value.Document.sgd.Value.TempSnapshotInterval = FSharpOption<int>.Some(3);
        WorkspaceFileStore.SaveDocument(edited, saved, Token);
        var after = RunParameterFiles.loadFile(file);
        Assert.Equal(3, SgdRun.tempInterval(after.Sgd.Value)); Assert.Equal(before.Id, after.Id);
        Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Equal(RunOutputFiles.keys(before).Select(key => key.Id), RunOutputFiles.keys(after).Select(key => key.Id));
        Assert.Equal(RunOutputFiles.temporarySnapshotPath(folder, before), RunOutputFiles.temporarySnapshotPath(folder, after));
        Assert.ThrowsAny<Exception>(() => SgdRun.validate(new(5, 2, FSharpOption<int>.Some(0))));
    }
}
