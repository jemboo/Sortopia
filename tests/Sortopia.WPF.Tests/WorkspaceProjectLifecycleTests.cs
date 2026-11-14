using System.IO;
using Sortopia.Functions;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceProjectLifecycleTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-project-lifecycle-");
    private WorkspacePageViewModel Open() => new(() => { }, _ => null, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
    private async Task<WorkspacePageViewModel> Create()
    {
        var page = WorkspaceRunExecutionTests.Create(temporary.FullName, false);
        page.Selected!.Parent!.Parent!.Editor!.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
        page.Selected!.Name = "16and18";
        WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected, "ChannelCount", false, "16", "18");
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        WorkspaceRunExecutionTests.SelectRun(page);
        return page;
    }

    [Fact]
    public async Task Saved_spans_leave_query_keys_editable_until_generation_and_preserve_other_edits()
    {
        var page = await Create();
        Assert.False(page.Selected!.Parent!.IsSealed);
        page = Open(); WorkspaceRunExecutionTests.SelectRun(page);
        var run = page.Selected!; var parent = run.Parent!;
        Assert.False(parent.IsSealed); Assert.True(run.Editor!.CanEditQuerySelection);
        Assert.Single(run.Editor.ResultsQueryOutputs).Keys.Single(key => key.Key == "ChannelCount").Selected = true;
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null)); Assert.Contains("Save the changes to project", page.GenerationHint);
        await page.SaveWorkspaceAsync(); WorkspaceRunExecutionTests.SelectRun(page);
        Assert.False(page.Selected!.Parent!.IsSealed);
        page.Selected = page.Roots[0]; page.AddModuleCommand.Execute(null);
        var unrelated = page.Selected!; unrelated.Name = "Unfinished edits";
        WorkspaceRunExecutionTests.SelectRun(page);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
        await page.GenerateRunParametersAsync(); Assert.Empty(page.OperationError);
        Assert.Equal(runExecutionStatus.Completed, page.SelectedExecution!.Report!.Status);
        Assert.True(page.Selected!.Parent!.IsSealed); Assert.False(page.Selected.Editor!.CanEditQuerySelection);
        Assert.Same(unrelated, page.Roots[0].Children[1]); Assert.True(page.HasUnsavedChanges);
        var saved = WorkspaceFileStore.LoadDocument(page.WorkspaceFolder);
        Assert.Single(saved.Modules); Assert.True(saved.Modules[0].Projects[0].Definition.isSealed);
        Assert.Equal(new[] { "ChannelCount" }, saved.Modules[0].Projects[0].Definition.configuration.resultsDataTypes[0].queryParameterKeys);
        Assert.Equal(2, Directory.GetFiles(page.RunParametersFolder, "*.msgpack").Length);
        page.Selected = page.Selected.Parent;
        Assert.False(page.ResumeProjectEditingCommand.CanExecute(null)); Assert.Contains("generated", page.ResumeProjectEditingHint);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
    }

    [Fact]
    public async Task Unsaved_inherited_default_changes_must_be_saved_before_generation()
    {
        var page = await Create();
        page.Selected!.Parent!.Parent!.Editor!.ResultsParameterDefaults.Single(row => row.Key == "SortableDataFormat").Value.Text = "Ints";
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null)); Assert.Contains("inherited settings", page.GenerationHint);
        await page.SaveWorkspaceAsync(); WorkspaceRunExecutionTests.SelectRun(page);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
    }

    [Fact]
    public async Task Unused_sealed_project_can_resume_editing_without_losing_run_edits()
    {
        var page = await Create();
        var root = page.WorkspaceFolder;
        var saved = WorkspaceFileStore.LoadDocument(root);
        var dto = saved.Modules[0].Projects[0].Definition;
        var parent = page.Selected!.Parent!;
        dto.configuration = parent.Editor!.ToProjectSettings(); dto.isSealed = true;
        dto.localOverrides = null; dto.inheritedDefaults = null; dto.inheritedFrom = null;
        WorkspaceFileStore.SaveDocument(root, saved, WorkspaceFileStore.LoadDocument(root), TestContext.Current.CancellationToken);
        page = Open(); WorkspaceRunExecutionTests.SelectRun(page);
        var run = page.Selected!; var id = run.Id;
        run.Editor!.DescriptionOverride.IsOverridden = true; run.Editor.Description = "Unfinished run edit";
        page.Selected = run.Parent;
        Assert.True(page.ResumeProjectEditingCommand.CanExecute(null), page.ResumeProjectEditingHint);
        await page.ResumeProjectEditingAsync(); Assert.Empty(page.OperationError);
        Assert.False(page.Selected!.IsSealed); Assert.True(run.Editor.CanEditQuerySelection);
        Assert.Equal("Unfinished run edit", run.Editor.Description); Assert.True(page.HasUnsavedChanges);
        Assert.Equal(id, Assert.Single(page.Selected.Children).Id);
        Assert.False(WorkspaceFileStore.LoadDocument(root).Modules[0].Projects[0].Definition.isSealed);
        page.Selected = run;
        Assert.Single(run.Editor.ResultsQueryOutputs).Keys.Single(key => key.Key == "ChannelCount").Selected = true;
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        Assert.False(Open().Roots[0].Children[0].Children[0].IsSealed);
    }
    public void Dispose() => temporary.Delete(true);
}
