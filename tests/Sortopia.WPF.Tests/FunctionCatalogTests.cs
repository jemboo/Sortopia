using System.IO;
using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Inputs;
using Sortopia.WPF.Models;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionCatalogTests
{
    private static parameterSet Parameters(params parameterBinding[] bindings) => parameterSet.fromBindings(bindings);
    private static FSharpList<functionData> Invoke(bool setup, string name, parameterSet parameters, params functionData[] inputs)
    {
        var function = setup ? FunctionCatalogs.setupFunctionCatalog[name] : FunctionCatalogs.resultsFunctionCatalog[name];
        var result = function.Invoke(parameters).Invoke(ListModule.OfArray(inputs));
        Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
        return result.ResultValue;
    }
    private static parameterSet ModelParameters(int repl = 0) => Parameters(
        ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.stageLengthKey, 3),
        ParameterBinding.create(ParamKeys.simpleSorterModelTypeKey, "Mssi"), ParameterBinding.create(ParamKeys.rngTypeKey, "SplitMix64"),
        ParameterBinding.create(ParamKeys.excludeSelfCeKey, true), ParameterBinding.create(ParamKeys.sorterCountKey, 3),
        ParameterBinding.create(ParamKeys.replKey, repl));

    [Fact]
    public void Named_setup_functions_generate_tests_and_repeatable_models_for_the_results_evaluator()
    {
        var tests = Assert.IsType<functionData.SortableTests>(Assert.Single(Invoke(true, SetupFunctionNames.standardTests,
            Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")))));
        Assert.Equal(16, SortableTests.getSortableCount(tests.Item1));
        Assert.Empty(tests.Item2);
        var models = Assert.IsType<functionData.SorterModelSet>(Assert.Single(Invoke(true, SetupFunctionNames.uniformSorterModels, ModelParameters())));
        var repeated = Assert.IsType<functionData.SorterModelSet>(Assert.Single(Invoke(true, SetupFunctionNames.uniformSorterModels, ModelParameters())));
        Assert.Equal(3, models.Item.Count);
        Assert.Equal(models.Item.SorterModels.Select(SorterModel.getId), repeated.Item.SorterModels.Select(SorterModel.getId));
        var next = Assert.IsType<functionData.SorterModelSet>(Assert.Single(Invoke(true, SetupFunctionNames.uniformSorterModels, ModelParameters(1))));
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
        var key = merge ? ParamKeys.mergeLibIdKey : ParamKeys.prefixLibIdKey;
        var library = merge ? MergeLibId.toString(MergeLibId.availableForChannelCount(4).First(value => value.MergeDimension == 2))
            : PrefixLibId.toString(PrefixLib.ids().First(value => value.ChannelCount == 8 && value.StageLength == 1));
        var value = Assert.IsType<functionData.SortableTests>(Assert.Single(Invoke(false, ResultsFunctionNames.sortableTests,
            Parameters(ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools"), ParameterBinding.create(key, library)))));
        Assert.Equal(merge ? 4 : 8, SortableTests.getChannelCount(value.Item1));
        Assert.Equal(merge ? 9 : 81, SortableTests.getSortableCount(value.Item1));
        Assert.Equal(merge ? 2 : 4, value.Item2.Length);
    }

    [Fact]
    public void Catalog_functions_report_missing_or_invalid_inputs_without_requiring_a_host()
    {
        var empty = Parameters();
        foreach (var function in FunctionCatalogs.resultsFunctionCatalog.Values.Concat(FunctionCatalogs.setupFunctionCatalog.Values))
        {
            var result = function.Invoke(empty).Invoke(ListModule.Empty<functionData>());
            Assert.True(result.IsError);
            Assert.NotEmpty(result.ErrorValue);
        }
        var wrongType = FunctionCatalogs.setupFunctionCatalog[SetupFunctionNames.standardTests].Invoke(
            Parameters(ParameterBinding.create(ParamKeys.sortableDataFormatKey, "invalid"), ParameterBinding.create(ParamKeys.channelCountKey, 4)))
            .Invoke(ListModule.Empty<functionData>());
        Assert.True(wrongType.IsError);
        var invalidReplication = FunctionCatalogs.setupFunctionCatalog[SetupFunctionNames.uniformSorterModels].Invoke(ModelParameters(-1)).Invoke(ListModule.Empty<functionData>());
        Assert.True(invalidReplication.IsError);
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
            Assert.Equal(FunctionCatalogs.resultsFunctionCatalog.Keys, module.Editor!.ResultsFunctionOverride.Choices);
            Assert.Equal(FunctionCatalogs.setupFunctionCatalog.Keys, module.Editor.SetupFunctionOverride.Choices);
            Assert.False(module.Editor.ResultsDatabaseOverride.HasChoices);
            module.Editor.ResultsFunctionOverride.IsOverridden = true;
            module.Editor.ResultsFunctionOverride.EffectiveValue = ResultsFunctionNames.sortableTests;
            if (branch)
            {
                module.Editor.SetupFunctionOverride.IsOverridden = true;
                module.Editor.SetupFunctionOverride.EffectiveValue = SetupFunctionNames.standardTests;
            }
            page.AddProjectCommand.Execute(null);
            var project = page.Selected!;
            Assert.Equal(ResultsFunctionNames.sortableTests, project.Editor!.ResultsFunctionName);
            Assert.Same(module.Editor, project.Editor.ResultsFunctionOverride.InheritedSource);
            project.Editor.ResultsFunctionOverride.IsOverridden = true;
            project.Editor.ResultsFunctionOverride.EffectiveValue = ResultsFunctionNames.sorterEval;
            page.AddRunCommand.Execute(null);
            var run = page.Selected!;
            Assert.Equal(ResultsFunctionNames.sorterEval, run.Editor!.ResultsFunctionName);
            Assert.False(run.Editor.ResultsFunctionOverride.CanOverride);
            if (branch)
            {
                Assert.Equal(SetupFunctionNames.standardTests, run.Editor.SetupFunctionName);
                run.Editor.SetupFunctionOverride.IsOverridden = true;
                run.Editor.SetupFunctionOverride.EffectiveValue = SetupFunctionNames.uniformSorterModels;
            }
            await page.SaveWorkspaceAsync();
            Assert.Empty(page.Error);
            var saved = WorkspaceFileStore.LoadDefinition(root).Modules[0].Projects[0].Runs[0];
            Assert.Equal(ResultsFunctionNames.sorterEval, saved.ResultsFunctionName);
            if (branch) Assert.Equal(SetupFunctionNames.uniformSorterModels, saved.SetupFunctionName.Value);
            var reopened = new WorkspacePageViewModel(() => { }, _ => null, settings);
            var savedProject = reopened.Roots[0].Children[0].Children[0];
            Assert.True(savedProject.IsSealed);
            Assert.False(savedProject.Editor!.ResultsFunctionOverride.CanEditValue);
            Assert.Equal(ResultsFunctionNames.sorterEval, savedProject.Editor.ResultsFunctionName);
        }
        finally { temporary.Delete(recursive: true); }
    }
}
