using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Evaluation;
using Sortopia.FileDb;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Networks;
using Sortopia.Sorting.Inputs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class EvaluationInputFunctionTests
{
    private static bool IsSetup(string group) => group == "setup";
    private static string Function(string group, string model, string family = "Simple") => $"sorter-eval-inputs.standard.{model}" + ModelFamilyResultsTests.Suffix(family);
    private static WorkspaceConfigurationViewModel Module(string group, string model, string family = "Simple")
    {
        var setup = IsSetup(group);
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: setup ? hierarchyKind.Branch : hierarchyKind.Root).Editor!;
        if (setup) editor.SetupFunctionName = Function(group, model, family); else editor.ResultsFunctionName = Function(group, model, family);
        (setup ? editor.AddRequiredSetupDefaultsCommand : editor.AddRequiredResultsDefaultsCommand).Execute(null);
        var defaults = setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
        defaults.Single(row => row.Key == "SorterCount").Value.Text = "2";
        defaults.Single(row => row.Key == "SorterSuccessFraction").Value.Text = "50%";
        return editor;
    }
    private static parameterSet Parameters(WorkspaceConfigurationViewModel editor, bool setup)
    {
        var settings = editor.ToProjectSettings();
        return parameterSet.fromBindings((setup ? settings.setupParameterDefaults : settings.resultsParameterDefaults).Select(ParameterBindingDto.toDomain));
    }
    private static simpleSorterModel Simple(sorterModel model) => model switch {
        sorterModel.Simple value => value.Item, sorterModel.SimpleR value => value.Item.Model, sorterModel.SimpleS value => value.Item.Model,
        _ => throw new InvalidOperationException()
    };
    public static IEnumerable<object[]> Choices =>
        from prefix in new[] { "setup", "results" }
        from model in new[] { "msce", "mssi", "msrs", "msuf4", "msuf6" }
        from family in new[] { "Simple", "SimpleR", "SimpleS" }
        select new object[] { prefix, model, family };

    [Theory]
    [MemberData(nameof(Choices))]
    public void Catalog_entries_generate_the_fixed_model_and_keep_test_projections_and_prefixes_separate(string group, string model, string family)
    {
        var setup = IsSetup(group); var name = Function(group, model, family);
        var editor = Module(group, model, family);
        var parameters = Parameters(editor, setup);
        var required = setup ? SetupFunctionCatalog.require(name).RequiredParameters : ResultsFunctionCatalog.require(name).RequiredParameters;
        var dataTypes = setup ? SetupFunctionCatalog.require(name).DataTypes : ResultsFunctionCatalog.require(name).DataTypes;
        var sourceTypes = setup ? SetupFunctionCatalog.require(name).SourceDataTypes : ResultsFunctionCatalog.require(name).SourceDataTypes;
        Assert.False(SetupFunctionCatalog.functions.ContainsKey(group));
        Assert.False(ResultsFunctionCatalog.functions.ContainsKey(group));
        Assert.Equal((model == "msuf4" ? 7 : 2).ToString(), setup ? SetupFunctionCatalog.require(name).Version : ResultsFunctionCatalog.require(name).Version);
        Assert.DoesNotContain(required, key => key.Name == "SimpleSorterModelType");
        Assert.Equal(model == "msce", required.Any(key => key.Name == "ExcludeSelfCe"));
        Assert.Equal(model == "msrs", required.Any(key => key.Name == "StageWeights"));
        Assert.DoesNotContain(required, key => key.Name == "Seed4Weights");
        foreach (var rate in new[] { "OrthoGenRate", "ParaGenRate", "SelfSymGenRate" }) Assert.Equal(model == "msuf4", required.Any(key => key.Name == rate));
        Assert.Equal(model == "msuf6", required.Any(key => key.Name == "Seed6Weights"));
        Assert.Equal(model == "msuf6", required.Any(key => key.Name == "UnfoldingWeights"));
        var testsType = sortopiaDataType.NewSortableTests(""); var modelsType = sortopiaDataType.NewSorterModelSet("");
        Assert.Equal(new[] { testsType, modelsType }.Order(), dataTypes.Keys.Order());
        Assert.Empty(sourceTypes);
        if (setup) Assert.Empty(SetupFunctionCatalog.require(name).OutputDependencies[modelsType]);
        Assert.Equal(setup ? SetupFunctionCatalog.require($"sorter-model-set.uniform.{model}{ModelFamilyResultsTests.Suffix(family)}").DataTypes[modelsType]
            : ResultsFunctionCatalog.require($"sorter-model-set.uniform.{model}{ModelFamilyResultsTests.Suffix(family)}").DataTypes[modelsType], dataTypes[modelsType]);
        Assert.Equal(new[] { "ChannelCount", "SortableDataFormat" }.Order(), dataTypes[testsType].Order());
        Assert.Equal(model == "msuf6" ? 12 : 8, parameters.TryFind(ParamKeys.channelCountKey).Value);
        Assert.Empty(setup ? editor.SetupParameterDefaultsError : editor.ResultsParameterDefaultsError);

        var output = CatalogTestHost.Invoke(setup, name, parameters);
        Assert.Equal(2, output.Length);
        var tests = Assert.Single(output.OfType<sortopiaStorageData.SortableTests>());
        Assert.Equal(parameters.TryFind(ParamKeys.channelCountKey).Value, SortableTests.getChannelCount(tests.Item));
        var models = Assert.Single(output.OfType<sortopiaStorageData.SorterModelSet>()).Item;
        Assert.Equal(2, models.Count);
        Assert.All(models.SorterModels, value => {
            Assert.Empty(SorterModel.getPrefix(value));
            Assert.True(family switch { "Simple" => value.IsSimple, "SimpleR" => value.IsSimpleR, _ => value.IsSimpleS });
            var simple = Simple(value);
            Assert.True(model switch { "msce" => simple.IsMsce, "mssi" => simple.IsMssi, "msrs" => simple.IsMsrs, "msuf4" => simple.IsMsuf4, _ => simple.IsMsuf6 });
        });
        var repeated = Assert.Single(CatalogTestHost.Invoke(setup, name, parameters).OfType<sortopiaStorageData.SorterModelSet>()).Item;
        Assert.Equal(models.SorterModels.Select(SorterModel.getId), repeated.SorterModels.Select(SorterModel.getId));
    }

    [Theory]
    [InlineData("setup", "msuf6")]
    [InlineData("results", "msuf6")]
    public void Named_weight_editors_control_every_generated_stage(string group, string model)
    {
        var setup = IsSetup(group); var editor = Module(group, model);
        var defaults = setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
        var seed = defaults.Single(row => row.Key == "Seed6Weights").Value;
        foreach (var mode in seed.Weights.Rows[0].Modes) mode.Text = mode.Name == "Para4" ? "1" : "0";
        var unfolding = defaults.Single(row => row.Key == "UnfoldingWeights").Value;
        foreach (var mode in unfolding.Weights.Rows.Single().Modes) mode.Text = mode.Name == "Para" ? "1" : "0";
        Assert.True(unfolding.IsWeights); Assert.False(unfolding.IsTextEditor);
        var parameters = Parameters(editor, setup);
        var models = Assert.Single(CatalogTestHost.Invoke(setup, Function(group, model), parameters).OfType<sortopiaStorageData.SorterModelSet>()).Item;
        foreach (var value in models.SorterModels) {
            Assert.All(((simpleSorterModel.Msuf6)Simple(value)).Item.TwoOrbitUnfolder6s, stage => {
                Assert.True(stage.Seed6TwoOrbitType.IsPara4);
                Assert.All(stage.TwoOrbitUnfolderSteps.Single().TwoOrbitPairTypes, pair => Assert.True(pair.IsPara));
            });
        }
        unfolding.Weights.RemoveLevelCommand.Execute(null);
        Assert.Contains("needs 1 unfolding levels", setup ? editor.SetupParameterDefaultsError : editor.ResultsParameterDefaultsError);
        Assert.Contains("needs 1 unfolding levels", Assert.Throws<ArgumentException>(() =>
            CatalogTestHost.Invoke(setup, Function(group, model), Parameters(editor, setup))).Message);
    }

    [Theory]
    [InlineData(false, "matching")]
    [InlineData(false, "different")]
    [InlineData(false, "empty")]
    [InlineData(true, "matching")]
    [InlineData(true, "different")]
    [InlineData(true, "empty")]
    public void Evaluation_skips_the_model_prefix_on_prepared_tests(bool mutants, string prefixKind)
    {
        var parameters = Parameters(Module("setup", "msuf4", "SimpleS"), true)
            .WithBinding(ParameterBinding.create(ParamKeys.activationProbabilityKey, 0.0))
            .WithBinding(ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Ints"));
        // Filtering [1,0,0,1,...] through (1,2),(0,1) produced this input.
        // It carries no prefix metadata; the model prefix is assumed already applied.
        var source = sortopiaStorageData.NewSortableTests(sortableTests.NewInts(sortableIntTests.create(Guid.NewGuid(), 8,
            [sortableIntArray.create([0, 1, 0, 1, 1, 1, 1, 1], 8, 2)])));
        ce[] prefix = prefixKind switch {
            "matching" => [ce.create(1, 2), ce.create(0, 1)],
            "different" => [ce.create(0, 1)],
            _ => []
        };
        var generated = Assert.Single(CatalogTestHost.Invoke(true, Function("setup", "msuf4", "SimpleS"), parameters).OfType<sortopiaStorageData.SorterModelSet>()).Item;
        var prefixed = generated.SorterModels.Cast<sorterModel.SimpleS>().Select(value => sorterModel.NewSimpleS(
            simpleSorterModelS.create(SimpleSorterModel.withPrefix(prefix, value.Item.Model), value.Item.Activators))).ToArray();
        var modelsInput = sortopiaStorageData.NewSorterModelSet(sorterModelSet.create(Guid.NewGuid(), prefixed, generated.RawCeLength));
        var outputs = CatalogTestHost.Invoke(true, SetupFunctionNames.evaluationSource, parameterSet.fromBindings([]), modelsInput, source);
        var tests = Assert.Single(outputs.OfType<sortopiaStorageData.SortableTests>());
        Assert.Same(source, tests);
        var evaluationParameters = parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.sorterEvalTypeKey, "V2"),
            .. mutants ? Array.Empty<parameterBinding>() : new[] {
                ParameterBinding.create(ParamKeys.collectSortableTestsKey, false) }]);
        var name = mutants ? ResultsFunctionNames.mutantEval : ResultsFunctionNames.sorterEval;
        var result = Assert.Single(CatalogTestHost.Invoke(false, name, evaluationParameters, outputs.ToArray()).OfType<sortopiaStorageData.SorterSetEval>());
        Assert.All(result.Item.SorterEvals, evaluation => {
            var uses = SorterEval.getCeUseArray(evaluation);
            Assert.Equal(prefix, uses.Select(value => value.Ce));
            Assert.All(uses, value => Assert.Equal(-1, value.UseCount));
            Assert.Equal(prefix.Length, SorterEval.getCeLength(evaluation));
            Assert.Equal(prefix.Length, SorterEval.getStageLength(evaluation));
            Assert.Equal(1, SorterEval.getUnsortedCount(evaluation));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inherited_weight_defaults_and_explicit_spans_use_the_same_typed_editors(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.SetupFunctionName = $"sorter-eval-inputs.standard.{(setup ? "msuf6" : "mssi")}.simple";
        module.Editor.ResultsFunctionName = $"sorter-eval-inputs.standard.{(setup ? "mssi" : "msuf6")}.simple";
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null); module.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "UnfoldingWeights", setup, "1,0,0", "0,1,0");
        Assert.All(span.Values, value => Assert.True(value.IsWeights));
        Assert.Empty(setup ? run.Editor!.SetupParametersError : run.Editor!.ResultsParametersError);
        Assert.Equal(2, (setup ? run.Editor.SetupParameterSetsPreview : run.Editor.ResultsParameterSetsPreview).Rows.Count());
        Assert.Contains(setup ? run.Editor.InheritedSetupDefaults : run.Editor.InheritedOutputDefaults, row => row.Key == "Seed6Weights");
        Assert.DoesNotContain(setup ? run.Editor.SetupParameters : run.Editor.ResultsParameters, row => row.Key == "Seed6Weights");
        span.Values[1].Weights.AddLevelCommand.Execute(null);
        Assert.Contains("needs 1 unfolding levels", setup ? run.Editor.SetupParametersError : run.Editor.ResultsParametersError);
        Assert.Contains("separate output projections", setup ? run.Editor.SetupInputGuidance : run.Editor.ResultsInputGuidance);
    }
}
