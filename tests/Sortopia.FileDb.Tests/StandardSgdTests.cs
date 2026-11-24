using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Evaluation;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    private static parameterBinding[] SgdParameters => [
        Bind("SorterPoolCount", "1"), Bind("SorterCountPerPool", "2"),
        Bind("InitialPoolSeed", "0"), Bind("InitialPoolSelection", "Random", parameterValueType.Text),
        Bind("SelectedSorterCountPerPool", "2"), Bind("SorterChildCount", "1"), Bind("MutationMod", "0"),
        Bind("AdjustCeLengthFrequency", "2"), Bind("SortedFraction", "0.99", parameterValueType.Floating),
        Bind("PrioritizeNewMutants", "true", parameterValueType.Boolean), Bind("DistinctSorterHashes", "true", parameterValueType.Boolean),
        Bind("SorterEvalMeasure", SorterEvalMeasure.toCompactString(SorterEvalMeasure.stageBiasedFilterUnsorted), parameterValueType.Text),
        Bind("RngType", "LCG", parameterValueType.Text), Bind("MutatorVariantKey", "V2", parameterValueType.Text),
        Bind("SeedModificationRate", "0.020", parameterValueType.Floating), Bind("ModificationRate", "0.035", parameterValueType.Floating),
        Bind("OrthoRate", "4.001", parameterValueType.Floating), Bind("ParaRate", "1.15", parameterValueType.Floating),
        Bind("SelfSymRate", "1.75", parameterValueType.Floating) ];

    private (WorkspaceDocument World, ProjectDocument Consumer) SgdWorkspace(bool wrongFamily = false, parameterSpanDto[]? resultsSpans = null, sgdReportingConfig? reporting = null, sgdRunConfig? sgd = null)
    {
        var prefix = PrefixLibId.toString(PrefixLib.ids().First(id => id.ChannelCount == 8 && id.StageLength == 1));
        var prefixBinding = Bind("PrefixLibId", prefix, parameterValueType.PrefixLibId);
        var modelsType = sortopiaDataType.NewSorterModelSet("");
        var testsType = sortopiaDataType.NewSortableTests("");
        var sourceName = wrongFamily ? ResultsFunctionNames.prefixMssiSimpleSorterModels : ResultsFunctionNames.prefixMsuf4SimpleSorterModels;
        parameterBinding[] generatorParameters = [prefixBinding, Bind("SorterCount", "4"), Bind("SorterSuccessFraction", "50%", parameterValueType.SorterSuccessFraction),
            Bind("Repl", "0"), Bind("RngType", "LCG", parameterValueType.Text)];
        if (!wrongFamily) generatorParameters = [..generatorParameters,
            Bind("OrthoGenRate", "1", parameterValueType.Floating), Bind("ParaGenRate", "1", parameterValueType.Floating), Bind("SelfSymGenRate", "1", parameterValueType.Floating)];
        var models = Project("SorterModelSets", new projectConfiguration("Source models", new resultsConfig("Models", sourceName, ResultsFunctionCatalog.require(sourceName).Version,
                ListModule.Singleton(new resultsDataTypeConfig(modelsType, ListModule.Singleton("PrefixLibId"))), ListModule.OfArray(generatorParameters)), null));
        var tests = Project("SortableTests", new projectConfiguration("Source tests", new resultsConfig("Tests", ResultsFunctionNames.prefixSortableTests, "1",
                ListModule.Singleton(new resultsDataTypeConfig(testsType, ListModule.Singleton("PrefixLibId"))),
                ListModule.OfArray(new[] {prefixBinding, Bind("SortableDataFormat", "Bools", parameterValueType.Text)})), null));
        var setup = new setupConfig("Setup", SetupFunctionNames.evaluationSource, SetupFunctionCatalog.require(SetupFunctionNames.evaluationSource).Version,
            ListModule.OfArray(new[] {
                new setupDataTypeConfig(modelsType, models.Definition.id, ListModule.Singleton("PrefixLibId")),
                new setupDataTypeConfig(testsType, tests.Definition.id, ListModule.Singleton("PrefixLibId")) }), ListModule.Singleton(prefixBinding));
        var definition = ResultsFunctionCatalog.require(ResultsFunctionNames.standardMsuf4SimpleSgd);
        var queryKeys = (resultsSpans?.Select(span => span.keyName) ?? []).Distinct();
        var outputs = definition.SortopiaDataTypes.Select(type => new resultsDataTypeConfig(type, ListModule.OfSeq(queryKeys)));
        var consumer = Project("SorterSgd", new projectConfiguration("Standard SGD", new resultsConfig("Results", ResultsFunctionNames.standardMsuf4SimpleSgd, definition.Version, ListModule.OfSeq(outputs), ListModule.OfArray(SgdParameters)), FSharpOption<setupConfig>.Some(setup)), resultsSpans);
        var inputs = RunDto.fromDomain(consumer.ResolvedRuns()[0]);
        inputs.reporting = reporting is null ? null : FSharpOption<sgdReportingConfig>.Some(reporting);
        inputs.sgd = FSharpOption<sgdRunConfig>.Some(sgd ?? new(3, 1));
        consumer = WorkspaceFixtures.WithRuns(consumer, [RunDto.toDomain(consumer.Snapshot(), inputs)]);
        ModuleDocument Module(ProjectDocument p) => WorkspaceFixtures.InheritedModule(Guid.NewGuid(), p.Definition.projectName,
            ModuleDefaults.configuration(p.Configuration().HasSetup), ModuleOverridesDto.toDomain(p.Configuration().HasSetup ? hierarchyKind.Branch : hierarchyKind.Root,
                new moduleOverridesDto { resultsFunctionName = p.Configuration().ResultsFunctionName, setupFunctionName = p.Configuration().HasSetup ? SetupFunctionNames.evaluationSource : null }), [], [p]);
        var world = WorkspaceFixtures.Workspace(Guid.NewGuid(), "Workspace", "Workspace", [Module(models), Module(tests), Module(consumer)]);
        Directory.CreateDirectory(Root);
        WorkspaceFileStore.CreateDocument(Root, world, Token);
        world = WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument();
        foreach (var source in new[] {models, tests})
            Completed(FileRunExecution.executeInScope(WorkspaceRunDocument.resolveByRunId(world, source.Runs[0].id), Root, null!, Token));
        return (world, new WorkspaceDocumentIndex(world).TryFindRun(consumer.Runs[0].id).Value.Parent.Document);
    }

    [Fact]
    public void Standard_SGD_copies_two_sources_and_saves_checkpoints_with_models_and_evaluations()
    {
        var (world, consumer) = SgdWorkspace();
        var sourceFiles = Directory.GetFiles(Root, "*.msgpack", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var prepared = Prepare(world, consumer); Completed(prepared.Execution);
        Assert.Single(prepared.WorkItemIds);
        Assert.Equal(2, Assert.Single(prepared.Execution.Setup).StoredOutputs.Length);
        foreach (var file in sourceFiles) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        var folder = Isolate(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder));
        var parameters = Path.Combine(folder, "runParameters");
        var report = RunParameterExecution.executeFolder(parameters, null!, null!, Token); Completed(report);
        var outputs = Assert.Single(report.Results).StoredOutputs;
        var item = Assert.Single(RunParameterFiles.listWorkItems(parameters, Token));
        var finalId = RunOutputFiles.samples(item).Snapshots.Last().Item2.Id;
        var poolsOutput = Assert.Single(outputs, output => output.Reference.Id == finalId);
        Assert.True(poolsOutput.Reference.DataType.IsSorterPoolSet);
        Assert.Equal(RunOutputFiles.keys(item).Length, DataFiles(folder, "results").Length);
        Assert.False(Directory.Exists(Path.Combine(folder, "data", "results", "SorterModelSet")));
        Assert.False(Directory.Exists(Path.Combine(folder, "data", "results", "SorterSetEval")));
        var store = new SortopiaFileDb(folder, consumer.Definition.id);
        var pools = Assert.IsType<sortopiaStorageData.SorterPoolSet>(((ISortopiaDb)store).Load(poolsOutput.Reference, Token).ResultValue.Value).Item;
        Assert.Equal(3, pools.Generation);
        Assert.Single(pools.Pools);
        Assert.InRange(pools.Pools[0].Members.Length, 1, 2);
        Assert.All(pools.Pools[0].Members, member => { Assert.NotNull(member.Model); Assert.NotNull(member.Evaluation); });
        var poolsBytes = File.ReadAllBytes(store.GetPath(poolsOutput.Reference));
        var retry = RunParameterExecution.executeFolder(parameters, null!, null!, Token); Completed(retry);
        var retried = Assert.Single(retry.Results).StoredOutputs;
        Assert.All(retried, output => Assert.True(output.Reused));
        Assert.Equal(poolsBytes, File.ReadAllBytes(store.GetPath(poolsOutput.Reference)));
        var status = Assert.Single(RunParameterExecution.inspectFolder(parameters, null!, Token)).Outputs;
        Assert.Equal(outputState.Complete, status.State); Assert.Equal(RunOutputFiles.keys(item).Length, status.ExpectedCount);
        File.Delete(store.GetPath(poolsOutput.Reference));
        Assert.Equal(outputState.Partial, Assert.Single(RunParameterExecution.inspectFolder(parameters, null!, Token)).Outputs.State);
        var regenerated = RunParameterExecution.executeFolder(parameters, null!, null!, Token); Completed(regenerated);
        Assert.False(Assert.Single(Assert.Single(regenerated.Results).StoredOutputs, output => output.Reference.Id == finalId).Reused);
        Assert.Equal(RunOutputFiles.keys(item).Length, DataFiles(folder, "results").Length);
        Assert.Equal(poolsBytes, File.ReadAllBytes(store.GetPath(poolsOutput.Reference)));
    }

    [Fact]
    public void Initial_pool_seed_sweeps_keep_shared_samples_and_distinct_results_and_report_columns()
    {
        var (world, consumer) = SgdWorkspace(resultsSpans: [
            new() { keyName = "InitialPoolSeed", values = ["0", "1"] },
            new() { keyName = "MutationMod", values = ["0", "15"] }], reporting: new("Constant(100)", "Constant(1)"), sgd: new(100, 0));
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder));
        var parameters = Path.Combine(folder, "runParameters");
        var items = RunParameterFiles.listWorkItems(parameters, Token);
        Assert.Equal(4, items.Length);
        Assert.All(items, item => Assert.Equal(SgdFunctions.standardMsuf4Simple.Version, item.Results.FunctionVersion));
        var executed = RunParameterExecution.executeFolder(parameters, null!, null!, Token); Completed(executed);
        var references = executed.Results.SelectMany(result => result.StoredOutputs).Select(output => output.Reference).ToArray();
        Assert.Equal(references.Length, references.Distinct().Count());
        var samples = items.Select(item => new {
            Seed = item.Parameters.TryFindText("InitialPoolSeed").Value,
            Ids = RunOutputFiles.loadSnapshot(folder, item, 0, Token).Pools[0].Members.Select(member => SorterModel.getId(member.Model)).ToArray()
        }).ToArray();
        foreach (var group in samples.GroupBy(sample => sample.Seed))
        {
            Assert.Equal(group.First().Ids, group.Last().Ids);
            Assert.Equal(2, group.First().Ids.Distinct().Count());
        }
        Assert.False(samples.First(sample => sample.Seed == "0").Ids.SequenceEqual(samples.First(sample => sample.Seed == "1").Ids));
        var report = SorterEvalReportExport.exportFolder(parameters, null!, Path.Combine(temporary.FullName, "Pool seed report"), Token);
        var rows = TypedReportTests.ReadCsv(report.SummaryPath);
        Assert.Equal(4, rows.Length);
        Assert.Equal(4, rows.Select(row => (row["Results.InitialPoolSeed"], row["Results.MutationMod"])).Distinct().Count());
    }

    [Fact]
    public void Best_initial_pools_are_shared_across_mutation_and_seed_settings_and_reported()
    {
        var (world, consumer) = SgdWorkspace(resultsSpans: [
            new() { keyName = "InitialPoolSelection", values = ["Best"] },
            new() { keyName = "InitialPoolSeed", values = ["0", "99"] },
            new() { keyName = "MutationMod", values = ["0", "3"] }], reporting: new("Constant(100)", "Constant(1)"), sgd: new(100, 0));
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder));
        var parameters = Path.Combine(folder, "runParameters");
        var items = RunParameterFiles.listWorkItems(parameters, Token);
        Completed(RunParameterExecution.executeFolderWithOptions(parameters, null!, new(2), null!, Token));
        var pools = items.Select(item => RunOutputFiles.loadSnapshot(folder, item, 0, Token).Pools[0]).ToArray();
        var ids = pools[0].Members.Select(member => SorterModel.getId(member.Model)).ToArray();
        Assert.Equal(2, ids.Distinct().Count());
        Assert.All(pools, pool => {
            Assert.Equal(pools[0].Id, pool.Id);
            Assert.Equal(ids, pool.Members.Select(member => SorterModel.getId(member.Model)));
            Assert.All(pool.Members, member => Assert.True(SorterEval.getIsSorted(member.Evaluation)));
        });
        var report = SorterEvalReportExport.exportFolder(parameters, null!, Path.Combine(temporary.FullName, "Best pool report"), Token);
        Assert.All(TypedReportTests.ReadCsv(report.SummaryPath), row => Assert.Equal("Best", row["Results.InitialPoolSelection"]));
    }

    [Fact]
    public void Standard_SGD_rejects_other_algorithms_before_saving_any_results()
    {
        var (world, consumer) = SgdWorkspace(wrongFamily: true);
        Completed(Prepare(world, consumer).Execution);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        var report = RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token);
        Assert.Equal(runExecutionStatus.Failed, report.Status);
        Assert.Contains("Simple MSUF4", report.Error);
        Assert.Empty(DataFiles(folder, "results"));
    }

    [Theory]
    [InlineData("InitialPoolSeed", "-1")]
    [InlineData("InitialPoolSelection", "Invalid")]
    [InlineData("SorterCountPerPool", "0")]
    [InlineData("SelectedSorterCountPerPool", "3")]
    [InlineData("AdjustCeLengthFrequency", "0")]
    [InlineData("SortedFraction", "0")]
    [InlineData("SeedModificationRate", "1.01")]
    [InlineData("MutatorVariantKey", "V3")]
    public void Standard_SGD_validates_parameter_values_before_planning_outputs(string key, string text)
    {
        var defaults = SgdParameters.Select(value => value.KeyName == key ? Bind(key, text, value.ValueType) : value);
        var error = SgdFunctions.standardMsuf4Simple.InputError.Invoke(ListModule.OfSeq(defaults)).Invoke(ListModule.Empty<parameterSpan>());
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
