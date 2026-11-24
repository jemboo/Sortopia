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
    [Fact]
    public void Version_six_is_rejected_by_the_catalog()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ResultsFunctionCatalog.requireVersion(ResultsFunctionNames.standardMsuf4SimpleSgd, "6"));
        Assert.Contains("requires version 6", error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prepared_SGD_rejects_version_six_and_current_files_without_snapshot_controls_before_writing_results(bool legacy)
    {
        var (world, consumer) = SgdWorkspace();
        Completed(Prepare(world, consumer).Execution);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        var item = Assert.Single(RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token));
        var config = new resultsConfig(item.Results.DatabaseName, item.Results.FunctionName,
            legacy ? "6" : item.Results.FunctionVersion, item.Results.Outputs, item.Results.ParameterDefaults);
        var parameters = legacy ? item.Parameters.WithBinding(ParameterBinding.create(ParamKeys.generationCountKey, 3)) : item.Parameters;
        var unsupported = new runParameters(item.Id, item.ProjectId, item.RunId, config, parameters,
            item.SetupInputs, item.SetNumber, item.SetupSetNumber, item.SetupSuffixStages, item.ResultsSuffixStages, null, null);
        var id = RunParameterFiles.saveWorkItem(folder, unsupported, Token);
        var report = RunParameterExecution.executeFile(Path.Combine(folder, "runParameters", id + ".msgpack"), null!, Token);
        Assert.Equal(runExecutionStatus.Failed, report.Status);
        Assert.Contains(legacy ? "requires version 6" : "SnapshotInterval and SnapshotCount", report.Error);
        Assert.Empty(DataFiles(folder, "results"));
    }

    [Fact]
    public void Resolving_a_saved_run_does_not_expose_its_snapshot_or_reporting_controls()
    {
        var (_, consumer) = SgdWorkspace(sgd: new(2, 2), reporting: new("Constant(2)", "Constant(1)"));
        var saved = WorkspaceFileStore.LoadWorkspace(Root);
        var before = saved.CreateEditDocument();
        var runId = consumer.Runs[0].id;
        var scope = saved.ResolveRun(runId);
        scope.Run.Sgd.Value.SnapshotCount = 99;
        scope.Run.Reporting.Value.SnapshotSchedule = "unfinished";
        Assert.Equal(2, saved.TryReadRunSnapshotControls(runId).Value.SnapshotCount);
        var repeated = saved.ResolveRun(runId);
        Assert.Equal(2, repeated.Run.Sgd.Value.SnapshotCount);
        Assert.Equal("Constant(2)", repeated.Run.Reporting.Value.SnapshotSchedule);
        Assert.True(WorkspaceDocumentModule.sameContent(before, saved.CreateEditDocument()));
    }

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
        var folder = Isolate(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder));
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
        var folder = Isolate(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder));
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
        var saved = WorkspaceFileStore.LoadWorkspace(Root);
        WorkspaceDocument Change(int interval, int count)
        {
            var next = MessagePackSetup.deserialize<WorkspaceDocument>(MessagePackSetup.serialize(saved.CreateEditDocument()));
            var run = next.Modules.SelectMany(m => m.Projects).Single(p => p.Definition.id == consumer.Definition.id).Runs[0];
            run.sgd = FSharpOption<sgdRunConfig>.Some(new(interval, count));
            return next;
        }
        Assert.Contains("SnapshotInterval is locked", Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.SaveDocument(Change(3, 2), saved, Token)).Message);
        Assert.Contains("only increase", Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.SaveDocument(Change(2, 1), saved, Token)).Message);
        var result = WorkspaceFileStore.SaveDocument(Change(2, 4), saved, Token);
        Assert.Equal(4, result.Workspace.CreateEditDocument().Modules.SelectMany(m => m.Projects).Single(p => p.Definition.id == consumer.Definition.id).Runs[0].sgd.Value.SnapshotCount);
        var repeatWorld = WorkspaceFileStore.LoadWorkspace(Root);
        var repeat = Prepare(repeatWorld.CreateEditDocument(), new WorkspaceDocumentIndex(repeatWorld.CreateEditDocument()).TryFindRun(consumer.Runs[0].id).Value.Parent.Document); Completed(repeat.Execution);
        Assert.Single(Directory.GetFiles(Path.Combine(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder), "runParameters"), "*.msgpack"));
    }
}
