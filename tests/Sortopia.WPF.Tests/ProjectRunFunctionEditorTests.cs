using System.IO;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ProjectRunFunctionEditorTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("Sortopia-project-run-functions-");
    private WorkspacePageViewModel Open()
    {
        var settings = new WorkspaceFolderSettings(Path.Combine(folder.FullName, "settings.txt")); settings.Save(folder.FullName);
        return new(() => { }, _ => null, settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Projects_and_runs_begin_with_read_only_functions_and_gate_dependent_settings(bool branch)
    {
        var page = Open();
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var module = page.Selected!;
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        foreach (var node in new[] { project, run })
        {
            page.Selected = node;
            Assert.Equal(branch ? "setupFunction" : "resultsFunction", page.ActiveItemKey);
            Assert.Equal("Functions", page.InspectorItems[0].Group);
            Assert.Equal("Inherited functions", page.ActiveItemTitle);
            Assert.False(node.Editor!.ResultsFunctionOverride.CanOverride);
            Assert.False(node.Editor.ResultsFunctionOverride.ShowCatalogPicker);
            Assert.Contains("parent module", node.Editor.FunctionsStatus);
            Assert.DoesNotContain(page.InspectorItems, item => item.Key is "resultsDefaults" or "resultsSpans" or "resultsTypes" or "setupOutputs");
            Assert.Contains(page.InspectorItems, item => item.Key == "resultsDatabase");
        }
        module.Editor!.LoadProject(RunExecutionTests.Settings(branch));
        foreach (var node in new[] { project, run })
        {
            page.Selected = node;
            Assert.True(node.Editor!.FunctionsReady);
            Assert.Contains(page.InspectorItems, item => item.Key == "resultsDefaults");
            Assert.Contains(page.InspectorItems, item => item.Key == "resultsTypes");
            Assert.Equal(node.IsRun, page.InspectorItems.Any(item => item.Key == "resultsSpans"));
            var function = page.InspectorItems.Single(item => item.Key == "resultsFunction");
            page.SelectInspectorSourceCommand.Execute(function);
            Assert.Same(module, page.Selected);
        }
        page.Selected = run; page.SelectConfigurationItem("resultsSpans");
        module.Editor.ResultsFunctionName = "unavailable";
        Assert.Equal(branch ? "setupFunction" : "resultsFunction", page.ActiveItemKey);
        Assert.DoesNotContain(page.InspectorItems, item => item.Key == "resultsSpans");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Defaults_and_spans_follow_function_changes_and_only_active_values_are_saved(bool setup)
    {
        var page = Open();
        (setup ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var module = page.Selected!;
        module.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformSorterModels;
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        if (setup) { module.Editor.SetupFunctionName = SetupFunctionNames.uniformSorterModels; module.Editor.AddRequiredSetupDefaultsCommand.Execute(null); }
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        var local = WorkspaceParameterInheritanceTests.AddDefault(project.Editor!, "StageLength", "5", setup);
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "StageLength", setup, "2", "3");
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        void Change(bool standard)
        {
            if (setup) module.Editor.SetupFunctionName = standard ? SetupFunctionNames.standardTests : SetupFunctionNames.uniformSorterModels;
            else module.Editor.ResultsFunctionName = standard ? ResultsFunctionNames.standardSortableTests : ResultsFunctionNames.uniformSorterModels;
        }
        Change(true);
        Assert.DoesNotContain(setup ? project.Editor!.LocalSetupParameterDefaults : project.Editor!.LocalResultsParameterDefaults, row => row.Key == "StageLength");
        Assert.DoesNotContain(setup ? run.Editor!.ActiveSetupParameters : run.Editor!.ActiveResultsParameters, row => row.Key == "StageLength");
        Assert.Contains("editing session", setup ? run.Editor.RetainedSetupSpansHint : run.Editor.RetainedResultsSpansHint);
        Change(false);
        Assert.DoesNotContain(local, setup ? project.Editor.LocalSetupParameterDefaults : project.Editor.LocalResultsParameterDefaults);
        Assert.Contains(span, setup ? run.Editor.ActiveSetupParameters : run.Editor.ActiveResultsParameters);
        Assert.Equal("5", local.Value.Text);
        Assert.Equal(new[] { "2", "3" }, span.Values.Select(value => value.Text));
        Change(true);
        (setup ? module.Editor.AddRequiredSetupDefaultsCommand : module.Editor.AddRequiredResultsDefaultsCommand).Execute(null);
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        Assert.Empty(run.Editor.ResultsParametersError);
        if (setup) Assert.Empty(run.Editor.SetupParametersError);
        var inputs = run.Editor.ToRunInputs(run.Id, run.Name, project.Id);
        Assert.DoesNotContain(setup ? inputs.setupParameterSpans : inputs.resultsParameterSpans, row => row.keyName == "StageLength");
        var preview = setup ? run.Editor.SetupParameterSetsPreview : run.Editor.ResultsParameterSetsPreview;
        Assert.Single(preview.Rows);
        Assert.DoesNotContain(preview.Columns, column => column.Key == "StageLength");
        Assert.Empty(setup ? project.Editor.ToLocalOverrides().setupParameterDefaults : project.Editor.ToLocalOverrides().resultsParameterDefaults);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(folder.FullName).Modules[0].Projects[0].Runs[0];
        var sets = RunParameterSets.create(saved);
        Assert.Equal(new[] { "ChannelCount", "SortableDataFormat" }, (setup ? sets.Setup.Value : sets.Results).Single().ParamMap.Keys.Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sealed_projects_and_runs_keep_their_function_contract_after_module_changes(bool branch)
    {
        var page = Open(); (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        page.Selected!.Editor!.LoadProject(RunExecutionTests.Settings(branch));
        page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null); WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var module = page.Roots[0].Children[0]; var project = module.Children[0]; var run = project.Children[0];
        module.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformSorterModels;
        if (branch) module.Editor.SetupFunctionName = SetupFunctionNames.uniformSorterModels;
        foreach (var node in new[] { project, run })
        {
            page.Selected = node;
            Assert.True(node.Editor!.FunctionsReady);
            Assert.Equal(ResultsFunctionNames.mergeSortableTests, node.Editor.ResultsFunctionName);
            Assert.Single(node.Editor.ResultsOutputContracts, output => output.Kind == "SortableTests");
            Assert.Contains(page.InspectorItems, item => item.Key == "resultsDefaults");
        }
        Assert.Same(project.Editor, run.Editor!.ResultsFunctionOverride.InheritedSource);
        Assert.Contains("fixed", project.Editor!.FunctionsHint);
    }

    public void Dispose() => folder.Delete(true);
}
