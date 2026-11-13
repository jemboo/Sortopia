using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class EvaluationInputFunctionTests
{
    private static bool IsSetup(string group) => group != "sorter-eval-inputs.standard";
    private static bool HasSource(string group) => group is "sorter-eval.merge" or "sorter-eval.prefix";
    private static WorkspaceConfigurationViewModel Module(string group, string model)
    {
        var setup = IsSetup(group);
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: setup ? hierarchyKind.Branch : hierarchyKind.Root).Editor!;
        if (setup) editor.SetupFunctionName = $"{group}.{model}"; else editor.ResultsFunctionName = $"{group}.{model}";
        (setup ? editor.AddRequiredSetupDefaultsCommand : editor.AddRequiredResultsDefaultsCommand).Execute(null);
        var defaults = setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
        defaults.Single(row => row.Key == "SorterCount").Value.Text = "2";
        defaults.Single(row => row.Key == "StageLength").Value.Text = "3";
        return editor;
    }
    private static parameterSet Parameters(WorkspaceConfigurationViewModel editor, bool setup)
    {
        var settings = editor.ToProjectSettings();
        return parameterSet.fromBindings((setup ? settings.setupParameterDefaults : settings.resultsParameterDefaults).Select(ParameterBindingDto.toDomain));
    }
    private static functionData[] Source(string group, parameterSet parameters)
    {
        if (!HasSource(group)) return [];
        var name = group.EndsWith(".merge") ? ResultsFunctionNames.mergeSortableTests : ResultsFunctionNames.prefixSortableTests;
        var keys = FunctionCatalogs.requireResults(name).RequiredParameters.Select(key => key.Name).ToHashSet();
        return CatalogTestHost.Invoke(false, name, parameterSet.fromBindings(parameters.ParamMap.Values.Where(binding => keys.Contains(binding.KeyName)))).ToArray();
    }
    private static simpleSorterModel Simple(sorterModel model) => model switch {
        sorterModel.Simple value => value.Item, sorterModel.SimpleR value => value.Item.Model, sorterModel.SimpleS value => value.Item.Model,
        _ => throw new InvalidOperationException()
    };
    public static IEnumerable<object[]> Choices =>
        from prefix in new[] { "sorter-eval.standard", "sorter-eval.merge", "sorter-eval.prefix", "sorter-eval-inputs.standard" }
        from model in new[] { "msce", "mssi", "msrs", "msuf4", "msuf6" }
        from family in new[] { "Simple", "SimpleR", "SimpleS" }
        select new object[] { prefix, model, family };

    [Theory]
    [MemberData(nameof(Choices))]
    public void Catalog_entries_generate_the_fixed_model_and_keep_test_projections_and_prefixes_separate(string group, string model, string family)
    {
        var setup = IsSetup(group); var name = $"{group}.{model}";
        var editor = Module(group, model);
        var parameters = Parameters(editor, setup).WithBinding(ParameterBinding.create(ParamKeys.sorterModelTypeKey, family));
        var required = setup ? FunctionCatalogs.requireSetup(name).RequiredParameters : FunctionCatalogs.requireResults(name).RequiredParameters;
        var dataTypes = setup ? FunctionCatalogs.requireSetup(name).DataTypes : FunctionCatalogs.requireResults(name).DataTypes;
        var sourceTypes = setup ? FunctionCatalogs.requireSetup(name).SourceDataTypes : FunctionCatalogs.requireResults(name).SourceDataTypes;
        Assert.False(FunctionCatalogs.setupFunctionCatalog.ContainsKey(group));
        Assert.False(FunctionCatalogs.resultsFunctionCatalog.ContainsKey(group));
        Assert.Equal("1", setup ? FunctionCatalogs.requireSetup(name).Version : FunctionCatalogs.requireResults(name).Version);
        Assert.DoesNotContain(required, key => key.Name == "SimpleSorterModelType");
        Assert.Equal(model == "msce", required.Any(key => key.Name == "ExcludeSelfCe"));
        Assert.Equal(model == "msrs", required.Any(key => key.Name == "StageWeights"));
        Assert.Equal(model == "msuf4", required.Any(key => key.Name == "Seed4Weights"));
        Assert.Equal(model == "msuf6", required.Any(key => key.Name == "Seed6Weights"));
        Assert.Equal(model is "msuf4" or "msuf6", required.Any(key => key.Name == "UnfoldingWeights"));
        var testsType = sortopiaDataType.NewSortableTests(""); var modelsType = sortopiaDataType.NewSorterModelSet("");
        Assert.Equal(new[] { testsType, modelsType }.Order(), dataTypes.Keys.Order());
        Assert.Equal(HasSource(group) ? new[] { testsType } : [], sourceTypes);
        Assert.Equal(FunctionCatalogs.requireResults($"sorter-model-set.uniform.{model}").DataTypes[modelsType], dataTypes[modelsType]);
        var libraryKey = group.EndsWith(".merge") ? "MergeLibId" : group.EndsWith(".prefix") ? "PrefixLibId" : "ChannelCount";
        Assert.Equal(new[] { libraryKey, "SortableDataFormat" }.Order(), dataTypes[testsType].Order());
        Assert.Equal(model == "msuf6" ? 12 : 8, parameters.TryFind(ParamKeys.channelCountKey).Value);
        Assert.Empty(setup ? editor.SetupParameterDefaultsError : editor.ResultsParameterDefaultsError);

        var source = Source(group, parameters);
        var output = CatalogTestHost.Invoke(setup, name, parameters, source);
        Assert.Equal(2, output.Length);
        var tests = Assert.Single(output.OfType<functionData.SortableTests>());
        if (HasSource(group)) {
            var original = Assert.IsType<functionData.SortableTests>(source.Single());
            Assert.Same(original.Item1, tests.Item1); Assert.Same(original.Item2, tests.Item2); Assert.NotEmpty(tests.Item2);
        } else Assert.Empty(tests.Item2);
        var models = Assert.Single(output.OfType<functionData.SorterModelSet>()).Item;
        Assert.Equal(2, models.Count);
        Assert.All(models.SorterModels, value => {
            Assert.Empty(SorterModel.getPrefix(value));
            Assert.True(family switch { "Simple" => value.IsSimple, "SimpleR" => value.IsSimpleR, _ => value.IsSimpleS });
            var simple = Simple(value);
            Assert.True(model switch { "msce" => simple.IsMsce, "mssi" => simple.IsMssi, "msrs" => simple.IsMsrs, "msuf4" => simple.IsMsuf4, _ => simple.IsMsuf6 });
        });
        var repeated = Assert.Single(CatalogTestHost.Invoke(setup, name, parameters, source).OfType<functionData.SorterModelSet>()).Item;
        Assert.Equal(models.SorterModels.Select(SorterModel.getId), repeated.SorterModels.Select(SorterModel.getId));
    }

    [Theory]
    [InlineData("sorter-eval.standard", "msuf4")]
    [InlineData("sorter-eval.merge", "msuf4")]
    [InlineData("sorter-eval.prefix", "msuf4")]
    [InlineData("sorter-eval-inputs.standard", "msuf4")]
    [InlineData("sorter-eval.standard", "msuf6")]
    [InlineData("sorter-eval.merge", "msuf6")]
    [InlineData("sorter-eval.prefix", "msuf6")]
    [InlineData("sorter-eval-inputs.standard", "msuf6")]
    public void Named_weight_editors_control_every_generated_stage(string group, string model)
    {
        var setup = IsSetup(group); var editor = Module(group, model);
        var defaults = setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
        var seed = defaults.Single(row => row.Key == (model == "msuf4" ? "Seed4Weights" : "Seed6Weights")).Value;
        foreach (var mode in seed.Weights.Rows[0].Modes) mode.Text = mode.Name == (model == "msuf4" ? "SelfReflection" : "Para4") ? "1" : "0";
        var unfolding = defaults.Single(row => row.Key == "UnfoldingWeights").Value;
        foreach (var mode in unfolding.Weights.Rows.Single().Modes) mode.Text = mode.Name == "Para" ? "1" : "0";
        Assert.True(unfolding.IsWeights); Assert.False(unfolding.IsTextEditor);
        var parameters = Parameters(editor, setup);
        var models = Assert.Single(CatalogTestHost.Invoke(setup, $"{group}.{model}", parameters, Source(group, parameters)).OfType<functionData.SorterModelSet>()).Item;
        foreach (var value in models.SorterModels) {
            if (Simple(value) is simpleSorterModel.Msuf4 uf4)
                Assert.All(uf4.Item.TwoOrbitUnfolder4s, stage => {
                    Assert.True(stage.SeedTwoOrbitPairType.IsSelfRefl);
                    Assert.All(stage.TwoOrbitUnfolderSteps.Single().TwoOrbitPairTypes, pair => Assert.True(pair.IsPara));
                });
            else Assert.All(((simpleSorterModel.Msuf6)Simple(value)).Item.TwoOrbitUnfolder6s, stage => {
                Assert.True(stage.Seed6TwoOrbitType.IsPara4);
                Assert.All(stage.TwoOrbitUnfolderSteps.Single().TwoOrbitPairTypes, pair => Assert.True(pair.IsPara));
            });
        }
        unfolding.Weights.RemoveLevelCommand.Execute(null);
        Assert.Contains("needs 1 unfolding levels", setup ? editor.SetupParameterDefaultsError : editor.ResultsParameterDefaultsError);
        Assert.Contains("needs 1 unfolding levels", Assert.Throws<ArgumentException>(() =>
            CatalogTestHost.Invoke(setup, $"{group}.{model}", Parameters(editor, setup))).Message);
    }

    [Theory]
    [InlineData("sorter-eval.merge")]
    [InlineData("sorter-eval.prefix")]
    public void Source_width_validation_checks_partial_defaults_and_every_span_value_before_source_reads(string group)
    {
        var editor = Module(group, "mssi");
        var parameters = Parameters(editor, true).WithBinding(ParameterBinding.create(ParamKeys.channelCountKey, 12));
        var key = group.EndsWith(".merge") ? "MergeLibId" : "PrefixLibId";
        Assert.Contains($"must match {key}", Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true, $"{group}.mssi", parameters)).Message);
        editor.SetupParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "12";
        Assert.Contains($"must match {key}", editor.SetupParameterDefaultsError);
        var definition = FunctionCatalogs.requireSetup($"{group}.mssi");
        var defaults = ListModule.OfSeq(parameters.ParamMap.Values.Where(binding => binding.KeyName != "ChannelCount"));
        var span = ParameterSpan.create(ParamKeys.channelCountKey, ListModule.OfSeq(Enumerable.Repeat(8, 50).Append(12)));
        Assert.Contains($"must match {key}", definition.InputError.Invoke(defaults).Invoke(ListModule.OfArray(new[] { span })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inherited_weight_defaults_and_explicit_spans_use_the_same_typed_editors(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.SetupFunctionName = $"sorter-eval.standard.{(setup ? "msuf6" : "mssi")}";
        module.Editor.ResultsFunctionName = $"sorter-eval-inputs.standard.{(setup ? "mssi" : "msuf6")}";
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
