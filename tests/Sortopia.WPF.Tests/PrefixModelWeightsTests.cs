using System.IO;
using Microsoft.FSharp.Collections;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Permutations;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class PrefixModelWeightsTests
{
    private static string Function(string model) => $"sorter-model-set.prefix.{model}";
    private static WorkspaceConfigurationViewModel Module(string model)
    {
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models").Editor!;
        editor.ResultsFunctionName = Function(model); editor.AddRequiredResultsDefaultsCommand.Execute(null);
        editor.ResultsParameterDefaults.Single(row => row.Key == "SorterCount").Value.Text = "2";
        editor.ResultsParameterDefaults.Single(row => row.Key == "StageLength").Value.Text = "3";
        return editor;
    }
    private static parameterSet Parameters(WorkspaceConfigurationViewModel editor) =>
        parameterSet.fromBindings(editor.ToProjectSettings().resultsParameterDefaults.Select(ParameterBindingDto.toDomain));

    [Theory]
    [InlineData("msce", "ExcludeSelfCe")]
    [InlineData("mssi", "")]
    [InlineData("msrs", "StageWeights")]
    [InlineData("msuf4", "Seed4Weights,UnfoldingWeights")]
    [InlineData("msuf6", "Seed6Weights,UnfoldingWeights")]
    public void Each_function_exposes_only_its_algorithm_inputs_with_valid_initial_values(string model, string extras)
    {
        var editor = Module(model); var definition = FunctionCatalogs.requireResults(Function(model));
        string[] common = ["SorterModelType", "PrefixLibId", "AMax", "ActivationProbability", "RngType", "StageLength", "SorterCount", "Repl"];
        Assert.Equal(common.Concat(extras.Split(',', StringSplitOptions.RemoveEmptyEntries)).Order(), definition.RequiredParameters.Select(key => key.Name).Order());
        Assert.False(FunctionCatalogs.resultsFunctionCatalog.ContainsKey("sorter-model-set.prefix"));
        Assert.Equal("1", definition.Version); Assert.Empty(editor.ResultsParameterDefaultsError);
        var parameters = Parameters(editor); FunctionValidation.validate(definition, parameters);
        var models = Assert.IsType<functionData.SorterModelSet>(Assert.Single(CatalogTestHost.Invoke(false, Function(model), parameters))).Item;
        Assert.Equal(2, models.Count);
        if (model == "msuf6") Assert.Equal(12, parameters.TryFind(ParamKeys.prefixLibIdKey).Value.ChannelCount);
    }

    [Theory]
    [InlineData("msuf4")]
    [InlineData("msuf6")]
    public void Named_seed_and_per_level_modes_reach_every_generated_stage(string model)
    {
        var editor = Module(model);
        var prefix = editor.ResultsParameterDefaults.Single(row => row.Key == "PrefixLibId");
        prefix.Value.Text = PrefixLibId.toString(prefixLibId.create(model == "msuf4" ? 16 : 24, 1, prefixLibVariant.Prefix1));
        var seed = editor.ResultsParameterDefaults.Single(row => row.Key == (model == "msuf4" ? "Seed4Weights" : "Seed6Weights")).Value;
        Assert.True(seed.IsWeights); Assert.False(seed.IsTextEditor);
        foreach (var mode in seed.Weights.Rows[0].Modes) mode.Text = mode.Name == (model == "msuf4" ? "SelfReflection" : "Para4") ? "7" : "0";
        var unfolding = editor.ResultsParameterDefaults.Single(row => row.Key == "UnfoldingWeights").Value;
        unfolding.Text = "0,3,0;9,0,0";
        Assert.Empty(editor.ResultsParameterDefaultsError);
        var values = Parameters(editor);
        Assert.Equal(2, values.TryFind(ParamKeys.unfoldingWeightsKey).Value.Levels.Length);
        var set = Assert.IsType<functionData.SorterModelSet>(Assert.Single(CatalogTestHost.Invoke(false, Function(model), values))).Item;
        foreach (var value in set.SorterModels)
        {
            var simple = Assert.IsType<sorterModel.Simple>(value).Item;
            if (simple is simpleSorterModel.Msuf4 uf4)
                Assert.All(uf4.Item.TwoOrbitUnfolder4s, stage => {
                    Assert.True(stage.SeedTwoOrbitPairType.IsSelfRefl);
                    Assert.All(stage.TwoOrbitUnfolderSteps[0].TwoOrbitPairTypes, pair => Assert.True(pair.IsPara));
                    Assert.All(stage.TwoOrbitUnfolderSteps[1].TwoOrbitPairTypes, pair => Assert.True(pair.IsOrtho));
                });
            else if (simple is simpleSorterModel.Msuf6 uf6)
                Assert.All(uf6.Item.TwoOrbitUnfolder6s, stage => {
                    Assert.True(stage.Seed6TwoOrbitType.IsPara4);
                    Assert.All(stage.TwoOrbitUnfolderSteps[0].TwoOrbitPairTypes, pair => Assert.True(pair.IsPara));
                    Assert.All(stage.TwoOrbitUnfolderSteps[1].TwoOrbitPairTypes, pair => Assert.True(pair.IsOrtho));
                });
            else Assert.Fail("Wrong model algorithm.");
        }
        unfolding.Weights.RemoveLevelCommand.Execute(null);
        Assert.Contains("needs 2 unfolding levels", editor.ResultsParameterDefaultsError);
        unfolding.Weights.AddLevelCommand.Execute(null);
        Assert.Empty(editor.ResultsParameterDefaultsError);
        foreach (var mode in unfolding.Weights.Rows[1].Modes) mode.Text = "0";
        Assert.NotEmpty(unfolding.Error);
        unfolding.Weights.Rows[1].Modes[0].Text = "NaN"; Assert.NotEmpty(unfolding.Error);
        unfolding.Weights.Rows[1].Modes[0].Text = "1"; Assert.Empty(unfolding.Error);
    }

    [Theory]
    [InlineData(parameterValueType.OpsWeights, "0,0,0")]
    [InlineData(parameterValueType.OpsWeights, "1,-1,1")]
    [InlineData(parameterValueType.OpsWeights, "1,NaN,1")]
    [InlineData(parameterValueType.Seed6Weights, "1,1,1")]
    [InlineData(parameterValueType.Seed6Weights, "1,1,1,1,Infinity,1,1")]
    [InlineData(parameterValueType.UnfoldingWeights, "1,0,0;0,0,0")]
    public void Invalid_weight_values_are_rejected_at_the_typed_parameter_boundary(parameterValueType type, string text)
    {
        Assert.NotEmpty(ParameterValue.validationError(type, text));
        Assert.Throws<ArgumentException>(() => ParameterBinding.fromText("CustomWeights", type, text));
    }

    [Fact]
    public void Weight_records_remain_typed_and_have_canonical_query_identifiers()
    {
        var a = ParameterBinding.fromText("UnfoldingWeights", parameterValueType.UnfoldingWeights, "1.0,0,-0;0,2e0,0");
        var b = ParameterBinding.create(ParamKeys.unfoldingWeightsKey, new unfoldingWeights([new opsWeights(1, 0, 0), new opsWeights(0, 2, 0)]));
        Assert.Equal(a, b); Assert.Equal("1,0,0;0,2,0", a.FormattedValue);
        var value = parameterSet.fromBindings([a]);
        Assert.IsType<parameterValue.UnfoldingWeightsValue>(value.ParamMap["UnfoldingWeights"].Value);
        var decoded = ParameterBindingDto.toDomain(ParameterBindingDto.fromDomain(a)); Assert.Equal(a, decoded);
        var id = Guid.NewGuid(); var kind = sortopiaDataType.NewSorterModelSet("");
        dataKey Key(parameterBinding binding) => DataKey.create(id, "Results", false, Function("msuf4"), "1", kind,
            ["UnfoldingWeights"], parameterSet.fromBindings([binding]), []);
        Assert.Equal(Key(a).Reference.Id, Key(b).Reference.Id);
        Assert.NotEqual(Key(a).Reference.Id, Key(ParameterBinding.create(ParamKeys.unfoldingWeightsKey, new unfoldingWeights([new opsWeights(0, 1, 0), new opsWeights(0, 2, 0)]))).Reference.Id);
    }

    [Theory]
    [InlineData("msuf4")]
    [InlineData("msuf6")]
    public async Task Weight_spans_survive_portable_run_files_execute_and_reuse_outputs(string model)
    {
        var temp = Directory.CreateTempSubdirectory("Sortopia-model-weights-");
        WorkspacePageViewModel? page = null;
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(temp.FullName, "Workspace")).FullName;
            var settings = new WorkspaceFolderSettings(Path.Combine(temp.FullName, "settings.txt")); settings.Save(root);
            page = new WorkspacePageViewModel(() => { }, _ => null, settings);
            page.AddModuleCommand.Execute(null); var editor = page.Selected!.Editor!;
            editor.ResultsFunctionName = Function(model); editor.AddRequiredResultsDefaultsCommand.Execute(null);
            editor.ResultsParameterDefaults.Single(row => row.Key == "SorterCount").Value.Text = "2";
            editor.ResultsParameterDefaults.Single(row => row.Key == "UnfoldingWeights").RemoveCommand.Execute(null);
            page.AddProjectCommand.Execute(null); var projectId = page.Selected!.Id;
            page.AddRunCommand.Execute(null); var run = page.Selected!;
            run.Editor!.AddMissingResultsParametersCommand.Execute(null);
            var span = Assert.Single(run.Editor.ResultsParameters); Assert.Equal("UnfoldingWeights", span.Key);
            foreach (var text in new[] { "0,1,0", "1,0,0" }) { span.AddValueCommand.Execute(null); span.Values.Last().Text = text; }
            Assert.All(span.Values, value => Assert.True(value.IsWeights));
            Assert.Equal(2, run.Editor.ResultsParameterSetsPreview.Rows.Length);
            run.Editor.ResultsQueryOutputs.Single().Keys.Single().Selected = true;
            span.AddValueCommand.Execute(null); span.Values.Last().Text = "1,1,1;1,1,1";
            Assert.Contains("needs 1 unfolding", run.Editor.ResultsParametersError); Assert.Empty(run.Editor.ResultsParameterSetsPreview.Rows);
            span.Values.Last().RemoveCommand.Execute(null);
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
            page.Selected = run;
            await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
            Assert.False(string.IsNullOrEmpty(page.ResultsExecution.Folder));
            var source = Path.GetDirectoryName(page.ResultsExecution.Folder)!;
            var copy = Directory.CreateDirectory(Path.Combine(temp.FullName, "Copied project")).FullName;
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) {
                var target = Path.Combine(copy, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            var folder = Path.Combine(copy, "runParameters");
            var report = RunParameterExecution.executeFolder(folder, null!, null!, TestContext.Current.CancellationToken);
            Assert.True(report.Status == runExecutionStatus.Completed, report.Error); Assert.Equal(2, report.Results.Length);
            ISortopiaDb db = new SortopiaFileDb(copy, projectId);
            foreach (var call in report.Results) {
                var weights = call.Parameters.TryFind(ParamKeys.unfoldingWeightsKey).Value;
                var output = Assert.Single(call.StoredOutputs); Assert.False(output.Reused);
                var saved = db.Load(output.Reference, TestContext.Current.CancellationToken); Assert.True(saved.IsOk);
                var models = Assert.IsType<functionData.SorterModelSet>(saved.ResultValue.Value).Item;
                foreach (var item in models.SorterModels) {
                    var simple = Assert.IsType<sorterModel.Simple>(item).Item;
                    var pairs = simple is simpleSorterModel.Msuf4 uf4
                        ? uf4.Item.TwoOrbitUnfolder4s.SelectMany(stage => stage.TwoOrbitUnfolderSteps[0].TwoOrbitPairTypes)
                        : ((simpleSorterModel.Msuf6)simple).Item.TwoOrbitUnfolder6s.SelectMany(stage => stage.TwoOrbitUnfolderSteps[0].TwoOrbitPairTypes);
                    Assert.All(pairs, pair => Assert.Equal(weights.Levels[0].Ortho == 1, pair.IsOrtho));
                }
            }
            Assert.Equal(2, Directory.GetFiles(Path.Combine(copy, "data", "results", "SorterModelSet"), "*.msgpack").Length);
            var repeated = RunParameterExecution.executeFolder(folder, null!, null!, TestContext.Current.CancellationToken);
            Assert.True(repeated.Status == runExecutionStatus.Completed, repeated.Error);
            Assert.All(repeated.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        }
        finally { page?.Cancel(); temp.Delete(true); }
    }
}
