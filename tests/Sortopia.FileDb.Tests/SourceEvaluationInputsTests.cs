using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting.Inputs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    private (workspace World, project ModelsSource, project TestsSource, project Consumer,
        runExecutionReport ModelsData, runExecutionReport TestsData) EvaluationCopyWorkspace(int channels)
    {
        var modelsType = sortopiaDataType.NewSorterModelSet("");
        var testsType = sortopiaDataType.NewSortableTests("");
        var modelConfig = new resultsConfig("Model archive", ResultsFunctionNames.uniformMssiSorterModels, "1",
            ListModule.Singleton(new resultsDataTypeConfig(modelsType, ListModule.Singleton("ChannelCount"))), ListModule.OfArray(Models));
        var modelsSource = Project("Models", projectConfiguration.NewRootProjectConfig(new("Models by width", modelConfig)),
            [new() { keyName = "ChannelCount", values = ["4", "8"] }]);
        var testsConfig = new resultsConfig("Tests archive", ResultsFunctionNames.standardSortableTests, "1",
            ListModule.Singleton(new resultsDataTypeConfig(testsType, ListModule.Singleton("ChannelCount"))),
            ListModule.Singleton(Bind("SortableDataFormat", "Bools", parameterValueType.Text)));
        var testsSource = Project("Tests", projectConfiguration.NewRootProjectConfig(new("Tests by width", testsConfig)),
            [new() { keyName = "ChannelCount", values = ["4", "8"] }]);
        var setup = new setupConfig("Local inputs", SetupFunctionNames.evaluationSource, "1",
            ListModule.OfArray(new[] {
                new setupDataTypeConfig(modelsType, modelsSource.Id, ListModule.Singleton("ChannelCount")),
                new setupDataTypeConfig(testsType, testsSource.Id, ListModule.Singleton("ChannelCount")) }),
            ListModule.Singleton(Bind("ChannelCount", channels.ToString())));
        var consumer = Project("Consumer", projectConfiguration.NewBranchProjectConfig(new("Evaluate copied inputs",
            Results(ResultsFunctionNames.sorterEval, [Bind("ChannelCount", channels.ToString()),
                Bind("SorterEvalType", "V1", parameterValueType.Text), Bind("CollectSortableTests", "false", parameterValueType.Boolean)]), setup)));
        workspaceModule Module(project project) => workspaceModule.createInherited(Guid.NewGuid(), project.ProjectName, project.ProjectName,
            ModuleDefaults.configuration(project.IsBranchProject), ModuleOverridesDto.toDomain(project.IsBranchProject ? hierarchyKind.Branch : hierarchyKind.Root,
                new moduleOverridesDto { resultsFunctionName = project.ResultsFunctionName, setupFunctionName = project.IsBranchProject ? SetupFunctionNames.evaluationSource : null }), [], [project]);
        var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [Module(modelsSource), Module(testsSource), Module(consumer)]);
        Directory.CreateDirectory(Root);
        WorkspaceFileStore.CreateWorkspace(Root, world, Token);
        world = WorkspaceFileStore.LoadDefinition(Root);
        runExecutionReport Produce(project source)
        {
            var report = FileRunExecution.execute(world, Root, world.ProjectIndex[source.Id].Runs[0], null!, Token);
            Completed(report); return report;
        }
        return (world, world.ProjectIndex[modelsSource.Id], world.ProjectIndex[testsSource.Id], world.ProjectIndex[consumer.Id],
            Produce(modelsSource), Produce(testsSource));
    }

    private static storedDataReference SourceReference(runExecutionReport report, string width) =>
        Assert.Single(report.Results.Single(row => row.Parameters.TryFindText("ChannelCount").Value == width).StoredOutputs).Reference;

    [Fact]
    public void Copied_models_and_tests_use_separate_projects_reuse_setup_and_execute_without_sources()
    {
        var (world, modelsSource, testsSource, consumer, modelsData, testsData) = EvaluationCopyWorkspace(8);
        var sources = new[] { SourceReference(modelsData, "8"), SourceReference(testsData, "8") };
        Assert.Equal(modelsSource.Id, sources[0].ProjectId); Assert.Equal(testsSource.Id, sources[1].ProjectId);
        var originals = sources.Select(reference => {
            var store = new SortopiaFileDb(WorkspaceData.projectFolder(world, Root, reference.ProjectId), reference.ProjectId);
            return (Reference: reference, Path: store.GetPath(reference), Bytes: File.ReadAllBytes(store.GetPath(reference)),
                Value: ((ISortopiaDb)store).Load(reference, Token).ResultValue.Value);
        }).ToArray();
        var prepared = Prepare(world, consumer); Completed(prepared.Execution);
        Assert.Single(prepared.WorkItemIds);
        var copied = Assert.Single(prepared.Execution.Setup).StoredOutputs;
        Assert.Equal(2, copied.Length);
        var folder = WorkspaceData.projectFolder(world, Root, consumer.Id);
        ISortopiaDb local = new SortopiaFileDb(folder, consumer.Id);
        foreach (var output in copied)
        {
            Assert.False(output.Reused); Assert.True(output.Reference.IsSetup); Assert.Equal(consumer.Id, output.Reference.ProjectId);
            var original = originals.Single(source => source.Reference.DataType.Equals(output.Reference.DataType));
            Assert.Equal(original.Value, local.Load(output.Reference, Token).ResultValue.Value);
            Assert.Equal(original.Bytes, File.ReadAllBytes(original.Path));
        }
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "data", "setup", "SorterModelSet"), "*.msgpack"));
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "data", "setup", "SortableTests"), "*.msgpack"));
        Assert.Empty(DataFiles(folder, "results"));
        var repeated = Prepare(world, consumer); Completed(repeated.Execution);
        Assert.Equal(prepared.WorkItemIds, repeated.WorkItemIds);
        Assert.All(Assert.Single(repeated.Execution.Setup).StoredOutputs, output => Assert.True(output.Reused));
        // Only this project's files remain; results execution cannot consult either source.
        var portableFolder = Isolate(folder);
        var report = RunParameterExecution.executeFolder(Path.Combine(portableFolder, "runParameters"), null!, null!, Token);
        Completed(report);
        var result = Assert.Single(Assert.Single(report.Results).StoredOutputs);
        ISortopiaDb portable = new SortopiaFileDb(portableFolder, consumer.Id);
        var evaluation = Assert.IsType<functionData.SorterSetEval>(portable.Load(result.Reference, Token).ResultValue.Value).Item;
        Assert.Equal(3, evaluation.SorterEvals.Length);
        var originalTests = Assert.IsType<functionData.SortableTests>(originals.Single(source => source.Value.IsSortableTests).Value);
        Assert.Equal(8, SortableTests.getChannelCount(originalTests.Item1));
        Assert.Equal(SortableTests.getId(originalTests.Item1), evaluation.SorterTestId);
        var retry = RunParameterExecution.executeFolder(Path.Combine(portableFolder, "runParameters"), null!, null!, Token);
        Completed(retry); Assert.True(Assert.Single(Assert.Single(retry.Results).StoredOutputs).Reused);
    }

    [Fact]
    public void Missing_source_channel_count_fails_without_copying_another_set()
    {
        var (world, _, _, consumer, _, _) = EvaluationCopyWorkspace(16);
        var prepared = Prepare(world, consumer);
        Assert.Equal(runExecutionStatus.Failed, prepared.Execution.Status);
        Assert.Contains("missing", prepared.Execution.Error);
        Assert.Empty(prepared.WorkItemIds);
        Assert.Empty(DataFiles(WorkspaceData.projectFolder(world, Root, consumer.Id), "setup"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Either_missing_source_fails_before_any_setup_output_is_written(bool missingTests)
    {
        var (world, models, tests, consumer, modelsData, testsData) = EvaluationCopyWorkspace(8);
        var source = missingTests ? tests : models;
        var reference = SourceReference(missingTests ? testsData : modelsData, "8");
        var store = new SortopiaFileDb(WorkspaceData.projectFolder(world, Root, source.Id), source.Id);
        File.Delete(store.GetPath(reference));
        var prepared = Prepare(world, consumer);
        Assert.Equal(runExecutionStatus.Failed, prepared.Execution.Status);
        Assert.Contains("missing", prepared.Execution.Error);
        Assert.Contains(missingTests ? "SortableTests" : "SorterModelSet", prepared.Execution.Error);
        Assert.Empty(prepared.WorkItemIds);
        Assert.Empty(DataFiles(WorkspaceData.projectFolder(world, Root, consumer.Id), "setup"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Either_ambiguous_source_fails_before_setup_is_written(bool ambiguousTests)
    {
        var (world, models, tests, consumer, modelsData, testsData) = EvaluationCopyWorkspace(8);
        var source = ambiguousTests ? tests : models;
        var data = ambiguousTests ? testsData : modelsData;
        var selected = data.Results.Single(row => row.Parameters.TryFindText("ChannelCount").Value == "8");
        var original = Assert.Single(selected.StoredOutputs).Reference;
        ISortopiaDb store = new SortopiaFileDb(WorkspaceData.projectFolder(world, Root, source.Id), source.Id);
        var payload = store.Load(original, Token).ResultValue.Value;
        var configuration = source.Snapshot.Results;
        var another = DataKey.create(source.Id, configuration.DatabaseName, false, configuration.FunctionName, configuration.FunctionVersion,
            original.DataType, ListModule.Singleton("ChannelCount"), selected.Parameters, ListModule.Singleton(data.Results[0].StoredOutputs[0].Reference));
        Assert.True(store.GetOrCreate(another, FuncConvert.FromFunc<Unit, FSharpResult<functionData, string>>(_ => FSharpResult<functionData, string>.NewOk(payload)), Token).IsOk);
        var prepared = Prepare(world, consumer);
        Assert.Equal(runExecutionStatus.Failed, prepared.Execution.Status);
        Assert.Contains("ambiguous", prepared.Execution.Error);
        Assert.Empty(prepared.WorkItemIds);
        Assert.Empty(DataFiles(WorkspaceData.projectFolder(world, Root, consumer.Id), "setup"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_payloads_are_validated_before_any_local_copy_is_saved(bool invalidTests)
    {
        var (world, models, tests, consumer, modelsData, testsData) = EvaluationCopyWorkspace(8);
        var source = invalidTests ? tests : models;
        var data = invalidTests ? testsData : modelsData;
        var store = new SortopiaFileDb(WorkspaceData.projectFolder(world, Root, source.Id), source.Id);
        var selected = data.Results.Single(row => row.Parameters.TryFindText("ChannelCount").Value == "8");
        var reference = Assert.Single(selected.StoredOutputs).Reference;
        var wrongWidth = ((ISortopiaDb)store).Load(SourceReference(data, "4"), Token).ResultValue.Value;
        File.Delete(store.GetPath(reference));
        var configuration = source.Snapshot.Results;
        var key = DataKey.create(source.Id, configuration.DatabaseName, false, configuration.FunctionName, configuration.FunctionVersion,
            reference.DataType, ListModule.Singleton("ChannelCount"), selected.Parameters, ListModule.Empty<storedDataReference>());
        Assert.True(((ISortopiaDb)store).GetOrCreate(key, FuncConvert.FromFunc<Unit, FSharpResult<functionData, string>>(_ => FSharpResult<functionData, string>.NewOk(wrongWidth)), Token).IsOk);
        var prepared = Prepare(world, consumer);
        Assert.Equal(runExecutionStatus.Failed, prepared.Execution.Status);
        Assert.Contains("ChannelCount 8", prepared.Execution.Error);
        Assert.Empty(prepared.WorkItemIds);
        Assert.Empty(DataFiles(WorkspaceData.projectFolder(world, Root, consumer.Id), "setup"));
    }
}
