using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting.Inputs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    private (workspace World, project ModelsSource, project TestsSource, project Consumer,
        runExecutionReport ModelsData, runExecutionReport TestsData) EvaluationCopyWorkspace(int channels, bool prefix = false)
    {
        var modelsType = sortopiaDataType.NewSorterModelSet("");
        var testsType = sortopiaDataType.NewSortableTests("");
        var modelConfig = new resultsConfig("Model archive", ResultsFunctionNames.uniformMssiSorterModels, "1",
            ListModule.Singleton(new resultsDataTypeConfig(modelsType, ListModule.Singleton("ChannelCount"))), ListModule.OfArray(Models));
        var modelsSource = Project("Models", projectConfiguration.NewRootProjectConfig(new("Models by width", modelConfig)),
            [new() { keyName = "ChannelCount", values = ["4", "8"] }]);
        var prefixes = PrefixLib.ids().Where(id => id.ChannelCount == 8).Take(2).Select(PrefixLibId.toString).ToArray();
        var testsKey = prefix ? "PrefixLibId" : "ChannelCount";
        var setupName = prefix ? SetupFunctionNames.evaluationSourcePrefix : SetupFunctionNames.evaluationSource;
        var testsConfig = new resultsConfig("Tests archive", prefix ? ResultsFunctionNames.prefixSortableTests : ResultsFunctionNames.standardSortableTests, "1",
            ListModule.Singleton(new resultsDataTypeConfig(testsType, ListModule.Singleton(testsKey))),
            ListModule.Singleton(Bind("SortableDataFormat", "Bools", parameterValueType.Text)));
        var testsSource = Project("Tests", projectConfiguration.NewRootProjectConfig(new("Tests by width", testsConfig)),
            [new() { keyName = testsKey, values = prefix ? prefixes : ["4", "8"] }]);
        var setup = new setupConfig("Local inputs", setupName, "1",
            ListModule.OfArray(new[] {
                new setupDataTypeConfig(modelsType, modelsSource.Id, ListModule.Singleton("ChannelCount")),
                new setupDataTypeConfig(testsType, testsSource.Id, ListModule.Singleton(testsKey)) }),
            ListModule.Singleton(Bind("ChannelCount", channels.ToString())));
        var consumer = Project("Consumer", projectConfiguration.NewBranchProjectConfig(new("Evaluate copied inputs",
            Results(ResultsFunctionNames.sorterEval, [Bind("ChannelCount", channels.ToString()),
                Bind("SorterEvalType", "V1", parameterValueType.Text), Bind("CollectSortableTests", "false", parameterValueType.Boolean)]), setup)),
            setup: prefix ? [new() { keyName = "PrefixLibId", values = prefixes }] : null);
        workspaceModule Module(project project) => workspaceModule.createInherited(Guid.NewGuid(), project.ProjectName, project.ProjectName,
            ModuleDefaults.configuration(project.IsBranchProject), ModuleOverridesDto.toDomain(project.IsBranchProject ? hierarchyKind.Branch : hierarchyKind.Root,
                new moduleOverridesDto { resultsFunctionName = project.ResultsFunctionName, setupFunctionName = project.IsBranchProject ? setupName : null }), [], [project]);
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
    public void Prefix_copy_finds_exact_test_projections_reuses_models_and_executes_portably()
    {
        var (world, modelsSource, testsSource, consumer, modelsData, testsData) = EvaluationCopyWorkspace(8, prefix: true);
        var sourceModel = SourceReference(modelsData, "8");
        var prepared = Prepare(world, consumer); Completed(prepared.Execution);
        Assert.Equal(2, prepared.WorkItemIds.Length); Assert.Equal(2, prepared.Execution.Setup.Length);
        var folder = WorkspaceData.projectFolder(world, Root, consumer.Id);
        ISortopiaDb local = new SortopiaFileDb(folder, consumer.Id);
        ISortopiaDb sourceModels = new SortopiaFileDb(WorkspaceData.projectFolder(world, Root, modelsSource.Id), modelsSource.Id);
        ISortopiaDb sourceTests = new SortopiaFileDb(WorkspaceData.projectFolder(world, Root, testsSource.Id), testsSource.Id);
        foreach (var row in prepared.Execution.Setup)
        {
            var models = row.StoredOutputs.Single(output => output.Reference.DataType.IsSorterModelSet);
            var tests = row.StoredOutputs.Single(output => output.Reference.DataType.IsSortableTests);
            var originalTests = Assert.Single(testsData.Results.Single(result => result.Parameters.TryFindText("PrefixLibId").Value == row.Parameters.TryFindText("PrefixLibId").Value).StoredOutputs).Reference;
            var modelRecord = local.Load(models.Reference, Token).ResultValue;
            var testRecord = local.Load(tests.Reference, Token).ResultValue;
            Assert.Equal(sourceModels.Load(sourceModel, Token).ResultValue.Value, modelRecord.Value);
            Assert.Equal(sourceTests.Load(originalTests, Token).ResultValue.Value, testRecord.Value);
            Assert.NotEmpty(Assert.IsType<functionData.SortableTests>(testRecord.Value).Item2);
        }
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "data", "setup", "SorterModelSet"), "*.msgpack"));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(folder, "data", "setup", "SortableTests"), "*.msgpack").Length);
        Assert.True(prepared.Execution.Setup[1].StoredOutputs.Single(output => output.Reference.DataType.IsSorterModelSet).Reused);
        var retry = Prepare(world, consumer); Completed(retry.Execution);
        Assert.Equal(prepared.WorkItemIds, retry.WorkItemIds); Assert.All(retry.Execution.Setup.SelectMany(row => row.StoredOutputs), output => Assert.True(output.Reused));
        var expectedTestIds = testsData.Results.SelectMany(row => row.StoredOutputs).Select(output =>
            SortableTests.getId(Assert.IsType<functionData.SortableTests>(sourceTests.Load(output.Reference, Token).ResultValue.Value).Item1)).ToArray();
        var portable = Isolate(folder);
        var report = RunParameterExecution.executeFolder(Path.Combine(portable, "runParameters"), null!, null!, Token);
        Completed(report); Assert.Equal(2, report.Results.Length);
        ISortopiaDb portableDb = new SortopiaFileDb(portable, consumer.Id);
        foreach (var output in report.Results.SelectMany(row => row.StoredOutputs))
            Assert.Contains(Assert.IsType<functionData.SorterSetEval>(portableDb.Load(output.Reference, Token).ResultValue.Value).Item.SorterTestId, expectedTestIds);
    }

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
