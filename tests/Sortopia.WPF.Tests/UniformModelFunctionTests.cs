using System.IO;
using Microsoft.FSharp.Collections;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class UniformModelFunctionTests
{
    private static string Function(string model) => $"sorter-model-set.uniform.{model}";
    private static WorkspaceConfigurationViewModel Module(bool setup, string model)
    {
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: setup ? hierarchyKind.Branch : hierarchyKind.Root).Editor!;
        if (setup) editor.SetupFunctionName = Function(model); else editor.ResultsFunctionName = Function(model);
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
    private static sorterModelSet Generate(bool setup, string model, parameterSet parameters) =>
        Assert.IsType<functionData.SorterModelSet>(Assert.Single(CatalogTestHost.Invoke(setup, Function(model), parameters))).Item;
    private static simpleSorterModel Simple(sorterModel model) => model switch {
        sorterModel.Simple value => value.Item, sorterModel.SimpleR value => value.Item.Model, sorterModel.SimpleS value => value.Item.Model,
        _ => throw new InvalidOperationException()
    };
    public static IEnumerable<object[]> Choices =>
        from setup in new[] { false, true }
        from model in new[] { "msce", "mssi", "msrs", "msuf4", "msuf6" }
        from family in new[] { "Simple", "SimpleR", "SimpleS" }
        select new object[] { setup, model, family };

    [Theory]
    [MemberData(nameof(Choices))]
    public void Both_catalogs_generate_the_fixed_algorithm_without_a_prefix_and_preserve_replay(bool setup, string model, string family)
    {
        var editor = Module(setup, model);
        var values = Parameters(editor, setup).WithBinding(ParameterBinding.create(ParamKeys.sorterModelTypeKey, family));
        Assert.False(FunctionCatalogs.resultsFunctionCatalog.ContainsKey("sorter-model-set.uniform"));
        Assert.False(FunctionCatalogs.setupFunctionCatalog.ContainsKey("sorter-model-set.uniform"));
        var results = FunctionCatalogs.requireResults(Function(model)); var preparation = FunctionCatalogs.requireSetup(Function(model));
        Assert.Equal(results.RequiredParameters, preparation.RequiredParameters);
        Assert.Equal(results.DataTypes, preparation.DataTypes);
        Assert.Empty(results.SourceDataTypes); Assert.Empty(preparation.SourceDataTypes);
        Assert.DoesNotContain(results.RequiredParameters, key => key.Name is "SimpleSorterModelType" or "PrefixLibId");
        Assert.Equal(model == "msce", results.RequiredParameters.Any(key => key.Name == "ExcludeSelfCe"));
        Assert.Equal(model == "msrs", results.RequiredParameters.Any(key => key.Name == "StageWeights"));
        Assert.Equal(model == "msuf4", results.RequiredParameters.Any(key => key.Name == "Seed4Weights"));
        Assert.Equal(model == "msuf6", results.RequiredParameters.Any(key => key.Name == "Seed6Weights"));
        Assert.Equal(model is "msuf4" or "msuf6", results.RequiredParameters.Any(key => key.Name == "UnfoldingWeights"));
        Assert.Equal(model == "msuf6" ? 12 : 8, values.TryFind(ParamKeys.channelCountKey).Value);
        var generated = Generate(setup, model, values);
        Assert.Equal(2, generated.Count);
        Assert.All(generated.SorterModels, value => {
            Assert.Empty(SorterModel.getPrefix(value));
            Assert.True(family switch { "Simple" => value.IsSimple, "SimpleR" => value.IsSimpleR, _ => value.IsSimpleS });
            var simple = Simple(value);
            Assert.True(model switch { "msce" => simple.IsMsce, "mssi" => simple.IsMssi, "msrs" => simple.IsMsrs, "msuf4" => simple.IsMsuf4, _ => simple.IsMsuf6 });
        });
        var repeated = Generate(!setup, model, values);
        Assert.Equal(generated.SorterModels.Select(SorterModel.getId), repeated.SorterModels.Select(SorterModel.getId));
        Assert.Equal(generated.SorterModels.Select(value => SorterModel.makeSorterWithPrefix(value, null!)), repeated.SorterModels.Select(value => SorterModel.makeSorterWithPrefix(value, null!)));
        var next = Generate(setup, model, values.WithBinding(ParameterBinding.create(ParamKeys.replKey, 1)));
        Assert.Empty(generated.SorterModels.Select(SorterModel.getId).Intersect(next.SorterModels.Select(SorterModel.getId)));
    }

    [Theory]
    [InlineData(false, "msuf4")]
    [InlineData(true, "msuf4")]
    [InlineData(false, "msuf6")]
    [InlineData(true, "msuf6")]
    public void Typed_weight_controls_drive_every_stage_in_both_phases(bool setup, string model)
    {
        var editor = Module(setup, model);
        var defaults = setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
        defaults.Single(row => row.Key == "ChannelCount").Value.Text = model == "msuf4" ? "16" : "24";
        var seed = defaults.Single(row => row.Key == (model == "msuf4" ? "Seed4Weights" : "Seed6Weights")).Value;
        foreach (var mode in seed.Weights.Rows[0].Modes) mode.Text = mode.Name == (model == "msuf4" ? "SelfReflection" : "Para4") ? "1" : "0";
        var unfolding = defaults.Single(row => row.Key == "UnfoldingWeights").Value;
        unfolding.Text = "0,3,0;9,0,0";
        Assert.True(unfolding.IsWeights); Assert.False(unfolding.IsTextEditor);
        Assert.Empty(setup ? editor.SetupParameterDefaultsError : editor.ResultsParameterDefaultsError);
        var generated = Generate(setup, model, Parameters(editor, setup));
        foreach (var value in generated.SorterModels) {
            var simple = Simple(value);
            if (simple is simpleSorterModel.Msuf4 uf4)
                Assert.All(uf4.Item.TwoOrbitUnfolder4s, stage => {
                    Assert.True(stage.SeedTwoOrbitPairType.IsSelfRefl);
                    Assert.All(stage.TwoOrbitUnfolderSteps[0].TwoOrbitPairTypes, pair => Assert.True(pair.IsPara));
                    Assert.All(stage.TwoOrbitUnfolderSteps[1].TwoOrbitPairTypes, pair => Assert.True(pair.IsOrtho));
                });
            else Assert.All(((simpleSorterModel.Msuf6)simple).Item.TwoOrbitUnfolder6s, stage => {
                Assert.True(stage.Seed6TwoOrbitType.IsPara4);
                Assert.All(stage.TwoOrbitUnfolderSteps[0].TwoOrbitPairTypes, pair => Assert.True(pair.IsPara));
                Assert.All(stage.TwoOrbitUnfolderSteps[1].TwoOrbitPairTypes, pair => Assert.True(pair.IsOrtho));
            });
        }
        unfolding.Weights.RemoveLevelCommand.Execute(null);
        Assert.Contains("needs 2 unfolding levels", setup ? editor.SetupParameterDefaultsError : editor.ResultsParameterDefaultsError);
        Assert.Contains("needs 2 unfolding levels", Assert.Throws<ArgumentException>(() => Generate(setup, model, Parameters(editor, setup))).Message);
    }

    [Theory]
    [InlineData(false, "mssi", 1)]
    [InlineData(true, "mssi", 1)]
    [InlineData(false, "msrs", 9)]
    [InlineData(true, "msrs", 9)]
    [InlineData(false, "msuf4", 12)]
    [InlineData(true, "msuf4", 12)]
    [InlineData(false, "msuf6", 16)]
    [InlineData(true, "msuf6", 16)]
    public void Invalid_widths_fail_in_partial_defaults_and_before_execution(bool setup, string model, int width)
    {
        var editor = Module(setup, model);
        var values = Parameters(editor, setup).WithBinding(ParameterBinding.create(ParamKeys.channelCountKey, width));
        Assert.Contains("incompatible", Assert.Throws<ArgumentException>(() => Generate(setup, model, values)).Message);
        (setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults).Single(row => row.Key == "ChannelCount").Value.Text = width.ToString();
        Assert.Contains("incompatible", setup ? editor.SetupParameterDefaultsError : editor.ResultsParameterDefaultsError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_validation_checks_all_width_and_weight_combinations_without_moving_defaults(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.ResultsFunctionName = Function(setup ? "mssi" : "msuf6");
        module.Editor.SetupFunctionName = Function(setup ? "msuf6" : "mssi");
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null); module.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
        var defaults = setup ? module.Editor.SetupParameterDefaults : module.Editor.ResultsParameterDefaults;
        Assert.Equal("12", defaults.Single(row => row.Key == "ChannelCount").Value.Text);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", setup, Enumerable.Repeat("12", 50).Append("24").ToArray());
        Assert.Contains("needs 2 unfolding levels", setup ? run.Editor!.SetupParametersError : run.Editor!.ResultsParametersError);
        Assert.Empty((setup ? run.Editor.SetupParameterSetsPreview : run.Editor.ResultsParameterSetsPreview).Rows);
        span.Values.Last().Text = "12";
        Assert.Empty(setup ? run.Editor.SetupParametersError : run.Editor.ResultsParametersError);
        Assert.True((setup ? run.Editor.SetupParameterSetsPreview : run.Editor.ResultsParameterSetsPreview).HasRows);
        Assert.Contains(setup ? run.Editor.InheritedSetupDefaults : run.Editor.InheritedOutputDefaults, row => row.Key == "UnfoldingWeights");
        Assert.DoesNotContain(setup ? run.Editor.SetupParameters : run.Editor.ResultsParameters, row => row.Key == "UnfoldingWeights");
        Assert.Contains("no fixed prefix", setup ? run.Editor.SetupInputGuidance : run.Editor.ResultsInputGuidance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UI_prepares_local_setup_and_portable_uniform_results_with_output_reuse(bool branch)
    {
        var temp = Directory.CreateTempSubdirectory("Sortopia-uniform-models-"); WorkspacePageViewModel? page = null;
        try {
            var root = Directory.CreateDirectory(Path.Combine(temp.FullName, "Workspace")).FullName;
            var settings = new WorkspaceFolderSettings(Path.Combine(temp.FullName, "settings.txt")); settings.Save(root);
            page = new WorkspacePageViewModel(() => { }, _ => null, settings);
            (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null); var editor = page.Selected!.Editor!;
            if (branch) editor.SetupFunctionName = Function("msuf6");
            editor.ResultsFunctionName = Function(branch ? "msuf4" : "msuf6");
            editor.AddRequiredResultsDefaultsCommand.Execute(null); if (branch) editor.AddRequiredSetupDefaultsCommand.Execute(null);
            foreach (var row in editor.ResultsParameterDefaults.Concat(editor.SetupParameterDefaults).Where(row => row.Key == "SorterCount")) row.Value.Text = "2";
            page.AddProjectCommand.Execute(null); var projectId = page.Selected!.Id;
            page.AddRunCommand.Execute(null); var run = page.Selected!;
            WorkspaceParameterSetsPreviewTests.AddSpan(run, "UnfoldingWeights", false, "1,0,0", "0,1,0");
            Assert.All(run.Editor!.ResultsParameters.Single().Values, value => Assert.True(value.IsWeights));
            run.Editor.ResultsQueryOutputs.Single().Keys.Single().Selected = true;
            if (branch) {
                WorkspaceParameterSetsPreviewTests.AddSpan(run, "UnfoldingWeights", true, "1,0,0", "0,1,0");
                run.Editor.SetupQueryOutputs.Single().Keys.Single().Selected = true;
            }
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error); page.Selected = run;
            await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
            Assert.Equal(branch ? 2 : 0, page.SelectedExecution!.Report!.Setup.Length); Assert.Empty(page.SelectedExecution.Report.Results);
            var source = Path.GetDirectoryName(page.ResultsExecution.Folder)!;
            if (branch) Assert.Equal(2, Directory.GetFiles(Path.Combine(source, "data", "setup", "SorterModelSet"), "*.msgpack").Length);
            Assert.False(Directory.Exists(Path.Combine(source, "data", "results")));
            var copy = Directory.CreateDirectory(Path.Combine(temp.FullName, "Cloud project")).FullName;
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) {
                var target = Path.Combine(copy, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            var folder = Path.Combine(copy, "runParameters");
            var report = RunParameterExecution.executeFolder(folder, null!, null!, TestContext.Current.CancellationToken);
            Assert.True(report.Status == runExecutionStatus.Completed, report.Error); Assert.NotEmpty(report.Results);
            ISortopiaDb db = new SortopiaFileDb(copy, projectId);
            foreach (var call in report.Results) {
                Assert.NotNull(call.Parameters.TryFind(ParamKeys.unfoldingWeightsKey));
                var stored = db.Load(Assert.Single(call.StoredOutputs).Reference, TestContext.Current.CancellationToken); Assert.True(stored.IsOk);
                var models = Assert.IsType<functionData.SorterModelSet>(stored.ResultValue.Value).Item;
                Assert.All(models.SorterModels, model => Assert.Empty(SorterModel.getPrefix(model)));
            }
            Assert.Equal(2, Directory.GetFiles(Path.Combine(copy, "data", "results", "SorterModelSet"), "*.msgpack").Length);
            var repeated = RunParameterExecution.executeFolder(folder, null!, null!, TestContext.Current.CancellationToken);
            Assert.True(repeated.Status == runExecutionStatus.Completed, repeated.Error);
            Assert.All(repeated.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        }
        finally { page?.Cancel(); temp.Delete(true); }
    }
}
