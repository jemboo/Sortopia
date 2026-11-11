using Microsoft.FSharp.Collections;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class PreparedExecutionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-preparation-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static parameterBinding Bind(string key, string value, parameterValueType type = parameterValueType.Integer) => ParameterBinding.fromText(key, type, value);
    private static parameterBinding[] Models => [Bind("ChannelCount", "4"), Bind("StageLength", "3"), Bind("SorterCount", "3"), Bind("Repl", "0"),
        Bind("SimpleSorterModelType", "Mssi", parameterValueType.Text), Bind("RngType", "SplitMix64", parameterValueType.Text), Bind("ExcludeSelfCe", "true", parameterValueType.Boolean)];
    private static resultsConfig Results(string name, parameterBinding[] defaults) => new("Results", name, "1", FunctionCatalogs.resultsOutputs(name), ListModule.OfArray(defaults));

    private static project Project(string name, projectConfiguration configuration, parameterSpanDto[]? results = null, parameterSpanDto[]? setup = null)
    {
        var parent = project.create(Guid.NewGuid(), name, name, configuration, []);
        parameterSpanDto[] Spans(IEnumerable<string> keys, IEnumerable<parameterBinding> defaults, parameterSpanDto[]? supplied)
        {
            var spans = (supplied ?? []).ToList();
            foreach (var key in keys.Distinct().Where(key => !spans.Any(span => span.keyName == key)))
                spans.Add(new() { keyName = key, values = [defaults.Single(binding => binding.KeyName == key).FormattedValue] });
            return spans.ToArray();
        }
        var run = RunDto.toDomain(parent, new runDto
        {
            schemaVersion = 14, id = Guid.NewGuid(), parentProjectId = parent.Id, runName = "Trial", runKind = parent.IsBranchProject ? "branch" : "root",
            resultsParameterSpans = Spans(parent.ResultsQueryParameterKeys, parent.ResultsParameterDefaults, results),
            setupParameterSpans = parent is project.BranchProject branch ? Spans(branch.Item.SetupDataTypes.SelectMany(output => output.QueryParameterKeys), branch.Item.SetupParameterDefaults, setup) : []
        });
        return project.withRuns(parent, [run]);
    }

    private (workspace World, project Project) Create(bool branch, bool invalidSecondSetup = false)
    {
        var merge = Bind("MergeLibId", MergeLibId.toString(MergeLibId.availableForChannelCount(4).First(value => value.MergeDimension == 2)), parameterValueType.MergeLibId);
        var format = Bind("SortableDataFormat", "Bools", parameterValueType.Text);
        var source = Project("Source", projectConfiguration.NewRootProjectConfig(new("Source", Results(ResultsFunctionNames.mergeSortableTests, [merge, format]))));
        var target = source;
        if (branch)
        {
            var definition = FunctionCatalogs.setupFunctionCatalog[SetupFunctionNames.evaluationMerge];
            var outputs = ListModule.OfSeq(definition.DataTypes.Select(entry => new setupDataTypeConfig(entry.Key, entry.Key.IsSortableTests ? source.Id : Guid.Empty, entry.Value)));
            var setup = new setupConfig("Setup", SetupFunctionNames.evaluationMerge, "1", outputs, ListModule.OfArray<parameterBinding>([.. Models, merge, format]));
            var results = Results(ResultsFunctionNames.sorterEval, [Bind("ChannelCount", "4"), Bind("SorterEvalType", "V1", parameterValueType.Text), Bind("CollectSortableTests", "false", parameterValueType.Boolean)]);
            target = Project("Consumer", projectConfiguration.NewBranchProjectConfig(new("Consumer", results, setup)),
                [new() { keyName = "CollectSortableTests", values = ["false", "true"] }],
                invalidSecondSetup ? [new() { keyName = "SortableDataFormat", values = ["Bools", "invalid"] }]
                    : [new() { keyName = "Repl", values = ["0", "0", "1"] }]);
        }
        workspaceModule Module(project p) => workspaceModule.createInherited(Guid.NewGuid(), p.ProjectName, p.ProjectName,
            ModuleDefaults.configuration(p.IsBranchProject), ModuleOverridesDto.toDomain(p.IsBranchProject ? hierarchyKind.Branch : hierarchyKind.Root,
                new moduleOverridesDto { resultsFunctionName = p.ResultsFunctionName, setupFunctionName = p.IsBranchProject ? SetupFunctionNames.evaluationMerge : null }), [], [p]);
        var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", branch ? [Module(source), Module(target)] : [Module(source)]);
        Directory.CreateDirectory(Root);
        WorkspaceFileStore.CreateWorkspace(Root, world, Token);
        world = WorkspaceFileStore.LoadDefinition(Root);
        if (branch) Completed(FileRunExecution.execute(world, Root, world.ProjectIndex[source.Id].Runs[0], null!, Token));
        return (world, world.ProjectIndex[target.Id]);
    }
    private static void Completed(runExecutionReport report) => Assert.True(report.Status == runExecutionStatus.Completed, report.Error);
    private static string[] DataFiles(string folder, string phase) => Directory.Exists(Path.Combine(folder, "data", phase))
        ? Directory.GetFiles(Path.Combine(folder, "data", phase), "*.msgpack", SearchOption.AllDirectories) : [];
    private runPreparationReport Prepare(workspace world, project project, IProgress<runExecutionProgress>? progress = null, CancellationToken? token = null) =>
        PreparedRunExecution.prepare(world, Root, project.Runs[0], progress!, token ?? Token);
    private string Isolate(string folder)
    {
        var copy = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Cloud project")).FullName;
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(copy, Path.GetRelativePath(folder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        // These are this test's own temporary folders. No workspace or source
        // remains available when the copied project's results execute.
        Directory.Delete(Root, true);
        return copy;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Copied_project_executes_without_workspace_or_sources_and_retries_reuse_data(bool branch)
    {
        var (world, project) = Create(branch);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var ready = Assert.IsType<preparedRun>(prepared.Preparation.Value);
        Assert.Equal(branch ? 6 : 1, ready.WorkItemIds.Length);
        Assert.Empty(DataFiles(folder, "results"));
        if (branch)
        {
            Assert.Equal(3, DataFiles(folder, "setup").Length); // two model sets, one shared tests file
            Assert.Contains(prepared.Execution.Setup.SelectMany(call => call.StoredOutputs), output => output.Reused);
        }
        var before = ready.WorkItemIds.Select(id => PreparedRunFiles.loadWorkItem(folder, id)).ToArray();
        var repeated = Prepare(world, project); Completed(repeated.Execution);
        Assert.Equal(ready.WorkItemIds, repeated.Preparation.Value.WorkItemIds);
        Assert.Equal(ready.WorkItemIds.Length, Directory.GetFiles(Path.Combine(folder, "runParameters")).Length);
        var isolated = Isolate(folder);
        var report = PreparedRunExecution.executePreparation(isolated, ready.Id, null!, Token); Completed(report);
        Assert.Empty(report.Setup);
        Assert.Equal(ready.WorkItemIds.Length, report.Results.Length);
        Assert.Equal(branch ? 4 : 1, DataFiles(isolated, "results").Length);
        for (var i = 0; i < before.Length; i++) Assert.Equal(before[i].Parameters, report.Results[i].Parameters);
        if (branch) Assert.IsType<parameterValue.BooleanValue>(before[0].Parameters.TryFind("CollectSortableTests").Value.Value);
        else Assert.IsType<parameterValue.MergeLibIdValue>(before[0].Parameters.TryFind("MergeLibId").Value.Value);
        var receipts = Directory.GetFiles(Path.Combine(isolated, "executions")).Length;
        var retry = PreparedRunExecution.executePreparation(isolated, ready.Id, null!, Token); Completed(retry);
        Assert.All(retry.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        Assert.Equal(receipts + ready.WorkItemIds.Length, Directory.GetFiles(Path.Combine(isolated, "executions")).Length);
        Assert.All(ready.WorkItemIds, id => Assert.Equal(before.Single(item => item.Id == id), PreparedRunFiles.loadWorkItem(isolated, id)));
    }

    [Fact]
    public void Failed_setup_keeps_data_but_never_publishes_a_preparation_or_results()
    {
        var (world, project) = Create(true, invalidSecondSetup: true);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var prepared = Prepare(world, project);
        Assert.Null(prepared.Preparation);
        Assert.Equal(runExecutionStatus.Failed, prepared.Execution.Status);
        Assert.Contains("Setup set 2", prepared.Execution.Error);
        Assert.Single(prepared.Execution.Setup);
        Assert.Empty(PreparedRunFiles.listPreparations(folder));
        Assert.NotEmpty(DataFiles(folder, "setup"));
        Assert.Empty(DataFiles(folder, "results"));
    }

    private sealed class Progress(Action<runExecutionProgress> callback) : IProgress<runExecutionProgress>
    { public void Report(runExecutionProgress value) => callback(value); }

    [Fact]
    public void Cancelled_preparation_leaves_orphan_work_items_unexecutable_and_can_be_retried()
    {
        var (world, project) = Create(true);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        using var cancellation = new CancellationTokenSource();
        var progress = new Progress(update => { if (update.Message == "Saving results work item 2") cancellation.Cancel(); });
        var prepared = Prepare(world, project, progress, cancellation.Token);
        Assert.Equal(runExecutionStatus.Cancelled, prepared.Execution.Status);
        Assert.Null(prepared.Preparation);
        Assert.Empty(PreparedRunFiles.listPreparations(folder));
        var orphan = Guid.Parse(Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters")))));
        Assert.Contains("No completed preparation", PreparedRunExecution.executeWorkItem(folder, orphan, null!, Token).Error);
        var retried = Prepare(world, project); Completed(retried.Execution);
        Assert.All(retried.Execution.Setup.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("parameters")]
    [InlineData("manifest")]
    [InlineData("project")]
    public void Incomplete_or_corrupt_copies_fail_before_results_are_written(string damaged)
    {
        var (world, project) = Create(true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var ready = prepared.Preparation.Value;
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, project.Id));
        var path = damaged switch
        {
            "input" => DataFiles(folder, "setup").Last(),
            "parameters" => Path.Combine(folder, "runParameters", ready.WorkItemIds.Last() + ".msgpack"),
            "manifest" => Path.Combine(folder, "preparations", ready.Id + ".msgpack"),
            _ => Path.Combine(folder, project.Id + ".msgpack")
        };
        if (damaged == "project") File.Delete(path); else File.WriteAllBytes(path, [1, 2, 3]);
        var report = PreparedRunExecution.executePreparation(folder, ready.Id, null!, Token);
        Assert.Equal(runExecutionStatus.Failed, report.Status);
        Assert.NotEmpty(report.Error);
        Assert.Empty(DataFiles(folder, "results"));
    }

    [Fact]
    public void CLI_executes_one_saved_parameter_file_then_the_full_preparation()
    {
        var (world, project) = Create(true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var ready = prepared.Preparation.Value;
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, project.Id));
        Assert.Equal(0, Sortopia.Cli.Program.main(["execute-results", "--project", folder, "--parameters", Path.Combine(folder, "runParameters", ready.WorkItemIds[0] + ".msgpack")]));
        Assert.Single(DataFiles(folder, "results"));
        Assert.Equal(0, Sortopia.Cli.Program.main(["execute-results", "--project", folder, "--preparation", ready.Id.ToString()]));
        Assert.Equal(4, DataFiles(folder, "results").Length);
        Assert.Equal(2, Sortopia.Cli.Program.main(["execute-results", "--project", folder]));
        Assert.Equal(1, Sortopia.Cli.Program.main(["execute-results", "--project", folder, "--parameters", Guid.NewGuid().ToString()]));
    }

    [Fact]
    public void Cancellation_between_results_preserves_receipts_and_retry_reuses_finished_work()
    {
        var (world, project) = Create(true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var ready = prepared.Preparation.Value;
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, project.Id));
        var before = Directory.GetFiles(Path.Combine(folder, "executions")).Length;
        using var cancellation = new CancellationTokenSource();
        var progress = new Progress(update => { if (update.Completed == 1) cancellation.Cancel(); });
        var stopped = PreparedRunExecution.executePreparation(folder, ready.Id, progress, cancellation.Token);
        Assert.Equal(runExecutionStatus.Cancelled, stopped.Status);
        Assert.Single(stopped.Results);
        Assert.Equal(before + 1, Directory.GetFiles(Path.Combine(folder, "executions")).Length);
        var resumed = PreparedRunExecution.executePreparation(folder, ready.Id, null!, Token); Completed(resumed);
        Assert.True(Assert.Single(resumed.Results[0].StoredOutputs).Reused);
        Assert.Equal(6, resumed.Results.Length);
    }

    [Fact]
    public void Standalone_host_rejects_incompatible_function_versions_without_writing_results()
    {
        var (world, project) = Create(false);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var item = PreparedRunFiles.loadWorkItem(folder, prepared.Preparation.Value.WorkItemIds[0]);
        var old = new resultsConfig(item.Results.DatabaseName, item.Results.FunctionName, "old", item.Results.Outputs, item.Results.ParameterDefaults);
        var error = Assert.Throws<InvalidOperationException>(() => FileResultsHost.create(folder, item.ProjectId, old, item.Parameters, ListModule.Empty<storedOutput>(), Token));
        Assert.Contains("requires version old", error.Message);
        Assert.Empty(DataFiles(folder, "results"));
    }
    public void Dispose() => temporary.Delete(true);
}
