using System.IO;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Inputs;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionCatalogTests
{
    private static parameterSet Parameters(params parameterBinding[] bindings) => parameterSet.fromBindings(bindings);
    private static FSharpList<functionData> Invoke(bool setup, string name, parameterSet parameters, params functionData[] inputs)
        => CatalogTestHost.Invoke(setup, name, parameters, inputs);
    private static parameterSet ModelParameters(int repl = 0) => Parameters(
        ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.stageLengthKey, 3),
        ParameterBinding.create(ParamKeys.sorterModelTypeKey, "Simple"), ParameterBinding.create(ParamKeys.rngTypeKey, "SplitMix64"),
        ParameterBinding.create(ParamKeys.aMaxKey, 1), ParameterBinding.create(ParamKeys.activationProbabilityKey, 0.5), ParameterBinding.create(ParamKeys.sorterCountKey, 3),
        ParameterBinding.create(ParamKeys.replKey, repl));

    [Fact]
    public void Returning_unit_without_saving_declared_outputs_is_rejected_for_both_phases()
    {
        var value = RunExecutionTests.MakeRun(true);
        var factory = new CatalogTestHost(value, TestContext.Current.CancellationToken);
        var sets = RunParameterSets.create(value);
        var resultsDefinition = ResultsFunctionCatalog.functions[value.ResultsFunctionName];
        var noResults = FunctionDefinition.results("1", resultsDefinition.RequiredParameters, ListModule.OfSeq(resultsDefinition.DataTypes.Select(pair => Tuple.Create(pair.Key, pair.Value))),
            FuncConvert.FromFunc<runHost, Unit>(_ => null!));
        var results = factory.ForResults(sets.Results[0], ListModule.Empty<storedOutput>());
        Assert.Contains("save exactly", Assert.Throws<InvalidOperationException>(() => noResults.Execute.Invoke(results)).Message);

        var setupDefinition = SetupFunctionCatalog.functions[value.SetupFunctionName.Value];
        var noSetup = FunctionDefinition.setup("1", setupDefinition.RequiredParameters, ListModule.OfSeq(setupDefinition.DataTypes.Select(pair => Tuple.Create(pair.Key, pair.Value))),
            FuncConvert.FromFunc<runHost, FSharpFunc<FSharpMap<sortopiaDataType, Guid>, Unit>>(_ =>
                FuncConvert.FromFunc<FSharpMap<sortopiaDataType, Guid>, Unit>(_ => null!)));
        var setup = factory.ForSetup(sets.Setup.Value[0]);
        Assert.Contains("save exactly", Assert.Throws<InvalidOperationException>(() => noSetup.Execute.Invoke(setup).Invoke(MapModule.Empty<sortopiaDataType, Guid>())).Message);
    }

    [Fact]
    public void Named_setup_functions_generate_tests_and_repeatable_models_for_the_results_evaluator()
    {
        var tests = Assert.IsType<functionData.SortableTests>(Assert.Single(Invoke(true, SetupFunctionNames.standardTests,
            Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")))));
        Assert.Equal(16, SortableTests.getSortableCount(tests.Item1));
        Assert.Empty(tests.Item2);
        var models = Assert.IsType<functionData.SorterModelSet>(Assert.Single(Invoke(true, SetupFunctionNames.uniformMssiSorterModels, ModelParameters())));
        var repeated = Assert.IsType<functionData.SorterModelSet>(Assert.Single(Invoke(true, SetupFunctionNames.uniformMssiSorterModels, ModelParameters())));
        Assert.Equal(3, models.Item.Count);
        Assert.Equal(models.Item.SorterModels.Select(SorterModel.getId), repeated.Item.SorterModels.Select(SorterModel.getId));
        var next = Assert.IsType<functionData.SorterModelSet>(Assert.Single(Invoke(true, SetupFunctionNames.uniformMssiSorterModels, ModelParameters(1))));
        Assert.Empty(models.Item.SorterModels.Select(SorterModel.getId).Intersect(next.Item.SorterModels.Select(SorterModel.getId)));
        var evaluation = Assert.IsType<functionData.SorterSetEval>(Assert.Single(Invoke(false, ResultsFunctionNames.sorterEval,
            Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.sorterEvalTypeKey, "V1"),
                ParameterBinding.create(ParamKeys.collectSortableTestsKey, false)), tests, models)));
        Assert.Equal(3, evaluation.Item.SorterEvals.Length);
        Assert.Equal(SortableTests.getId(tests.Item1), evaluation.Item.SorterTestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Standalone_results_generation_keeps_the_generating_prefix(bool merge)
    {
        var library = merge ? ParameterBinding.create(ParamKeys.mergeLibIdKey, MergeLibId.availableForChannelCount(4).First(value => value.MergeDimension == 2))
            : ParameterBinding.create(ParamKeys.prefixLibIdKey, PrefixLib.ids().First(value => value.ChannelCount == 8 && value.StageLength == 1));
        var value = Assert.IsType<functionData.SortableTests>(Assert.Single(Invoke(false, merge ? ResultsFunctionNames.mergeSortableTests : ResultsFunctionNames.prefixSortableTests,
            Parameters(ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools"), library))));
        Assert.Equal(merge ? 4 : 8, SortableTests.getChannelCount(value.Item1));
        Assert.Equal(merge ? 9 : 81, SortableTests.getSortableCount(value.Item1));
        Assert.Equal(merge ? 2 : 4, value.Item2.Length);
    }

    [Fact]
    public void Catalog_functions_report_missing_or_invalid_inputs_through_a_database_host()
    {
        var empty = Parameters();
        foreach (var function in ResultsFunctionCatalog.functions.Keys.Select(name => (Setup: false, Name: name))
            .Concat(SetupFunctionCatalog.functions.Keys.Select(name => (Setup: true, Name: name))))
        {
            Assert.Contains("Missing required", Assert.ThrowsAny<Exception>(() => Invoke(function.Setup, function.Name, empty)).Message);
        }
        Assert.ThrowsAny<Exception>(() => Invoke(true, SetupFunctionNames.standardTests,
            Parameters(ParameterBinding.create(ParamKeys.sortableDataFormatKey, "invalid"), ParameterBinding.create(ParamKeys.channelCountKey, 4))));
        Assert.ThrowsAny<Exception>(() => Invoke(true, SetupFunctionNames.uniformMssiSorterModels, ModelParameters(-1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Function_choices_follow_inheritance_and_the_chosen_names_survive_saving(bool branch)
    {
        var temporary = Directory.CreateTempSubdirectory("Sortopia-function-catalog-");
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
            var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(root);
            var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
            (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
            var module = page.Selected!;
            Assert.Equal(branch ? [] : ResultsFunctionCatalog.functions.Where(entry => entry.Value.SourceDataTypes.IsEmpty).Select(entry => entry.Key), module.Editor!.ResultsFunctionOverride.Choices);
            Assert.Equal(SetupFunctionCatalog.functions.Keys, module.Editor.SetupFunctionOverride.Choices);
            Assert.False(module.Editor.ResultsDatabaseOverride.HasChoices);
            module.Editor.ResultsFunctionOverride.IsOverridden = true;
            module.Editor.ResultsFunctionOverride.EffectiveValue = ResultsFunctionNames.mergeSortableTests;
            if (branch)
            {
                module.Editor.SetupFunctionOverride.IsOverridden = true;
                module.Editor.SetupFunctionOverride.EffectiveValue = SetupFunctionNames.standardTests;
            }
            module.Editor.LoadProject(RunExecutionTests.Settings(branch));
            page.AddProjectCommand.Execute(null);
            var project = page.Selected!;
            Assert.Equal(ResultsFunctionNames.mergeSortableTests, project.Editor!.ResultsFunctionName);
            Assert.Same(module.Editor, project.Editor.ResultsFunctionOverride.InheritedSource);
            project.Editor.ResultsFunctionOverride.IsOverridden = true;
            project.Editor.ResultsFunctionOverride.EffectiveValue = ResultsFunctionNames.sorterEval;
            page.AddRunCommand.Execute(null);
            var run = page.Selected!;
            Assert.Equal(ResultsFunctionNames.mergeSortableTests, run.Editor!.ResultsFunctionName);
            Assert.False(project.Editor.ResultsFunctionOverride.CanOverride);
            Assert.False(run.Editor.ResultsFunctionOverride.CanOverride);
            if (branch)
            {
                Assert.Equal(SetupFunctionNames.standardTests, run.Editor.SetupFunctionName);
                run.Editor.SetupFunctionOverride.IsOverridden = true;
                run.Editor.SetupFunctionOverride.EffectiveValue = SetupFunctionNames.uniformMssiSorterModels;
            }
            WorkspaceRunExecutionTests.SupplyQuerySpans(page);
            await page.SaveWorkspaceAsync();
            Assert.Empty(page.Error);
            var saved = WorkspaceFileStore.LoadDefinition(root).Modules[0].Projects[0].Runs[0];
            Assert.Equal(ResultsFunctionNames.mergeSortableTests, saved.ResultsFunctionName);
            if (branch) Assert.Equal(SetupFunctionNames.standardTests, saved.SetupFunctionName.Value);
            var reopened = new WorkspacePageViewModel(() => { }, _ => null, settings);
            var savedProject = reopened.Roots[0].Children[0].Children[0];
            Assert.True(savedProject.IsSealed);
            Assert.False(savedProject.Editor!.ResultsFunctionOverride.CanEditValue);
            Assert.Equal(ResultsFunctionNames.mergeSortableTests, savedProject.Editor.ResultsFunctionName);
            // A later module choice applies to drafts; sealed projects keep their execution contract.
            var savedModule = reopened.Roots[0].Children[0];
            savedModule.Editor!.ResultsFunctionName = ResultsFunctionNames.prefixSortableTests;
            savedModule.Editor.ResultsParameterDefaults.Single(row => row.Key == "MergeLibId").RemoveCommand.Execute(null);
            await reopened.SaveWorkspaceAsync(); Assert.Empty(reopened.Error);
            var fixedRun = WorkspaceFileStore.LoadDefinition(root).Modules[0].Projects[0].Runs[0];
            Assert.Equal(ResultsFunctionNames.mergeSortableTests, fixedRun.ResultsFunctionName);
            Assert.Contains(fixedRun.ResultsParameterSpans, value => value.KeyName == "MergeLibId");
        }
        finally { temporary.Delete(recursive: true); }
    }
}
