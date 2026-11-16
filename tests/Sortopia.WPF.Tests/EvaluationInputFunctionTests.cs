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
    private static bool IsSetup(string group) => group != "sorter-eval-inputs.standard";
    private static bool HasSource(string group) => group is "sorter-eval.merge" or "sorter-eval.prefix";
    private static string Function(string group, string model, string family = "Simple") => $"{group}.{model}" + ModelFamilyResultsTests.Suffix(family);
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
    private static functionData[] Source(string group, parameterSet parameters)
    {
        if (!HasSource(group)) return [];
        var name = group.EndsWith(".merge") ? ResultsFunctionNames.mergeSortableTests : ResultsFunctionNames.prefixSortableTests;
        var keys = ResultsFunctionCatalog.require(name).RequiredParameters.Select(key => key.Name).ToHashSet();
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
        var setup = IsSetup(group); var name = Function(group, model, family);
        var editor = Module(group, model, family);
        var parameters = Parameters(editor, setup);
        var required = setup ? SetupFunctionCatalog.require(name).RequiredParameters : ResultsFunctionCatalog.require(name).RequiredParameters;
        var dataTypes = setup ? SetupFunctionCatalog.require(name).DataTypes : ResultsFunctionCatalog.require(name).DataTypes;
        var sourceTypes = setup ? SetupFunctionCatalog.require(name).SourceDataTypes : ResultsFunctionCatalog.require(name).SourceDataTypes;
        Assert.False(SetupFunctionCatalog.functions.ContainsKey(group));
        Assert.False(ResultsFunctionCatalog.functions.ContainsKey(group));
        Assert.Equal(((model == "msuf4" ? 3 : 2) + (HasSource(group) ? 1 : 0)).ToString(), setup ? SetupFunctionCatalog.require(name).Version : ResultsFunctionCatalog.require(name).Version);
        Assert.DoesNotContain(required, key => key.Name == "SimpleSorterModelType");
        Assert.Equal(model == "msce", required.Any(key => key.Name == "ExcludeSelfCe"));
        Assert.Equal(model == "msrs", required.Any(key => key.Name == "StageWeights"));
        Assert.DoesNotContain(required, key => key.Name == "Seed4Weights");
        foreach (var rate in new[] { "OrthoRate", "ParaRate", "SelfSymRate" }) Assert.Equal(model == "msuf4", required.Any(key => key.Name == rate));
        Assert.Equal(model == "msuf6", required.Any(key => key.Name == "Seed6Weights"));
        Assert.Equal(model == "msuf6", required.Any(key => key.Name == "UnfoldingWeights"));
        var testsType = sortopiaDataType.NewSortableTests(""); var modelsType = sortopiaDataType.NewSorterModelSet("");
        Assert.Equal(new[] { testsType, modelsType }.Order(), dataTypes.Keys.Order());
        Assert.Equal(HasSource(group) ? new[] { testsType } : [], sourceTypes);
        if (setup) Assert.Equal(HasSource(group) ? new[] { testsType } : [], SetupFunctionCatalog.require(name).OutputDependencies[modelsType]);
        Assert.Equal(setup ? SetupFunctionCatalog.require($"sorter-model-set.uniform.{model}{ModelFamilyResultsTests.Suffix(family)}").DataTypes[modelsType]
            : ResultsFunctionCatalog.require($"sorter-model-set.uniform.{model}{ModelFamilyResultsTests.Suffix(family)}").DataTypes[modelsType], dataTypes[modelsType]);
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
            Assert.Equal(tests.Item2, SorterModel.getPrefix(value));
            Assert.True(family switch { "Simple" => value.IsSimple, "SimpleR" => value.IsSimpleR, _ => value.IsSimpleS });
            var simple = Simple(value);
            Assert.True(model switch { "msce" => simple.IsMsce, "mssi" => simple.IsMssi, "msrs" => simple.IsMsrs, "msuf4" => simple.IsMsuf4, _ => simple.IsMsuf6 });
        });
        var repeated = Assert.Single(CatalogTestHost.Invoke(setup, name, parameters, source).OfType<functionData.SorterModelSet>()).Item;
        Assert.Equal(models.SorterModels.Select(SorterModel.getId), repeated.SorterModels.Select(SorterModel.getId));
    }

    [Theory]
    [InlineData("sorter-eval.standard", "msuf6")]
    [InlineData("sorter-eval.merge", "msuf6")]
    [InlineData("sorter-eval.prefix", "msuf6")]
    [InlineData("sorter-eval-inputs.standard", "msuf6")]
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
        var models = Assert.Single(CatalogTestHost.Invoke(setup, Function(group, model), parameters, Source(group, parameters)).OfType<functionData.SorterModelSet>()).Item;
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
    [InlineData("Simple")]
    [InlineData("SimpleR")]
    [InlineData("SimpleS")]
    public void Setup_uses_the_actual_source_prefix_for_models_and_suffix_capacity(string family)
    {
        var editor = Module("sorter-eval.prefix", "msuf4", family);
        var parameters = Parameters(editor, true);
        // The source carries a deeper prefix than the lookup parameter's library entry.
        var sourceParameters = parameters.WithBinding(ParameterBinding.create(ParamKeys.prefixLibIdKey,
            prefixLibId.create(8, 3, prefixLibVariant.PrefixA)));
        var source = Assert.IsType<functionData.SortableTests>(Assert.Single(Source("sorter-eval.prefix", sourceParameters)));
        var output = CatalogTestHost.Invoke(true, Function("sorter-eval.prefix", "msuf4", family), parameters, source);
        var models = Assert.Single(output.OfType<functionData.SorterModelSet>()).Item;
        Assert.Equal(3, ModelGenerationStageLengths.prefixDepth(8, source.Item2));
        Assert.All(models.SorterModels, model => {
            Assert.Equal(source.Item2, SorterModel.getPrefix(model));
            Assert.Equal(18, SimpleSorterModel.getStageLength(Simple(model)));
            if (model is sorterModel.SimpleS simpleS) Assert.Equal(18, simpleS.Item.Activators.Length);
            if (model is sorterModel.SimpleR simpleR) Assert.Equal(18, simpleR.Item.Activators.Length);
        });
        Assert.Equal(20, ModelGenerationStageLengths.tryForFunction(Function("sorter-eval.prefix", "msuf4", family), parameters).Value);
        Assert.Contains("actual source prefix", editor.SetupInputGuidance);
    }

    [Theory]
    [InlineData("sorter-eval.prefix")]
    [InlineData("sorter-eval.merge")]
    public void Changing_source_identity_changes_the_model_cache_key(string group)
    {
        var name = Function(group, "msuf4", "SimpleS");
        var definition = SetupFunctionCatalog.require(name);
        var parameters = Parameters(Module(group, "msuf4", "SimpleS"), true);
        var project = Guid.NewGuid();
        var source = new storedDataReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, sortopiaDataType.NewSortableTests(""));
        var changed = new storedDataReference(source.ProjectId, Guid.NewGuid(), source.QueryId, false, source.DataType);
        var first = OutputPlan.create(project, "Setup", true, name, definition.Version, definition.DataTypes, definition.OutputDependencies, parameters, [source]);
        var second = OutputPlan.create(project, "Setup", true, name, definition.Version, definition.DataTypes, definition.OutputDependencies, parameters, [changed]);
        foreach (var type in definition.SortopiaDataTypes)
        {
            Assert.Equal(new[] { source }, first[type].Dependencies);
            Assert.NotEqual(first[type].Key.Id, second[type].Key.Id);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluation_applies_the_suffix_and_reports_the_source_prefix_once(bool mutants)
    {
        var parameters = Parameters(Module("sorter-eval.prefix", "msuf4", "SimpleS"), true)
            .WithBinding(ParameterBinding.create(ParamKeys.activationProbabilityKey, 0.0))
            .WithBinding(ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Ints"));
        // This prefix is not idempotent: [1,0,0,1,...] becomes [0,1,0,1,...].
        // Applying it a second time would incorrectly sort the remaining inversion.
        var source = functionData.NewSortableTests(sortableTests.NewInts(sortableIntTests.create(Guid.NewGuid(), 8,
            [sortableIntArray.create([0, 1, 0, 1, 1, 1, 1, 1], 8, 2)])), [ce.create(1, 2), ce.create(0, 1)]);
        var outputs = CatalogTestHost.Invoke(true, Function("sorter-eval.prefix", "msuf4", "SimpleS"), parameters, source);
        var tests = Assert.Single(outputs.OfType<functionData.SortableTests>());
        var evaluationParameters = parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.sorterEvalTypeKey, "V2"),
            .. mutants ? Array.Empty<parameterBinding>() : new[] {
                ParameterBinding.create(ParamKeys.channelCountKey, 8), ParameterBinding.create(ParamKeys.collectSortableTestsKey, false) }]);
        var name = mutants ? ResultsFunctionNames.mutantEval : ResultsFunctionNames.sorterEval;
        var result = Assert.Single(CatalogTestHost.Invoke(false, name, evaluationParameters, outputs.ToArray()).OfType<functionData.SorterSetEval>());
        Assert.All(result.Item.SorterEvals, evaluation => {
            var uses = SorterEval.getCeUseArray(evaluation);
            Assert.Equal(tests.Item2, uses.Select(value => value.Ce));
            Assert.All(uses, value => Assert.Equal(-1, value.UseCount));
            Assert.Equal(1, SorterEval.getUnsortedCount(evaluation));
        });
        var models = outputs.Single(value => value.IsSorterModelSet);
        var differentPrefix = functionData.NewSortableTests(tests.Item1, tests.Item2.Reverse().ToArray());
        Assert.Contains("model prefix must match", Assert.Throws<ArgumentException>(() =>
            CatalogTestHost.Invoke(false, name, evaluationParameters, models, differentPrefix)).Message);
    }

    [Theory]
    [InlineData("sorter-eval.merge")]
    [InlineData("sorter-eval.prefix")]
    public void Source_width_validation_checks_partial_defaults_and_every_span_value_before_source_reads(string group)
    {
        var editor = Module(group, "mssi");
        var parameters = Parameters(editor, true).WithBinding(ParameterBinding.create(ParamKeys.channelCountKey, 12));
        var key = group.EndsWith(".merge") ? "MergeLibId" : "PrefixLibId";
        Assert.Contains($"must match {key}", Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true, Function(group, "mssi"), parameters)).Message);
        editor.SetupParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "12";
        Assert.Contains($"must match {key}", editor.SetupParameterDefaultsError);
        var definition = SetupFunctionCatalog.require(Function(group, "mssi"));
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
        module.Editor!.SetupFunctionName = $"sorter-eval.standard.{(setup ? "msuf6" : "mssi")}.simple";
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
