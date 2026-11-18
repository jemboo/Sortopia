using Microsoft.FSharp.Collections;
using Sortopia.Evaluation;
using Sortopia.FileDb;
using Sortopia.Functions;
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
        Bind("GenerationCount", "3"), Bind("SorterPoolCount", "1"), Bind("SorterCountPerPool", "2"),
        Bind("SelectedSorterCountPerPool", "2"), Bind("SorterChildCount", "1"), Bind("MutationMod", "0"),
        Bind("AdjustCeLengthFrequency", "2"), Bind("SortedFraction", "0.99", parameterValueType.Floating),
        Bind("PrioritizeNewMutants", "true", parameterValueType.Boolean), Bind("DistinctSorterHashes", "true", parameterValueType.Boolean),
        Bind("SorterEvalMeasure", SorterEvalMeasure.toCompactString(SorterEvalMeasure.stageBiasedFilterUnsorted), parameterValueType.Text),
        Bind("RngType", "LCG", parameterValueType.Text), Bind("MutatorVariantKey", "V2", parameterValueType.Text),
        Bind("SeedModificationRate", "0.020", parameterValueType.Floating), Bind("ModificationRate", "0.035", parameterValueType.Floating),
        Bind("OrthoRate", "4.001", parameterValueType.Floating), Bind("ParaRate", "1.15", parameterValueType.Floating),
        Bind("SelfSymRate", "1.75", parameterValueType.Floating) ];

    private (workspace World, project Consumer) SgdWorkspace(bool wrongFamily = false, parameterSpanDto[]? resultsSpans = null, sgdReportingConfig? reporting = null)
    {
        var prefix = PrefixLibId.toString(PrefixLib.ids().First(id => id.ChannelCount == 8 && id.StageLength == 1));
        var prefixBinding = Bind("PrefixLibId", prefix, parameterValueType.PrefixLibId);
        var modelsType = sortopiaDataType.NewSorterModelSet("");
        var testsType = sortopiaDataType.NewSortableTests("");
        var sourceName = wrongFamily ? ResultsFunctionNames.prefixMssiSimpleSorterModels : ResultsFunctionNames.prefixMsuf4SimpleSorterModels;
        parameterBinding[] generatorParameters = [prefixBinding, Bind("SorterCount", "4"), Bind("SorterSuccessFraction", "50%", parameterValueType.SorterSuccessFraction),
            Bind("Repl", "0"), Bind("RngType", "LCG", parameterValueType.Text)];
        if (!wrongFamily) generatorParameters = [..generatorParameters,
            Bind("OrthoRate", "1", parameterValueType.Floating), Bind("ParaRate", "1", parameterValueType.Floating), Bind("SelfSymRate", "1", parameterValueType.Floating)];
        var models = Project("SorterModelSets", projectConfiguration.NewRootProjectConfig(new("Source models",
            new resultsConfig("Models", sourceName, ResultsFunctionCatalog.require(sourceName).Version,
                ListModule.Singleton(new resultsDataTypeConfig(modelsType, ListModule.Singleton("PrefixLibId"))), ListModule.OfArray(generatorParameters)))));
        var tests = Project("SortableTests", projectConfiguration.NewRootProjectConfig(new("Source tests",
            new resultsConfig("Tests", ResultsFunctionNames.prefixSortableTests, "1",
                ListModule.Singleton(new resultsDataTypeConfig(testsType, ListModule.Singleton("PrefixLibId"))),
                ListModule.OfArray(new[] {prefixBinding, Bind("SortableDataFormat", "Bools", parameterValueType.Text)})))));
        var setup = new setupConfig("Setup", SetupFunctionNames.evaluationSource, SetupFunctionCatalog.require(SetupFunctionNames.evaluationSource).Version,
            ListModule.OfArray(new[] {
                new setupDataTypeConfig(modelsType, models.Id, ListModule.Singleton("PrefixLibId")),
                new setupDataTypeConfig(testsType, tests.Id, ListModule.Singleton("PrefixLibId")) }), ListModule.Singleton(prefixBinding));
        var definition = ResultsFunctionCatalog.require(ResultsFunctionNames.standardMsuf4SimpleSgd);
        var outputs = definition.SortopiaDataTypes.Select(type => new resultsDataTypeConfig(type,
            ListModule.OfArray<string>(resultsSpans is null ? ["GenerationCount"] : ["GenerationCount", "OrthoRate", "SelfSymRate"])));
        var consumer = Project("SorterSgd", projectConfiguration.NewBranchProjectConfig(new("Standard SGD",
            new resultsConfig("Results", ResultsFunctionNames.standardMsuf4SimpleSgd, definition.Version, ListModule.OfSeq(outputs), ListModule.OfArray(SgdParameters)), setup)), resultsSpans);
        if (reporting is not null)
        {
            var inputs = RunDto.fromDomain(consumer.Runs[0]);
            inputs.reporting = Microsoft.FSharp.Core.FSharpOption<sgdReportingConfig>.Some(reporting);
            consumer = project.withRuns(consumer, [RunDto.toDomain(consumer, inputs)]);
        }
        workspaceModule Module(project p) => workspaceModule.createInherited(Guid.NewGuid(), p.ProjectName,
            ModuleDefaults.configuration(p.IsBranchProject), ModuleOverridesDto.toDomain(p.IsBranchProject ? hierarchyKind.Branch : hierarchyKind.Root,
                new moduleOverridesDto { resultsFunctionName = p.ResultsFunctionName, setupFunctionName = p.IsBranchProject ? SetupFunctionNames.evaluationSource : null }), [], [p]);
        var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [Module(models), Module(tests), Module(consumer)]);
        Directory.CreateDirectory(Root);
        WorkspaceFileStore.CreateWorkspace(Root, world, Token);
        world = WorkspaceFileStore.LoadDefinition(Root);
        foreach (var source in new[] {models, tests})
            Completed(FileRunExecution.execute(world, Root, world.ProjectIndex[source.Id].Runs[0], null!, Token));
        return (world, world.ProjectIndex[consumer.Id]);
    }

    [Fact]
    public void Standard_SGD_copies_two_sources_executes_portably_and_reuses_final_population_for_partial_retries()
    {
        var (world, consumer) = SgdWorkspace();
        var sourceFiles = Directory.GetFiles(Root, "*.msgpack", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var prepared = Prepare(world, consumer); Completed(prepared.Execution);
        Assert.Single(prepared.WorkItemIds);
        Assert.Equal(2, Assert.Single(prepared.Execution.Setup).StoredOutputs.Length);
        foreach (var file in sourceFiles) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, consumer.Id));
        var parameters = Path.Combine(folder, "runParameters");
        var report = RunParameterExecution.executeFolder(parameters, null!, null!, Token); Completed(report);
        var outputs = Assert.Single(report.Results).StoredOutputs;
        Assert.Equal(3, outputs.Length);
        var poolsOutput = outputs.Single(value => value.Reference.DataType.IsSorterPoolSet);
        var store = new SortopiaFileDb(folder, consumer.Id);
        var pools = Assert.IsType<functionData.SorterPoolSet>(((ISortopiaDb)store).Load(poolsOutput.Reference, Token).ResultValue.Value).Item;
        Assert.Equal(3, pools.Generation);
        Assert.Equal(4, pools.History.Length);
        Assert.Single(pools.Pools);
        Assert.InRange(pools.Pools[0].Members.Length, 1, 2);
        var poolsBytes = File.ReadAllBytes(store.GetPath(poolsOutput.Reference));
        var modelsOutput = outputs.Single(value => value.Reference.DataType.IsSorterModelSet);
        File.Delete(store.GetPath(modelsOutput.Reference));
        var retry = RunParameterExecution.executeFolder(parameters, null!, null!, Token); Completed(retry);
        var retried = Assert.Single(retry.Results).StoredOutputs;
        Assert.True(retried.Single(value => value.Reference.DataType.IsSorterPoolSet).Reused);
        Assert.False(retried.Single(value => value.Reference.DataType.IsSorterModelSet).Reused);
        Assert.Equal(poolsBytes, File.ReadAllBytes(store.GetPath(poolsOutput.Reference)));
        Assert.Equal(outputState.Complete, Assert.Single(RunParameterExecution.inspectFolder(parameters, null!, Token)).Outputs.State);
    }

    [Fact]
    public void Standard_SGD_rejects_other_algorithms_before_saving_any_results()
    {
        var (world, consumer) = SgdWorkspace(wrongFamily: true);
        Completed(Prepare(world, consumer).Execution);
        var folder = WorkspaceData.projectFolder(world, Root, consumer.Id);
        var report = RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token);
        Assert.Equal(runExecutionStatus.Failed, report.Status);
        Assert.Contains("Simple MSUF4", report.Error);
        Assert.Empty(DataFiles(folder, "results"));
    }

    [Theory]
    [InlineData("GenerationCount", "-1")]
    [InlineData("SorterCountPerPool", "0")]
    [InlineData("SelectedSorterCountPerPool", "3")]
    [InlineData("AdjustCeLengthFrequency", "0")]
    [InlineData("SortedFraction", "0")]
    [InlineData("SeedModificationRate", "1.01")]
    [InlineData("MutatorVariantKey", "V3")]
    public void Standard_SGD_validates_parameter_values_before_planning_outputs(string key, string text)
    {
        var defaults = SgdParameters.Select(value => value.KeyName == key ? Bind(key, text, value.ValueType) : value);
        var error = ResultsFunctionCatalog.require(ResultsFunctionNames.standardMsuf4SimpleSgd).InputError.Invoke(ListModule.OfSeq(defaults)).Invoke(ListModule.Empty<parameterSpan>());
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
