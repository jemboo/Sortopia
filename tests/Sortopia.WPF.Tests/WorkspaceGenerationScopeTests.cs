using System.IO;
using Sortopia.Functions;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceGenerationScopeTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-generation-scope-");
    private async Task<WorkspacePageViewModel> Create(bool branch = false)
    {
        var page = WorkspaceRunExecutionTests.Create(temporary.FullName, branch);
        if (branch)
        {
            var module = page.Selected!.Parent!.Parent!;
            module.Editor!.SetupFunctionName = SetupFunctionNames.evaluationMergeMssi;
            module.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
            module.Editor.SetupParameterDefaults.Single(row => row.Key == "MergeLibId").Value.Text =
                RunExecutionTests.Settings(false).resultsParameterDefaults.Single(row => row.keyName == "MergeLibId").value;
            module.Editor.SetupParameterDefaults.Single(row => row.Key == "SorterCount").Value.Text = "2";
            module.Editor.SetupParameterDefaults.Single(row => row.Key == "StageLength").Value.Text = "1";
        }
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        await page.SaveWorkspaceAsync();
        WorkspaceRunExecutionTests.SelectRun(page);
        await WorkspaceRunExecutionTests.ExecuteSourceAsync(page);
        Assert.Empty(page.Error);
        return page;
    }
    private static async Task AssertGenerates(WorkspacePageViewModel page)
    {
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
        await page.GenerateRunParametersAsync();
        Assert.Empty(page.OperationError);
        Assert.Equal(runExecutionStatus.Completed, page.SelectedExecution!.Report!.Status);
        Assert.NotEmpty(Directory.GetFiles(page.RunParametersFolder, "*.msgpack"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unrelated_invalid_branch_does_not_block_generation(bool branch, bool saveDraft)
    {
        var page = await Create(branch);
        page.Selected = page.Roots[0]; page.AddBranchModuleCommand.Execute(null);
        page.Selected!.Editor!.LoadProject(RunExecutionTests.Settings(true));
        page.Selected.Editor.SetupFunctionName = SetupFunctionNames.evaluationSource;
        page.AddProjectCommand.Execute(null);
        Assert.NotEmpty(page.SaveIssues);
        if (saveDraft) { await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError); }
        WorkspaceRunExecutionTests.SelectRun(page);
        await AssertGenerates(page);
        Assert.Equal(!saveDraft, page.HasUnsavedChanges);
        Assert.NotEmpty(page.SaveIssues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_sibling_run_does_not_block_selected_run(bool branch)
    {
        var page = await Create(branch);
        page.Selected = page.Selected!.Parent;
        page.AddRunCommand.Execute(null);
        Assert.NotEmpty(page.Selected!.Editor!.ValidationError);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        WorkspaceRunExecutionTests.SelectRun(page);
        await AssertGenerates(page);
    }

    [Fact]
    public async Task Source_project_with_an_invalid_sibling_run_still_supplies_existing_results()
    {
        var page = await Create(true);
        page.Selected = page.Roots[0].Children[1].Children[0];
        page.AddRunCommand.Execute(null);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        WorkspaceRunExecutionTests.SelectRun(page);
        await AssertGenerates(page);
    }

    [Fact]
    public async Task Selected_run_edits_and_invalid_values_remain_blocked_with_specific_messages()
    {
        var page = await Create();
        var node = page.Selected!;
        node.Editor!.DescriptionOverride.IsOverridden = true;
        node.Editor.Description = "Unsaved change";
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.Contains("Save the changes to run", page.GenerationHint);
        await page.SaveWorkspaceAsync(); WorkspaceRunExecutionTests.SelectRun(page);
        await AssertGenerates(page);
        page.Selected!.Editor!.ResultsParameters.Single(row => row.Key == "SortableDataFormat").Values[0].Text = "invalid";
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.Contains("SortableDataFormat", page.GenerationHint);
        await page.SaveWorkspaceAsync(); WorkspaceRunExecutionTests.SelectRun(page);
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.Contains("SortableDataFormat", page.GenerationHint);
    }

    [Fact]
    public async Task Only_required_locations_and_definition_files_block_generation()
    {
        var page = await Create(true);
        page.Selected = page.Roots[0]; page.AddModuleCommand.Execute(null);
        var unrelatedId = page.Selected!.Id;
        await page.SaveWorkspaceAsync(); WorkspaceRunExecutionTests.SelectRun(page);
        var unrelated = page.Roots[0].Children.Single(node => node.Id == unrelatedId);
        File.Delete(WorkspaceDefinitionFiles.Require(unrelated.SavedFolderPath!, "module"));
        await AssertGenerates(page);
        unrelated.FolderName = "Unrelated move";
        await AssertGenerates(page);
        var parentModule = page.Selected!.Parent!.Parent!;
        parentModule.FolderName = "Relevant move";
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.Contains("location change", page.GenerationHint);
        page.DiscardChangesCommand.Execute(null); WorkspaceRunExecutionTests.SelectRun(page);
        var source = page.Roots[0].Children[1].Children[0];
        File.Delete(WorkspaceDefinitionFiles.Require(source.SavedFolderPath!, "project"));
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.Contains(source.Name, page.GenerationHint); Assert.Contains("missing", page.GenerationHint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_selected_run_or_project_definition_is_reported(bool project)
    {
        var page = await Create();
        var node = project ? page.Selected!.Parent! : page.Selected!;
        var file = project ? WorkspaceDefinitionFiles.Require(node.SavedFolderPath!, "project") : node.SavedFolderPath!;
        File.Delete(file);
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.Contains(file, page.GenerationHint);
    }

    public void Dispose() => temporary.Delete(true);
}
