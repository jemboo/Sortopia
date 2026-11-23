using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting;
using Sortopia.Sorting.Inputs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    private (WorkspaceDocument World, ProjectDocument ModelsSource, ProjectDocument TestsSource, ProjectDocument Consumer,
        runExecutionReport ModelsData, runExecutionReport TestsData) EvaluationCopyWorkspace(int channels, bool prefix = false)
    {
        var modelsType = sortopiaDataType.NewSorterModelSet("");
        var testsType = sortopiaDataType.NewSortableTests("");
        var modelConfig = new resultsConfig("Model archive", ResultsFunctionNames.uniformMssiSimpleSorterModels,
            ResultsFunctionCatalog.require(ResultsFunctionNames.uniformMssiSimpleSorterModels).Version,
            ListModule.Singleton(new resultsDataTypeConfig(modelsType, ListModule.Singleton("ChannelCount"))), ListModule.OfArray(Models));
        var modelsSource = Project("Models", new projectConfiguration("Models by width", modelConfig, null),
            [new() { keyName = "ChannelCount", values = ["4", "8"] }]);
        var prefixes = PrefixLib.ids().Where(id => id.ChannelCount == 8).Take(2).Select(PrefixLibId.toString).ToArray();
        var testsKey = prefix ? "PrefixLibId" : "ChannelCount";
        var setupName = SetupFunctionNames.evaluationSource;
        var testsConfig = new resultsConfig("Tests archive", prefix ? ResultsFunctionNames.prefixSortableTests : ResultsFunctionNames.standardSortableTests, "1",
            ListModule.Singleton(new resultsDataTypeConfig(testsType, ListModule.Singleton(testsKey))),
            ListModule.Singleton(Bind("SortableDataFormat", "Bools", parameterValueType.Text)));
        var testsSource = Project("Tests", new projectConfiguration("Tests by width", testsConfig, null),
            [new() { keyName = testsKey, values = prefix ? prefixes : ["4", "8"] }]);
        var setup = new setupConfig("Local inputs", setupName, SetupFunctionCatalog.require(setupName).Version,
            ListModule.OfArray(new[] {
                new setupDataTypeConfig(modelsType, modelsSource.Definition.id, ListModule.Singleton("ChannelCount")),
                new setupDataTypeConfig(testsType, testsSource.Definition.id, ListModule.Singleton(testsKey)) }),
            ListModule.Singleton(Bind("ChannelCount", channels.ToString())));
        var consumer = Project("Consumer", new projectConfiguration("Evaluate copied inputs",
            new resultsConfig("Results", ResultsFunctionNames.sorterEval, ResultsFunctionCatalog.require(ResultsFunctionNames.sorterEval).Version,
                ResultsFunctionCatalog.outputs(ResultsFunctionNames.sorterEval), ListModule.OfArray(new[] {
                Bind("SorterEvalType", "V1", parameterValueType.Text), Bind("CollectSortableTests", "false", parameterValueType.Boolean) })), FSharpOption<setupConfig>.Some(setup)),
            setup: prefix ? [new() { keyName = "PrefixLibId", values = prefixes }] : null);
        ModuleDocument Module(ProjectDocument project) => WorkspaceFixtures.InheritedModule(Guid.NewGuid(), project.Definition.projectName,
            ModuleDefaults.configuration(project.Configuration().HasSetup), ModuleOverridesDto.toDomain(project.Configuration().HasSetup ? hierarchyKind.Branch : hierarchyKind.Root,
                new moduleOverridesDto { resultsFunctionName = project.Configuration().ResultsFunctionName, setupFunctionName = project.Configuration().HasSetup ? setupName : null }), [], [project]);
        var world = WorkspaceFixtures.Workspace(Guid.NewGuid(), "Workspace", "Workspace", [Module(modelsSource), Module(testsSource), Module(consumer)]);
        Directory.CreateDirectory(Root);
        WorkspaceFileStore.CreateDocument(Root, world, Token);
        world = WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument();
        runExecutionReport Produce(ProjectDocument source)
        {
            var report = FileRunExecution.executeInScope(WorkspaceRunDocument.resolveByRunId(world, source.Runs[0].id), Root, null!, Token);
            Completed(report); return report;
        }
        return (world, new WorkspaceDocumentIndex(world).TryFindRun(modelsSource.Runs[0].id).Value.Parent.Document, new WorkspaceDocumentIndex(world).TryFindRun(testsSource.Runs[0].id).Value.Parent.Document, new WorkspaceDocumentIndex(world).TryFindRun(consumer.Runs[0].id).Value.Parent.Document,
            Produce(modelsSource), Produce(testsSource));
    }

    [Fact]
    public void One_database_keeps_inputs_read_only_and_writes_only_the_current_phase()
    {
        var (world, _, _, consumer, _, _) = EvaluationCopyWorkspace(4);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var value = consumer.ResolvedRuns()[0];
        var branch = value;
        var sets = RunParameterSets.create(value);
        var factory = new FileRunHost(WorkspaceRunDocument.resolveByRunId(world, (value).Id), Root, cancellation.Token);
        var setup = factory.ForSetup(sets.Setup.Value[0]);
        var testsType = sortopiaDataType.NewSortableTests("");
        var resultType = sortopiaDataType.NewSorterSetEval("");
        Assert.Empty(setup.Outputs.Stored);
        var source = setup.Database.Find(branch.SourceProjectIds[testsType], testsType, setup.Parameters);
        var copied = setup.Database.Find(setup.Outputs.Output(testsType));
        Assert.Equal(runExecutionPhase.Results, source.Phase);
        Assert.Equal(runExecutionPhase.Setup, copied.Phase);
        Assert.Throws<ArgumentException>(() => setup.Database.Find(testsType));
        SetupFunctionCatalog.require(branch.SetupFunctionName.Value).Execute.Invoke(setup).Invoke(branch.SourceProjectIds);
        Assert.Throws<ArgumentException>(() => setup.Database.Find(testsType));
        Assert.NotEqual(source, copied);
        Assert.Equal(setup.Database.Load(source), setup.Database.Load(copied));
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        var store = new SortopiaFileDb(folder, consumer.Definition.id);
        var originals = setup.Outputs.Stored.ToDictionary(output => store.GetPath(output.Reference), output => File.ReadAllBytes(store.GetPath(output.Reference)));
        Assert.All(originals.Keys, path => Assert.StartsWith(Path.Combine(folder, "data", "setup") + Path.DirectorySeparatorChar, path));

        // Extra setup outputs must not become readable unless the results function consumes them.
        var unrelated = new storedDataReference(consumer.Definition.id, Guid.NewGuid(), Guid.NewGuid(), runExecutionPhase.Setup, sortopiaDataType.NewSortableTests("unused"));
        var results = factory.ForResults(sets.Results[0], ListModule.OfSeq(setup.Outputs.Stored.Append(new storedOutput(unrelated, false))));
        Assert.All(setup.Outputs.Stored, output => Assert.Equal(output.Reference, results.Database.Find(output.Reference.DataType)));
        Assert.Empty(results.Outputs.Stored);
        Assert.Equal(copied, results.Database.Find(testsType));
        Assert.Throws<ArgumentException>(() => results.Database.Load(unrelated));
        Assert.Throws<ArgumentException>(() => results.Database.Find(unrelated.DataType));
        Assert.Throws<ArgumentException>(() => results.Database.Load(source));
        var input = results.Database.Find(testsType);
        Assert.Throws<ArgumentException>(() => results.Database.Load(new storedDataReference(input.ProjectId, input.Id, input.QueryId, runExecutionPhase.Results, input.DataType)));
        Assert.Throws<InvalidOperationException>(() => results.Database.Find(input.ProjectId, input.DataType, results.Parameters));
        Assert.Throws<ArgumentException>(() => results.Database.Save(setup.Outputs.Output(testsType), setup.Database.Load(copied)));
        Assert.Throws<ArgumentException>(() => results.Database.Save(setup.Outputs.Output(testsType),
            FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => throw new Exception("An input must not be written."))));
        ResultsFunctionCatalog.require(value.ResultsFunctionName).Execute.Invoke(results);
        var saved = Assert.Single(results.Outputs.Stored);
        Assert.Equal(saved.Reference, results.Database.Find(resultType));
        Assert.Equal(input, results.Database.Find(testsType));
        Assert.Equal(runExecutionPhase.Results, saved.Reference.Phase);
        Assert.Equal(Path.Combine(folder, "data", "results", "SorterSetEval", saved.Reference.Id + ".msgpack"), store.GetPath(saved.Reference));
        Assert.Throws<ArgumentException>(() => setup.Database.Load(saved.Reference));
        Assert.Throws<ArgumentException>(() => setup.Database.Save(results.Outputs.Output(resultType), results.Database.Load(saved.Reference)));
        foreach (var (path, bytes) in originals) Assert.Equal(bytes, File.ReadAllBytes(path));

        var savedValue = results.Database.Load(saved.Reference);
        var savedOutput = results.Outputs.Output(resultType);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => results.Database.Save(savedOutput, savedValue));
        Assert.Throws<OperationCanceledException>(() => results.Database.Save(savedOutput,
            FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => throw new Exception("Cancelled saves must not generate output."))));
        Assert.Throws<OperationCanceledException>(() => results.Database.Find(testsType));
        Assert.Throws<OperationCanceledException>(() => results.Database.Find(results.Outputs.Output(resultType)));
        Assert.Throws<OperationCanceledException>(() => results.Database.Load(input));
        Assert.Throws<OperationCanceledException>(() => results.Database.Load(saved.Reference));
    }

    [Fact]
    public void Setup_database_resolves_source_queries_without_changing_its_planned_outputs()
    {
        var (world, _, tests, consumer, _, testsData) = EvaluationCopyWorkspace(4);
        var parameters = RunParameterSets.create(consumer.ResolvedRuns()[0]).Setup.Value[0];
        var host = new FileRunHost(WorkspaceRunDocument.resolveByRunId(world, (consumer.ResolvedRuns()[0]).Id), Root, Token).ForSetup(parameters);
        var database = host.Database;
        var planned = host.Outputs.Planned;
        var first = SourceReference(testsData, "4");
        var second = SourceReference(testsData, "8");
        Assert.IsType<sortopiaStorageData.SortableTests>(database.Load(first));
        Assert.Throws<ArgumentException>(() => database.Load(second));

        var query = parameters.WithBinding(Bind("ChannelCount", "8"));
        Assert.Equal(second, database.Find(tests.Definition.id, second.DataType, query));
        Assert.Throws<ArgumentException>(() => database.Find(second.DataType));
        Assert.Equal(8, SortableTests.getChannelCount(Assert.IsType<sortopiaStorageData.SortableTests>(database.Load(second)).Item));
        Assert.IsType<sortopiaStorageData.SortableTests>(database.Load(first));
        Assert.Equal(planned, host.Outputs.Planned);
        Assert.Empty(host.Outputs.Stored);
        var output = host.Outputs.Output(second.DataType);
        Assert.Equal(first, Assert.Single(output.Dependencies));
        Assert.Throws<ArgumentException>(() => database.Save(host.Outputs.Output(second.DataType, ListModule.Singleton(second)),
            FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => throw new Exception("Changed dependencies must not generate output."))));
        Assert.Empty(DataFiles(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder), "setup"));
    }

    [Fact]
    public void Cancelled_setup_database_rejects_source_lookup_and_cached_loads()
    {
        var (world, _, tests, consumer, _, testsData) = EvaluationCopyWorkspace(4);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var parameters = RunParameterSets.create(consumer.ResolvedRuns()[0]).Setup.Value[0];
        var host = new FileRunHost(WorkspaceRunDocument.resolveByRunId(world, (consumer.ResolvedRuns()[0]).Id), Root, cancellation.Token).ForSetup(parameters);
        var database = host.Database;
        var reference = SourceReference(testsData, "4");
        Assert.IsType<sortopiaStorageData.SortableTests>(database.Load(reference));
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => database.Find(tests.Definition.id, reference.DataType, parameters));
        Assert.Throws<OperationCanceledException>(() => database.Load(reference));
        Assert.Empty(host.Outputs.Stored);
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
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        ISortopiaDb local = new SortopiaFileDb(folder, consumer.Definition.id);
        ISortopiaDb sourceModels = new SortopiaFileDb(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(modelsSource.Definition.id).RelativeFolder), modelsSource.Definition.id);
        ISortopiaDb sourceTests = new SortopiaFileDb(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(testsSource.Definition.id).RelativeFolder), testsSource.Definition.id);
        foreach (var row in prepared.Execution.Setup)
        {
            var models = row.StoredOutputs.Single(output => output.Reference.DataType.IsSorterModelSet);
            var tests = row.StoredOutputs.Single(output => output.Reference.DataType.IsSortableTests);
            var originalTests = Assert.Single(testsData.Results.Single(result => result.Parameters.TryFindText("PrefixLibId").Value == row.Parameters.TryFindText("PrefixLibId").Value).StoredOutputs).Reference;
            var modelRecord = local.Load(models.Reference, Token).ResultValue;
            var testRecord = local.Load(tests.Reference, Token).ResultValue;
            Assert.Equal(sourceModels.Load(sourceModel, Token).ResultValue.Value, modelRecord.Value);
            Assert.Equal(sourceTests.Load(originalTests, Token).ResultValue.Value, testRecord.Value);
            Assert.True(SortableTests.getSortableCount(Assert.IsType<sortopiaStorageData.SortableTests>(testRecord.Value).Item) > 0);
        }
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "data", "setup", "SorterModelSet"), "*.msgpack"));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(folder, "data", "setup", "SortableTests"), "*.msgpack").Length);
        Assert.True(prepared.Execution.Setup[1].StoredOutputs.Single(output => output.Reference.DataType.IsSorterModelSet).Reused);
        var retry = Prepare(world, consumer); Completed(retry.Execution);
        Assert.Equal(prepared.WorkItemIds, retry.WorkItemIds); Assert.All(retry.Execution.Setup.SelectMany(row => row.StoredOutputs), output => Assert.True(output.Reused));
        var expectedTestIds = testsData.Results.SelectMany(row => row.StoredOutputs).Select(output =>
            SortableTests.getId(Assert.IsType<sortopiaStorageData.SortableTests>(sourceTests.Load(output.Reference, Token).ResultValue.Value).Item)).ToArray();
        var portable = Isolate(folder);
        var report = RunParameterExecution.executeFolder(Path.Combine(portable, "runParameters"), null!, null!, Token);
        Completed(report); Assert.Equal(2, report.Results.Length);
        ISortopiaDb portableDb = new SortopiaFileDb(portable, consumer.Definition.id);
        foreach (var output in report.Results.SelectMany(row => row.StoredOutputs))
            Assert.Contains(Assert.IsType<sortopiaStorageData.SorterSetEval>(portableDb.Load(output.Reference, Token).ResultValue.Value).Item.SorterTestId, expectedTestIds);
    }

    [Fact]
    public void Copied_models_and_tests_use_separate_projects_reuse_setup_and_execute_without_sources()
    {
        var (world, modelsSource, testsSource, consumer, modelsData, testsData) = EvaluationCopyWorkspace(8);
        var sources = new[] { SourceReference(modelsData, "8"), SourceReference(testsData, "8") };
        Assert.Equal(modelsSource.Definition.id, sources[0].ProjectId); Assert.Equal(testsSource.Definition.id, sources[1].ProjectId);
        var originals = sources.Select(reference => {
            var store = new SortopiaFileDb(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(reference.ProjectId).RelativeFolder), reference.ProjectId);
            return (Reference: reference, Path: store.GetPath(reference), Bytes: File.ReadAllBytes(store.GetPath(reference)),
                Value: ((ISortopiaDb)store).Load(reference, Token).ResultValue.Value);
        }).ToArray();
        var prepared = Prepare(world, consumer); Completed(prepared.Execution);
        Assert.Single(prepared.WorkItemIds);
        var copied = Assert.Single(prepared.Execution.Setup).StoredOutputs;
        Assert.Equal(2, copied.Length);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        ISortopiaDb local = new SortopiaFileDb(folder, consumer.Definition.id);
        foreach (var output in copied)
        {
            Assert.False(output.Reused); Assert.Equal(runExecutionPhase.Setup, output.Reference.Phase); Assert.Equal(consumer.Definition.id, output.Reference.ProjectId);
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
        ISortopiaDb portable = new SortopiaFileDb(portableFolder, consumer.Definition.id);
        var evaluation = Assert.IsType<sortopiaStorageData.SorterSetEval>(portable.Load(result.Reference, Token).ResultValue.Value).Item;
        Assert.Equal(3, evaluation.SorterEvals.Length);
        var originalTests = Assert.IsType<sortopiaStorageData.SortableTests>(originals.Single(source => source.Value.IsSortableTests).Value);
        Assert.Equal(8, SortableTests.getChannelCount(originalTests.Item));
        Assert.Equal(SortableTests.getId(originalTests.Item), evaluation.SorterTestId);
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
        Assert.Empty(DataFiles(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder), "setup"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Either_missing_source_fails_before_any_setup_output_is_written(bool missingTests)
    {
        var (world, models, tests, consumer, modelsData, testsData) = EvaluationCopyWorkspace(8);
        var source = missingTests ? tests : models;
        var reference = SourceReference(missingTests ? testsData : modelsData, "8");
        var store = new SortopiaFileDb(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(source.Definition.id).RelativeFolder), source.Definition.id);
        File.Delete(store.GetPath(reference));
        var prepared = Prepare(world, consumer);
        Assert.Equal(runExecutionStatus.Failed, prepared.Execution.Status);
        Assert.Contains("missing", prepared.Execution.Error);
        Assert.Contains(missingTests ? "SortableTests" : "SorterModelSet", prepared.Execution.Error);
        Assert.Empty(prepared.WorkItemIds);
        Assert.Empty(DataFiles(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder), "setup"));
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
        ISortopiaDb store = new SortopiaFileDb(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(source.Definition.id).RelativeFolder), source.Definition.id);
        var payload = store.Load(original, Token).ResultValue.Value;
        var configuration = source.ResultsSnapshot().Results;
        var another = DataKey.create(source.Definition.id, configuration.DatabaseName, false, configuration.FunctionName, configuration.FunctionVersion,
            original.DataType, ListModule.Singleton("ChannelCount"), selected.Parameters, ListModule.Singleton(data.Results[0].StoredOutputs[0].Reference));
        Assert.True(store.GetOrCreate(another, FuncConvert.FromFunc<Unit, FSharpResult<sortopiaStorageData, string>>(_ => FSharpResult<sortopiaStorageData, string>.NewOk(payload)), Token).IsOk);
        var prepared = Prepare(world, consumer);
        Assert.Equal(runExecutionStatus.Failed, prepared.Execution.Status);
        Assert.Contains("ambiguous", prepared.Execution.Error);
        Assert.Empty(prepared.WorkItemIds);
        Assert.Empty(DataFiles(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder), "setup"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_payloads_are_validated_before_any_local_copy_is_saved(bool invalidTests)
    {
        var (world, models, tests, consumer, modelsData, testsData) = EvaluationCopyWorkspace(8);
        var source = invalidTests ? tests : models;
        var data = invalidTests ? testsData : modelsData;
        var store = new SortopiaFileDb(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(source.Definition.id).RelativeFolder), source.Definition.id);
        var selected = data.Results.Single(row => row.Parameters.TryFindText("ChannelCount").Value == "8");
        var reference = Assert.Single(selected.StoredOutputs).Reference;
        var wrongWidth = ((ISortopiaDb)store).Load(SourceReference(data, "4"), Token).ResultValue.Value;
        File.Delete(store.GetPath(reference));
        var configuration = source.ResultsSnapshot().Results;
        var key = DataKey.create(source.Definition.id, configuration.DatabaseName, false, configuration.FunctionName, configuration.FunctionVersion,
            reference.DataType, ListModule.Singleton("ChannelCount"), selected.Parameters, ListModule.Empty<storedDataReference>());
        Assert.True(((ISortopiaDb)store).GetOrCreate(key, FuncConvert.FromFunc<Unit, FSharpResult<sortopiaStorageData, string>>(_ => FSharpResult<sortopiaStorageData, string>.NewOk(wrongWidth)), Token).IsOk);
        var prepared = Prepare(world, consumer);
        Assert.Equal(runExecutionStatus.Failed, prepared.Execution.Status);
        Assert.Contains("channel", prepared.Execution.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(prepared.WorkItemIds);
        Assert.Empty(DataFiles(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder), "setup"));
    }
}
