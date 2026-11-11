using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
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

    private (workspace World, project Project) Create(bool branch, bool invalidSecondSetup = false, bool multipleOutputs = false)
    {
        var merge = Bind("MergeLibId", MergeLibId.toString(MergeLibId.availableForChannelCount(4).First(value => value.MergeDimension == 2)), parameterValueType.MergeLibId);
        var format = Bind("SortableDataFormat", "Bools", parameterValueType.Text);
        var source = Project("Source", projectConfiguration.NewRootProjectConfig(new("Source", multipleOutputs
            ? Results(ResultsFunctionNames.standardEvaluationInputs, [.. Models, format])
            : Results(ResultsFunctionNames.mergeSortableTests, [merge, format]))));
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
    private runParameterGenerationReport Prepare(workspace world, project project, IProgress<runExecutionProgress>? progress = null, CancellationToken? token = null) =>
        RunParameterExecution.generate(world, Root, project.Runs[0], progress!, token ?? Token);
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
        var ready = prepared;
        Assert.Equal(branch ? 6 : 1, ready.WorkItemIds.Length);
        Assert.Empty(DataFiles(folder, "results"));
        if (branch)
        {
            Assert.Equal(3, DataFiles(folder, "setup").Length); // two model sets, one shared tests file
            Assert.Contains(prepared.Execution.Setup.SelectMany(call => call.StoredOutputs), output => output.Reused);
        }
        var before = ready.WorkItemIds.Select(id => RunParameterFiles.loadWorkItem(folder, id)).ToArray();
        var repeated = Prepare(world, project); Completed(repeated.Execution);
        Assert.Equal(ready.WorkItemIds, repeated.WorkItemIds);
        Assert.False(Directory.Exists(Path.Combine(folder, "preparations")));
        Assert.Equal(ready.WorkItemIds.Length, Directory.GetFiles(Path.Combine(folder, "runParameters")).Length);
        var isolated = Isolate(folder);
        var report = RunParameterExecution.executeFolder(Path.Combine(isolated, "runParameters"), null!, null!, Token); Completed(report);
        Assert.Empty(report.Setup);
        Assert.Equal(ready.WorkItemIds.Length, report.Results.Length);
        Assert.Equal(branch ? 4 : 1, DataFiles(isolated, "results").Length);
        for (var i = 0; i < before.Length; i++) Assert.Equal(before[i].Parameters, report.Results[i].Parameters);
        if (branch) Assert.IsType<parameterValue.BooleanValue>(before[0].Parameters.TryFind("CollectSortableTests").Value.Value);
        else Assert.IsType<parameterValue.MergeLibIdValue>(before[0].Parameters.TryFind("MergeLibId").Value.Value);
        var files = Directory.GetFiles(isolated, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var retry = RunParameterExecution.executeFolder(Path.Combine(isolated, "runParameters"), null!, null!, Token); Completed(retry);
        Assert.All(retry.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        Assert.False(Directory.Exists(Path.Combine(isolated, "executions")));
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(isolated, "*", SearchOption.AllDirectories).Order());
        foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.All(RunParameterExecution.inspectFolder(Path.Combine(isolated, "runParameters"), null!, Token), status => Assert.Equal(outputState.Complete, status.Outputs.State));
        Assert.All(ready.WorkItemIds, id => Assert.Equal(before.Single(item => item.Id == id), RunParameterFiles.loadWorkItem(isolated, id)));
    }

    [Fact]
    public void Failed_setup_keeps_data_but_never_publishes_a_preparation_or_results()
    {
        var (world, project) = Create(true, invalidSecondSetup: true);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var prepared = Prepare(world, project);
        Assert.Empty(prepared.WorkItemIds);
        Assert.Equal(runExecutionStatus.Failed, prepared.Execution.Status);
        Assert.Contains("Setup set 2", prepared.Execution.Error);
        Assert.Single(prepared.Execution.Setup);
        Assert.False(Directory.Exists(Path.Combine(folder, "preparations")));
        Assert.NotEmpty(DataFiles(folder, "setup"));
        Assert.Empty(DataFiles(folder, "results"));
    }

    private sealed class Progress(Action<runExecutionProgress> callback) : IProgress<runExecutionProgress>
    { public void Report(runExecutionProgress value) => callback(value); }

    [Fact]
    public void Cancelled_generation_leaves_published_work_items_executable_and_can_be_retried()
    {
        var (world, project) = Create(true);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        using var cancellation = new CancellationTokenSource();
        var progress = new Progress(update => { if (update.Message == "Saving results work item 2") cancellation.Cancel(); });
        var prepared = Prepare(world, project, progress, cancellation.Token);
        Assert.Equal(runExecutionStatus.Cancelled, prepared.Execution.Status);
        Assert.Single(prepared.WorkItemIds);
        Assert.False(Directory.Exists(Path.Combine(folder, "preparations")));
        var orphan = Guid.Parse(Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters")))));
        Completed(RunParameterExecution.executeFile(Path.Combine(folder, "runParameters", orphan + ".msgpack"), null!, Token));
        var retried = Prepare(world, project); Completed(retried.Execution);
        Assert.All(retried.Execution.Setup.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("parameters")]
    [InlineData("project")]
    public void Incomplete_or_corrupt_copies_fail_before_results_are_written(string damaged)
    {
        var (world, project) = Create(true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var ready = prepared;
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, project.Id));
        var path = damaged switch
        {
            "input" => DataFiles(folder, "setup").Last(),
            "parameters" => Path.Combine(folder, "runParameters", ready.WorkItemIds.Last() + ".msgpack"),
            _ => Path.Combine(folder, project.Id + ".msgpack")
        };
        if (damaged == "project") File.Delete(path); else File.WriteAllBytes(path, [1, 2, 3]);
        var report = RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token);
        Assert.Equal(runExecutionStatus.Failed, report.Status);
        Assert.NotEmpty(report.Error);
        Assert.Empty(DataFiles(folder, "results"));
    }

    [Fact]
    public void CLI_executes_one_saved_parameter_file_then_the_full_preparation()
    {
        var (world, project) = Create(true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var ready = prepared;
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, project.Id));
        Assert.Equal(0, Sortopia.Cli.Program.main(["execute-results", "--parameters", Path.Combine(folder, "runParameters", ready.WorkItemIds[0] + ".msgpack")]));
        Assert.Single(DataFiles(folder, "results"));
        Assert.Equal(0, Sortopia.Cli.Program.main(["execute-results", "--parameters-folder", Path.Combine(folder, "runParameters")]));
        Assert.Equal(4, DataFiles(folder, "results").Length);
        Assert.Equal(2, Sortopia.Cli.Program.main(["execute-results", "--project", folder]));
        Assert.Equal(1, Sortopia.Cli.Program.main(["execute-results", "--parameters", Path.Combine(folder, "runParameters", Guid.NewGuid() + ".msgpack")]));
    }

    [Fact]
    public void Cancellation_between_results_preserves_outputs_and_retry_reuses_finished_work()
    {
        var (world, project) = Create(true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var ready = prepared;
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, project.Id));
        using var cancellation = new CancellationTokenSource();
        var progress = new Progress(update => { if (update.Completed == 1) cancellation.Cancel(); });
        var stopped = RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, progress, cancellation.Token);
        Assert.Equal(runExecutionStatus.Cancelled, stopped.Status);
        Assert.Single(stopped.Results);
        Assert.Single(DataFiles(folder, "results"));
        Assert.False(Directory.Exists(Path.Combine(folder, "executions")));
        var resumed = RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token); Completed(resumed);
        Assert.True(Assert.Single(resumed.Results[0].StoredOutputs).Reused);
        Assert.Equal(6, resumed.Results.Length);
    }

    [Fact]
    public void Standalone_host_rejects_incompatible_function_versions_without_writing_results()
    {
        var (world, project) = Create(false);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var item = RunParameterFiles.loadWorkItem(folder, prepared.WorkItemIds[0]);
        var old = new resultsConfig(item.Results.DatabaseName, item.Results.FunctionName, "old", item.Results.Outputs, item.Results.ParameterDefaults);
        var error = Assert.Throws<InvalidOperationException>(() => FileResultsHost.create(folder, item.ProjectId, old, item.Parameters, ListModule.Empty<storedOutput>(), Token));
        Assert.Contains("requires version old", error.Message);
        Assert.Empty(DataFiles(folder, "results"));
    }

    [Fact]
    public void Output_checks_are_read_only_and_partial_work_reuses_valid_outputs()
    {
        var (world, project) = Create(false, multipleOutputs: true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var ready = prepared;
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        var pending = Assert.Single(RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token)).Outputs;
        Assert.Equal(outputState.Pending, pending.State);
        Assert.Equal(2, pending.ExpectedCount);
        Assert.Equal(files.Order(), Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order());
        Completed(RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token));
        var data = DataFiles(folder, "results");
        var retained = File.ReadAllBytes(data[0]);
        File.Delete(data[1]); // Simulate a stopped copy containing just one output.
        var partial = Assert.Single(RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token)).Outputs;
        Assert.Equal(outputState.Partial, partial.State);
        Assert.Single(partial.Available);
        var retry = RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token); Completed(retry);
        Assert.Single(retry.Results[0].StoredOutputs, output => output.Reused);
        Assert.Single(retry.Results[0].StoredOutputs, output => !output.Reused);
        Assert.Equal(retained, File.ReadAllBytes(data[0]));
        Assert.Equal(outputState.Complete, Assert.Single(RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token)).Outputs.State);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("schema")]
    [InlineData("identity")]
    public void Damaged_outputs_are_invalid_and_never_overwritten(string damage)
    {
        var (world, project) = Create(false, multipleOutputs: true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var ready = prepared;
        Completed(RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token));
        var file = DataFiles(folder, "results")[0];
        var dto = Sortopia.Core.Serialization.MessagePackSetup.deserialize<dataEnvelopeDto>(File.ReadAllBytes(file));
        if (damage == "checksum") dto.Checksum[0] ^= 1;
        var damaged = new dataEnvelopeDto(damage == "schema" ? 999 : dto.Version,
            damage == "identity" ? Guid.NewGuid() : dto.ProjectId, dto.Id, dto.QueryId, dto.IsSetup, dto.Kind, dto.Label,
            dto.QueryBytes, dto.IdentityBytes, dto.Compression, dto.RawLength, dto.Checksum, dto.Payload);
        var bytes = Sortopia.Core.Serialization.MessagePackSetup.serialize(damaged);
        File.WriteAllBytes(file, bytes);
        var status = Assert.Single(RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token)).Outputs;
        Assert.Equal(outputState.Invalid, status.State);
        Assert.NotEmpty(status.Error);
        Assert.Single(status.Available);
        Assert.Equal(runExecutionStatus.Failed, RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token).Status);
        Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Equal(1, Sortopia.Cli.Program.main(["check-results", "--parameters-folder", Path.Combine(folder, "runParameters")]));
    }

    [Fact]
    public void Complete_work_skips_executors_even_without_setup_payloads()
    {
        var (world, project) = Create(true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, project.Id));
        var ready = prepared;
        Completed(RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token));
        foreach (var file in DataFiles(folder, "setup")) File.Delete(file);
        var messages = new List<string>();
        var retry = RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, new Progress(update => messages.Add(update.Message)), Token);
        Completed(retry);
        Assert.All(retry.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        Assert.Equal(ready.WorkItemIds.Length, messages.Count(message => message.StartsWith("Skipping complete work item")));
        Assert.Equal(0, Sortopia.Cli.Program.main(["check-results", "--parameters-folder", Path.Combine(folder, "runParameters")]));
    }

    [Fact]
    public void Different_setup_inputs_with_the_same_query_do_not_mark_each_other_complete()
    {
        var (world, project) = Create(true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var ready = prepared;
        var first = RunParameterFiles.loadWorkItem(folder, ready.WorkItemIds[0]);
        var other = RunParameterFiles.loadWorkItem(folder, ready.WorkItemIds[4]);
        var firstPlan = FileResultsHost.plan(first.ProjectId, first.Results, first.Parameters, first.SetupInputs);
        var otherPlan = FileResultsHost.plan(other.ProjectId, other.Results, other.Parameters, other.SetupInputs);
        Assert.Equal(firstPlan.Values.Single().Key.QueryId, otherPlan.Values.Single().Key.QueryId);
        Assert.NotEqual(firstPlan.Values.Single().Key.Id, otherPlan.Values.Single().Key.Id);
        Completed(RunParameterExecution.executeFile(Path.Combine(folder, "runParameters", first.Id + ".msgpack"), null!, Token));
        var statuses = RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token);
        Assert.Equal(outputState.Complete, statuses[0].Outputs.State);
        Assert.Equal(outputState.Complete, statuses[2].Outputs.State); // The repeated setup set shares exact inputs.
        Assert.Equal(outputState.Pending, statuses[4].Outputs.State);
    }

    [Fact]
    public async Task Concurrent_identical_generations_publish_identical_parameter_files()
    {
        var (world, project) = Create(false);
        var reports = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Prepare(world, project), Token)));
        Assert.All(reports, report => Completed(report.Execution));
        Assert.Single(reports.Select(report => string.Join(",", report.WorkItemIds)).Distinct());
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        Assert.Single(RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token));
        Assert.False(Directory.Exists(Path.Combine(folder, "executions")));
    }

    [Fact]
    public void Each_output_uses_only_its_declared_dependencies_and_saving_cannot_change_them()
    {
        var (world, project) = Create(true);
        var value = project.Runs[0];
        var config = ((run.BranchRun)value).Item.ProjectConfiguration.Setup;
        var definition = FunctionCatalogs.requireSetup(config.FunctionName);
        var parameters = RunParameterSets.create(value).Setup.Value[0];
        var host = new FileRunHost(world, Root, value, Token).ForSetup(parameters);
        var source = config.Outputs.Single(output => output.DataType.IsSortableTests);
        var reference = host.SourceResults.Find(source.SourceProjectId, source.DataType, parameters);
        var changed = new storedDataReference(reference.ProjectId, Guid.NewGuid(), reference.QueryId, reference.IsSetup, reference.DataType);
        var first = OutputPlan.create(project.Id, config.DatabaseName, true, config.FunctionName, config.FunctionVersion,
            config.DataTypes, definition.OutputDependencies, parameters, ListModule.Singleton(reference));
        var second = OutputPlan.create(project.Id, config.DatabaseName, true, config.FunctionName, config.FunctionVersion,
            config.DataTypes, definition.OutputDependencies, parameters, ListModule.Singleton(changed));
        var models = sortopiaDataType.NewSorterModelSet("");
        Assert.Equal(first[models].Key.Id, second[models].Key.Id);
        Assert.Equal(first[source.DataType].Key.QueryId, second[source.DataType].Key.QueryId);
        Assert.NotEqual(first[source.DataType].Key.Id, second[source.DataType].Key.Id);
        var never = FuncConvert.FromFunc<Unit, functionData>(_ => throw new Exception("Factory must not run"));
        Assert.Throws<ArgumentException>(() => host.SetupDb.SaveIfMissing(models, ListModule.Singleton(reference), never));
        Assert.Throws<ArgumentException>(() => host.SetupDb.SaveIfMissing(source.DataType, ListModule.Empty<storedDataReference>(), never));
        Assert.Empty(host.SetupDb.StoredOutputs);
    }

    [Fact]
    public void Changed_spans_preserve_existing_files_and_add_new_work()
    {
        var (world, project) = Create(false);
        var first = Prepare(world, project); Completed(first.Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var original = first;
        var dto = RunDto.fromDomain(project.Runs[0]);
        dto.resultsParameterSpans.Single(span => span.keyName == "SortableDataFormat").values = ["Bools", "Ints"];
        var changed = RunDto.toDomain(project, dto);
        var parent = Sortopia.Workspaces.project.withRuns(project, [changed]);
        // A saved definition can be reconstructed without changing the project's sealed configuration.
        var module = world.Modules[0];
        var updated = workspace.create(world.Id, world.WorkspaceName, world.FolderName,
            [workspaceModule.createInherited(module.Id, module.ModuleName, module.FolderName,
                ModuleDefaults.configuration(false), module.LocalOverrides, [], [parent])]);
        var second = Prepare(updated, parent); Completed(second.Execution);
        Assert.All(original.WorkItemIds, id => Assert.Contains(id, second.WorkItemIds));
        Assert.Equal(2, RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token).Length);
        Assert.False(Directory.Exists(Path.Combine(folder, "preparations")));
    }
    [Fact]
    public void Folder_execution_filters_by_run_and_ignores_nonparameter_and_temporary_files()
    {
        var (world, project) = Create(false);
        var first = Prepare(world, project); Completed(first.Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var dto = RunDto.fromDomain(project.Runs[0]); dto.id = Guid.NewGuid(); dto.runName = "Second";
        dto.resultsParameterSpans.Single(span => span.keyName == "SortableDataFormat").values = ["Ints"];
        var secondRun = RunDto.toDomain(project, dto);
        var snapshot = WorkspaceDto.fromDomain(world);
        snapshot.modules[0].projects[0].runs = [.. snapshot.modules[0].projects[0].runs, dto];
        var updated = WorkspaceDto.toDomain(snapshot);
        WorkspaceFileStore.SaveWorkspace(Root, updated, world, Token);
        var generated = RunParameterExecution.generate(updated, Root, updated.ProjectIndex[project.Id].Runs.Single(run => run.Id == secondRun.Id), null!, Token); Completed(generated.Execution);
        var parameters = Path.Combine(folder, "runParameters");
        File.WriteAllText(Path.Combine(parameters, "notes.txt"), "Ignore me");
        File.WriteAllBytes(Path.Combine(parameters, Guid.NewGuid() + ".msgpack.interrupted.tmp"), [1, 2]);
        Assert.Equal(2, RunParameterFiles.listWorkItems(parameters, Token).Length);
        Assert.Equal(0, Sortopia.Cli.Program.main(["execute-results", "--parameters-folder", parameters, "--run", secondRun.Id.ToString()]));
        var states = RunParameterExecution.inspectFolder(parameters, null!, Token);
        Assert.Equal(outputState.Pending, states.Single(item => item.RunId == project.Runs[0].Id).Outputs.State);
        Assert.Equal(outputState.Complete, states.Single(item => item.RunId == secondRun.Id).Outputs.State);
        Assert.Equal(0, Sortopia.Cli.Program.main(["list-parameters", "--parameters-folder", parameters]));
        Completed(RunParameterExecution.executeFolder(parameters, null!, null!, Token));
        Assert.Equal(2, DataFiles(folder, "results").Length);
        Assert.False(Directory.Exists(Path.Combine(folder, "preparations")));
    }

    [Fact]
    public void Empty_invalid_and_mixed_project_folders_are_rejected_without_results()
    {
        var (world, project) = Create(false);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var parameters = Directory.CreateDirectory(Path.Combine(folder, "runParameters")).FullName;
        Assert.Contains("No parameter files", RunParameterExecution.executeFolder(parameters, null!, null!, Token).Error);
        Completed(Prepare(world, project).Execution);
        var file = Directory.GetFiles(parameters, "*.msgpack").Single();
        var alias = Path.Combine(parameters, Guid.Parse(Path.GetFileNameWithoutExtension(file)).ToString("N") + ".msgpack");
        File.Copy(file, alias);
        Assert.Contains("canonical", RunParameterExecution.executeFolder(parameters, null!, null!, Token).Error);
        File.Delete(alias);
        // Work files use compressed envelopes, so use their normal loader to obtain a complete invocation.
        var item = RunParameterFiles.loadFile(file);
        var foreign = new runParametersDto(1, Guid.NewGuid(), item.RunId, item.Results.FunctionName, item.Results.FunctionVersion, item.Results.DatabaseName,
            [new preparedOutputDto("SortableTests", "", [])], [], [], 1, null!);
        var raw = Sortopia.Core.Serialization.MessagePackSetup.serialize(foreign);
        var id = new Guid(System.Security.Cryptography.SHA256.HashData(raw)[..16]);
        var envelope = new runParametersEnvelopeDto(1, "runParameters", System.Security.Cryptography.SHA256.HashData(raw), raw);
        File.WriteAllBytes(Path.Combine(parameters, id + ".msgpack"), MessagePack.MessagePackSerializer.Serialize(envelope,
            Sortopia.Core.Serialization.MessagePackSetup.options.WithCompression(MessagePack.MessagePackCompression.Lz4BlockArray), Token));
        Assert.Contains("project definition", RunParameterExecution.executeFolder(parameters, null!, null!, Token).Error);
        Assert.Empty(DataFiles(folder, "results"));
    }
    public void Dispose() => temporary.Delete(true);
}
