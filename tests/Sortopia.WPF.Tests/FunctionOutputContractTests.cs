using System.IO;
using Microsoft.FSharp.Collections;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Inputs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionOutputContractTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-output-contracts-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static parameterSet Parameters(IEnumerable<parameterBindingDto> values) => parameterSet.fromBindings(values.Select(ParameterBindingDto.toDomain));
    private static parameterBindingDto Format => RunExecutionTests.Binding("SortableDataFormat", "Bools");
    private static parameterBindingDto[] EvaluationDefaults => [RunExecutionTests.Binding("ChannelCount", "4", parameterValueType.Integer),
        RunExecutionTests.Binding("SorterEvalType", "V1"), RunExecutionTests.Binding("CollectSortableTests", "false", parameterValueType.Boolean)];
    internal static projectSettingsDto Combined(bool branch)
    {
        var settings = RunExecutionTests.Settings(branch);
        if (branch)
        {
            settings.setupFunctionName = SetupFunctionNames.evaluationStandard;
            settings.setupParameterDefaults = [.. RunExecutionTests.ModelDefaults(), Format];
            settings.setupDataTypes = [];
            settings.resultsFunctionName = ResultsFunctionNames.sorterEval;
            settings.resultsParameterDefaults = EvaluationDefaults;
        }
        else
        {
            settings.resultsFunctionName = ResultsFunctionNames.standardEvaluationInputs;
            settings.resultsParameterDefaults = [.. RunExecutionTests.ModelDefaults(), Format];
        }
        return settings;
    }
    private static FSharpList<functionData> Invoke(bool setup, string name, parameterSet parameters, params functionData[] inputs)
        => CatalogTestHost.Invoke(setup, name, parameters, inputs);

    [Fact]
    public void Catalog_maps_define_output_text_and_query_subsets_of_required_parameters()
    {
        var contracts = FunctionCatalogs.resultsFunctionCatalog.Values.Select(value => (value.DataTypes, value.RequiredParameters))
            .Concat(FunctionCatalogs.setupFunctionCatalog.Values.Select(value => (value.DataTypes, value.RequiredParameters)));
        foreach (var (outputs, required) in contracts)
        {
            Assert.NotEmpty(outputs);
            Assert.All(outputs.Keys, type => Assert.Equal("", SortopiaDataType.text(type)));
            Assert.All(outputs.Values.SelectMany(keys => keys), key => Assert.Contains(required, descriptor => descriptor.Name == key));
        }
        var function = SetupFunctions.evaluationStandard;
        Assert.Equal(new[] { "ChannelCount", "SortableDataFormat" }, function.DataTypes[sortopiaDataType.NewSortableTests("")]);
        Assert.DoesNotContain(function.DataTypes[sortopiaDataType.NewSortableTests("")], key => key == "Repl");
        Assert.Contains(function.DataTypes[sortopiaDataType.NewSorterModelSet("")], key => key == "Repl");
        var settings = Combined(false);
        var run = (run.RootRun)RunExecutionTests.MakeRun(false, settings);
        var dto = SettingsDto.fromDomain(projectConfiguration.NewRootProjectConfig(run.Item.ProjectConfiguration));
        dto.resultsDataTypes[0].dataType.value = "edited label";
        var edited = (projectConfiguration.RootProjectConfig)SettingsDto.toDomain(hierarchyKind.Root, dto);
        Assert.Contains("exactly match", Assert.Throws<ArgumentException>(() => FunctionConfiguration.validateResults(edited.Item.Results)).Message);
        dto.resultsDataTypes[0].dataType.value = "";
        dto.resultsDataTypes[0].queryParameterKeys = [];
        edited = (projectConfiguration.RootProjectConfig)SettingsDto.toDomain(hierarchyKind.Root, dto);
        Assert.Contains("keys", Assert.Throws<ArgumentException>(() => FunctionConfiguration.validateResults(edited.Item.Results)).Message);
    }

    [Fact]
    public void Module_contracts_flow_to_descendants_and_keep_output_labels_and_keys_read_only()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.LoadProject(Combined(true));
        var child = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Child", family: hierarchyKind.Branch); module.Children.Add(child);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); child.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        foreach (var node in new[] { module, child, project, run })
        {
            Assert.False(node.Editor!.OutputTypesOverride.CanOverride);
            Assert.False(node.Editor.ResultsQueryKeysOverride.CanOverride);
            Assert.Equal(2, node.Editor.InheritedSetupDataTypes.Count());
            Assert.All(node.Editor.InheritedSetupDataTypes, row => { Assert.False(row.CanEditContract); Assert.True(row.GeneratesData); Assert.Empty(row.Error); });
            Assert.Empty(node.Editor.FunctionCompatibilityError);
            Assert.Same(module.Editor, node.Editor.OutputTypesOverride.InheritedSource);
            Assert.Same(module.Editor, node.Editor.SetupDataTypesOverride.InheritedSource);
        }
        var span = WorkspaceParameterSetsPreviewTests.AddSpan(run.Editor!, "Repl", true, "1", "2");
        Assert.Contains("SortableDataFormat", span.KeyChoices);
        Assert.DoesNotContain("CollectSortableTests", span.KeyChoices);
        run.Editor!.UseSetupDefaultsAsSpansCommand.Execute(null);
        Assert.Equal(2, run.Editor!.SetupParameterSetsPreview.Rows.Count());
        module.Editor.SetupFunctionName = SetupFunctionNames.standardTests;
        Assert.Contains("SorterModelSet", run.Editor.FunctionCompatibilityError);
        Assert.Contains("not required", span.Error);
        module.Editor.SetupFunctionName = SetupFunctionNames.evaluationMerge;
        Assert.Single(project.Editor!.InheritedSetupDataTypes, row => row.RequiresSource);
        Assert.Contains("source project", project.Editor.SetupDataTypesError);
        Assert.Empty(module.Editor.SetupDataTypesError);
    }

    [Fact]
    public void Added_publishers_and_chunked_evaluator_execute_as_independent_functions()
    {
        var tests = Assert.IsType<functionData.SortableTests>(Assert.Single(Invoke(false, ResultsFunctionNames.standardSortableTests,
            Parameters([RunExecutionTests.Binding("ChannelCount", "4", parameterValueType.Integer), Format]))));
        Assert.Equal(16, SortableTests.getSortableCount(tests.Item1));
        var models = Assert.IsType<functionData.SorterModelSet>(Assert.Single(Invoke(false, ResultsFunctionNames.uniformSorterModels, Parameters(RunExecutionTests.ModelDefaults()))));
        Assert.Equal(3, models.Item.Count);
        var combined = Invoke(false, ResultsFunctionNames.standardEvaluationInputs, Parameters(Combined(false).resultsParameterDefaults));
        Assert.Equal(2, combined.Length);
        var normal = Assert.IsType<functionData.SorterSetEval>(Assert.Single(Invoke(false, ResultsFunctionNames.sorterEval, Parameters(EvaluationDefaults), combined.ToArray())));
        var mutants = Assert.IsType<functionData.SorterSetEval>(Assert.Single(Invoke(false, ResultsFunctionNames.mutantEval,
            Parameters([RunExecutionTests.Binding("SorterEvalType", "V1")]), combined.ToArray())));
        Assert.Equal(normal.Item.SorterEvals, mutants.Item.SorterEvals);
        var manyModels = Invoke(false, ResultsFunctionNames.uniformSorterModels,
            Parameters(RunExecutionTests.ModelDefaults()).WithBinding(ParameterBinding.create(ParamKeys.sorterCountKey, 1001)));
        var multipleChunks = Assert.IsType<functionData.SorterSetEval>(Assert.Single(Invoke(false, ResultsFunctionNames.mutantEval,
            Parameters([RunExecutionTests.Binding("SorterEvalType", "V1")]), [tests, .. manyModels])));
        Assert.Equal(1001, multipleChunks.Item.SorterEvals.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Combined_merge_and_prefix_setup_consume_matching_tests_and_generate_models(bool prefix)
    {
        var library = prefix ? ParameterBinding.create(ParamKeys.prefixLibIdKey, PrefixLib.ids().First(id => id.ChannelCount == 8 && id.StageLength == 1))
            : ParameterBinding.create(ParamKeys.mergeLibIdKey, MergeLibId.availableForChannelCount(4).First(id => id.MergeDimension == 2));
        var sourceParameters = parameterSet.fromBindings([ParameterBindingDto.toDomain(Format), library]);
        var source = Assert.Single(Invoke(false, prefix ? ResultsFunctionNames.prefixSortableTests : ResultsFunctionNames.mergeSortableTests, sourceParameters));
        var parameters = Parameters([.. RunExecutionTests.ModelDefaults(prefix ? 8 : 4), Format]).WithBinding(library);
        var name = prefix ? SetupFunctionNames.evaluationPrefix : SetupFunctionNames.evaluationMerge;
        var generated = Invoke(true, name, parameters, source);
        var sourceTests = Assert.IsType<functionData.SortableTests>(source);
        var reused = Assert.IsType<functionData.SortableTests>(generated.Single(value => value.IsSortableTests));
        Assert.Same(sourceTests.Item1, reused.Item1); Assert.Same(sourceTests.Item2, reused.Item2);
        Assert.Equal(3, Assert.IsType<functionData.SorterModelSet>(generated.Single(value => value.IsSorterModelSet)).Item.Count);
        Assert.Single(FunctionCatalogs.setupFunctionCatalog[name].SourceDataTypes);
        var wrongChannels = parameters.WithBinding(ParameterBinding.create(ParamKeys.channelCountKey, 6));
        Assert.Contains("ChannelCount", Assert.ThrowsAny<Exception>(() => Invoke(true, name, wrongChannels, source)).Message);
    }

    private workspace SaveRun(bool branch, projectSettingsDto settings, parameterSpanDto[] results, parameterSpanDto[] setup)
    {
        var run = RunExecutionTests.MakeRun(branch, settings, results, setup);
        var config = branch ? projectConfiguration.NewBranchProjectConfig(((run.BranchRun)run).Item.ProjectConfiguration)
            : projectConfiguration.NewRootProjectConfig(((run.RootRun)run).Item.ProjectConfiguration);
        var parent = project.withRuns(project.create(run.ParentProjectId, "Project", "Project", config, []), [run]);
        var module = WorkspaceHierarchyTests.MakeModule(branch ? hierarchyKind.Branch : hierarchyKind.Root, Guid.NewGuid(), "Module", "Module", [], [parent]);
        var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [module]);
        WorkspaceFileStore.CreateWorkspace(temporary.FullName, world, Token);
        return WorkspaceFileStore.LoadDefinition(temporary.FullName);
    }
    private runExecutionReport Execute(workspace world)
    {
        var report = FileRunExecution.execute(world, temporary.FullName, world.Modules[0].Projects[0].Runs[0], null!, Token);
        Assert.True(report.Status == runExecutionStatus.Completed, report.Error);
        return report;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_saved_output_uses_its_own_projection_across_replications_and_reopening(bool branch)
    {
        parameterSpanDto[] spans = [new() { keyName = "Repl", values = ["0", "1"] }];
        var world = SaveRun(branch, Combined(branch), branch ? [] : spans, branch ? spans : []);
        var first = Execute(world);
        var calls = branch ? first.Setup : first.Results;
        Assert.Equal(2, calls.Length);
        var tests = calls.Select(call => call.StoredOutputs.Single(output => output.Reference.DataType.IsSortableTests)).ToArray();
        Assert.False(tests[0].Reused); Assert.True(tests[1].Reused); Assert.Equal(tests[0].Reference, tests[1].Reference);
        var models = calls.Select(call => call.StoredOutputs.Single(output => output.Reference.DataType.IsSorterModelSet)).ToArray();
        Assert.NotEqual(models[0].Reference, models[1].Reference); Assert.All(models, output => Assert.False(output.Reused));
        if (branch) Assert.Equal(2, first.Results.SelectMany(call => call.StoredOutputs).Select(output => output.Reference.Id).Distinct().Count());
        else
        {
            // Fetching tests from a multi-output source must not require model-only keys.
            var source = world.Modules[0].Projects[0];
            var testKeys = ListModule.OfArray(new[] { "ChannelCount", "SortableDataFormat" });
            var entry = new setupDataTypeConfig(sortopiaDataType.NewSortableTests(""), source.Id, testKeys);
            var selected = WorkspaceData.loadSource(world, temporary.FullName, entry,
                Parameters([RunExecutionTests.Binding("ChannelCount", "4", parameterValueType.Integer), Format]), Token);
            Assert.Equal(tests[0].Reference, selected.Reference);
        }
        var second = Execute(WorkspaceFileStore.LoadDefinition(temporary.FullName));
        Assert.All(second.Setup.Concat(second.Results).SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        var dataFiles = Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories).Count(path => path.Contains(Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar));
        Assert.Equal(branch ? 5 : 3, dataFiles);
    }

    [Fact]
    public void Results_generators_do_not_duplicate_outputs_for_unconsumed_setup_data()
    {
        var world = SaveRun(true, RunExecutionTests.Settings(true), [], [new() { keyName = "ChannelCount", values = ["4", "8"] }]);
        var report = Execute(world);
        Assert.NotEqual(report.Setup[0].StoredOutputs.Head.Reference, report.Setup[1].StoredOutputs.Head.Reference);
        Assert.Equal(report.Results[0].StoredOutputs.Head.Reference, report.Results[1].StoredOutputs.Head.Reference);
        Assert.True(report.Results[1].StoredOutputs.Head.Reused);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
