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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Catalog_registrations_match_every_existing_public_name_without_aliases_or_omissions(bool setup)
    {
        var namesType = setup ? typeof(SetupFunctionNames) : typeof(ResultsFunctionNames);
        var published = namesType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string)).Select(property => (string)property.GetValue(null)!).ToArray();
        Assert.Equal(setup ? 32 : 51, published.Length);
        Assert.Equal(published.Length, published.Distinct(StringComparer.Ordinal).Count());
        var registered = setup ? SetupFunctionCatalog.functions.Keys : ResultsFunctionCatalog.functions.Keys;
        Assert.Equal(published.Order(StringComparer.Ordinal), registered.Order(StringComparer.Ordinal));
    }

    private static parameterSet Parameters(params parameterBinding[] bindings) => parameterSet.fromBindings(bindings);
    private static FSharpList<sortopiaStorageData> Invoke(bool setup, string name, parameterSet parameters, params sortopiaStorageData[] inputs)
        => CatalogTestHost.Invoke(setup, name, parameters, inputs);
    private static parameterSet ModelParameters(int repl = 0) => Parameters(
        ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.sorterSuccessFractionKey, Sortopia.Sorting.sorterSuccessFraction.pct50),
        ParameterBinding.create(ParamKeys.rngTypeKey, "SplitMix64"), ParameterBinding.create(ParamKeys.sorterCountKey, 3),
        ParameterBinding.create(ParamKeys.replKey, repl));

    [Fact]
    public void Returning_unit_without_saving_declared_outputs_is_rejected_for_both_phases()
    {
        var value = RunExecutionTests.MakeRun(true);
        var factory = new CatalogTestHost(value, TestContext.Current.CancellationToken);
        var sets = RunParameterSets.create(value);
        var resultsDefinition = ResultsFunctionCatalog.functions[value.ResultsFunctionName];
        var noResults = FunctionDefinition.results("1", resultsDefinition.RequiredParameters, ListModule.OfSeq(resultsDefinition.DataTypes.Select(pair => Tuple.Create(pair.Key, pair.Value))),
            [], resultsDefinition.ParameterSupport,
            FuncConvert.FromFunc<runHost, Unit>(_ => null!));
        var results = factory.ForResults(sets.Results[0], ListModule.Empty<storedOutput>());
        Assert.Contains("save exactly", Assert.Throws<InvalidOperationException>(() => noResults.Execute.Invoke(results)).Message);

        var setupDefinition = SetupFunctionCatalog.functions[value.SetupFunctionName.Value];
        var noSetup = FunctionDefinition.setup("1", setupParameterRequirements.NewDeclared(setupDefinition.RequiredParameters), ListModule.OfSeq(setupDefinition.DataTypes.Select(pair => Tuple.Create(pair.Key, pair.Value))),
            [], setupDefinition.ParameterSupport,
            FuncConvert.FromFunc<runHost, FSharpFunc<FSharpMap<sortopiaDataType, Guid>, Unit>>(_ =>
                FuncConvert.FromFunc<FSharpMap<sortopiaDataType, Guid>, Unit>(_ => null!)));
        var setup = factory.ForSetup(sets.Setup.Value[0]);
        Assert.Contains("save exactly", Assert.Throws<InvalidOperationException>(() => noSetup.Execute.Invoke(setup).Invoke(MapModule.Empty<sortopiaDataType, Guid>())).Message);
    }

    [Fact]
    public void Named_setup_functions_generate_tests_and_repeatable_models_for_the_results_evaluator()
    {
        var tests = Assert.IsType<sortopiaStorageData.SortableTests>(Assert.Single(Invoke(true, SetupFunctionNames.standardTests,
            Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")))));
        Assert.Equal(16, SortableTests.getSortableCount(tests.Item));
        var models = Assert.IsType<sortopiaStorageData.SorterModelSet>(Assert.Single(Invoke(true, SetupFunctionNames.uniformMssiSimpleSorterModels, ModelParameters())));
        var repeated = Assert.IsType<sortopiaStorageData.SorterModelSet>(Assert.Single(Invoke(true, SetupFunctionNames.uniformMssiSimpleSorterModels, ModelParameters())));
        Assert.Equal(3, models.Item.Count);
        Assert.Equal(models.Item.SorterModels.Select(SorterModel.getId), repeated.Item.SorterModels.Select(SorterModel.getId));
        var next = Assert.IsType<sortopiaStorageData.SorterModelSet>(Assert.Single(Invoke(true, SetupFunctionNames.uniformMssiSimpleSorterModels, ModelParameters(1))));
        Assert.Empty(models.Item.SorterModels.Select(SorterModel.getId).Intersect(next.Item.SorterModels.Select(SorterModel.getId)));
        var evaluation = Assert.IsType<sortopiaStorageData.SorterSetEval>(Assert.Single(Invoke(false, ResultsFunctionNames.sorterEval,
            Parameters(ParameterBinding.create(ParamKeys.sorterEvalTypeKey, "V1"),
                ParameterBinding.create(ParamKeys.collectSortableTestsKey, false)), tests, models)));
        Assert.Equal(3, evaluation.Item.SorterEvals.Length);
        Assert.Equal(SortableTests.getId(tests.Item), evaluation.Item.SorterTestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Standalone_results_generation_saves_filtered_tests(bool merge)
    {
        var library = merge ? ParameterBinding.create(ParamKeys.mergeLibIdKey, MergeLibId.availableForChannelCount(4).First(value => value.MergeDimension == 2))
            : ParameterBinding.create(ParamKeys.prefixLibIdKey, PrefixLib.ids().First(value => value.ChannelCount == 8 && value.StageLength == 1));
        var value = Assert.IsType<sortopiaStorageData.SortableTests>(Assert.Single(Invoke(false, merge ? ResultsFunctionNames.mergeSortableTests : ResultsFunctionNames.prefixSortableTests,
            Parameters(ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools"), library))));
        Assert.Equal(merge ? 4 : 8, SortableTests.getChannelCount(value.Item));
        Assert.Equal(merge ? 9 : 81, SortableTests.getSortableCount(value.Item));
    }

    [Fact]
    public void Catalog_functions_report_missing_or_invalid_inputs_through_a_database_host()
    {
        var empty = Parameters();
        foreach (var function in ResultsFunctionCatalog.functions.Keys.Select(name => (Setup: false, Name: name))
            .Concat(SetupFunctionCatalog.functions.Where(entry => !entry.Value.ParametersFromSources).Select(entry => (Setup: true, Name: entry.Key))))
        {
            Assert.Contains("Missing required", Assert.ThrowsAny<Exception>(() => Invoke(function.Setup, function.Name, empty)).Message);
        }
        Assert.ThrowsAny<Exception>(() => Invoke(true, SetupFunctionNames.standardTests,
            Parameters(ParameterBinding.create(ParamKeys.sortableDataFormatKey, "invalid"), ParameterBinding.create(ParamKeys.channelCountKey, 4))));
        Assert.ThrowsAny<Exception>(() => Invoke(true, SetupFunctionNames.uniformMssiSimpleSorterModels, ModelParameters(-1)));
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
            Assert.Equal(branch ? ResultsFunctionCatalog.functions.Keys : ResultsFunctionCatalog.functions.Where(entry => entry.Value.SourceDataTypes.IsEmpty).Select(entry => entry.Key), module.Editor!.ResultsFunctionOverride.Choices);
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
                run.Editor.SetupFunctionOverride.EffectiveValue = SetupFunctionNames.uniformMssiSimpleSorterModels;
            }
            WorkspaceRunExecutionTests.SupplyQuerySpans(page);
            await page.SaveWorkspaceAsync();
            Assert.Empty(page.Error);
            var saved = WorkspaceTestFiles.LoadRuntimeSnapshot(root).Modules[0].Projects[0].Runs[0];
            Assert.Equal(ResultsFunctionNames.mergeSortableTests, saved.ResultsFunctionName);
            if (branch) Assert.Equal(SetupFunctionNames.standardTests, saved.SetupFunctionName.Value);
            var reopened = new WorkspacePageViewModel(() => { }, _ => null, settings);
            var savedProject = reopened.Roots[0].Children[0].Children[0];
            Assert.False(savedProject.IsSealed);
            reopened.Selected = savedProject.Children[0]; await reopened.GenerateRunParametersAsync(); Assert.Empty(reopened.Error);
            Assert.True(savedProject.IsSealed);
            Assert.False(savedProject.Editor!.ResultsFunctionOverride.CanEditValue);
            Assert.Equal(ResultsFunctionNames.mergeSortableTests, savedProject.Editor.ResultsFunctionName);
            // Generated parameter files lock the ancestor module, including its default rows.
            var savedModule = reopened.Roots[0].Children[0];
            savedModule.Editor!.ResultsFunctionName = ResultsFunctionNames.prefixSortableTests;
            savedModule.Editor.ResultsParameterDefaults.Single(row => row.Key == "MergeLibId").RemoveCommand.Execute(null);
            Assert.True(savedModule.Editor.IsReadOnly);
            Assert.Equal(ResultsFunctionNames.mergeSortableTests, savedModule.Editor.ResultsFunctionName);
            await reopened.SaveWorkspaceAsync(); Assert.Empty(reopened.Error);
            var fixedRun = WorkspaceFileStore.LoadDefinition(root).Modules[0].Projects[0].Runs[0];
            Assert.Equal(ResultsFunctionNames.mergeSortableTests, fixedRun.ResultsFunctionName);
            Assert.Contains(fixedRun.ResultsParameterSpans, value => value.KeyName == "MergeLibId");
        }
        finally { temporary.Delete(recursive: true); }
    }
}
