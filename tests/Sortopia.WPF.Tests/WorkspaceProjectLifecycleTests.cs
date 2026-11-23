using System.IO;
using Sortopia.Functions;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
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
        page.Selected.Parent.Editor!.ResultsQueryOutputs.Single().Keys.Single(key => key.Key == "ChannelCount").Selected = false;
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        page = Open(); WorkspaceRunExecutionTests.SelectRun(page);
        var run = page.Selected!; var parent = run.Parent!;
        Assert.False(parent.IsSealed); Assert.True(parent.Editor!.CanEditQuerySelection); Assert.False(run.Editor!.CanEditQuerySelection);
        Assert.Single(parent.Editor.ResultsQueryOutputs).Keys.Single(key => key.Key == "ChannelCount").Selected = true;
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null)); Assert.Contains("Saved run '16and18'", page.GenerationHint);
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
        var saved = WorkspaceFileStore.LoadWorkspace(page.WorkspaceFolder);
        Assert.Single(saved.CreateEditDocument().Modules); Assert.True(saved.CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
        Assert.Equal(new[] { "ChannelCount" }, saved.CreateEditDocument().Modules[0].Projects[0].Definition.configuration.resultsDataTypes[0].queryParameterKeys);
        Assert.Equal(2, Directory.GetFiles(page.RunParametersFolder, "*.msgpack").Length);
        page.Selected = page.Selected.Parent;
        Assert.False(page.ResumeProjectEditingCommand.CanExecute(null)); Assert.Contains("generated", page.ResumeProjectEditingHint);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
    }

    [Fact]
    public async Task Sealing_preserves_unsaved_inheritance_choices_without_changing_the_saved_baseline()
    {
        var page = await Create();
        var parent = page.Selected!.Parent!;
        var description = parent.Editor!.Description;
        Assert.False(parent.Editor.DescriptionOverride.IsOverridden);
        parent.Editor.DescriptionOverride.IsOverridden = true;
        parent.Editor.Description = description; // Same effective setting, a different editing choice.
        Assert.True(page.HasUnsavedChanges);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
        await page.GenerateRunParametersAsync(); Assert.Empty(page.OperationError);
        Assert.True(parent.IsSealed);
        Assert.True(page.HasUnsavedChanges);
        Assert.Equal(description, page.Roots[0].ToDocument().Modules[0].Projects[0].Definition.localSettings.description);
        Assert.Null(WorkspaceFileStore.LoadWorkspace(page.WorkspaceFolder).CreateEditDocument().Modules[0].Projects[0].Definition.localSettings.description);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        WorkspaceRunExecutionTests.SelectRun(page);
        parent = page.Selected!.Parent!;
        await page.RefreshRunParameterFilesAsync();
        Assert.True(page.RemoveRunParameterFilesCommand.CanExecute(null));
        await page.RemoveRunParameterFilesAsync(); Assert.Empty(page.OperationError);
        Assert.False(parent.IsSealed);
        Assert.True(parent.Editor!.DescriptionOverride.IsOverridden);
        Assert.Equal(description, parent.Editor.Description);
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
        var saved = WorkspaceFileStore.LoadWorkspace(root);
        var edit = saved.CreateEditDocument();
        var dto = edit.Modules[0].Projects[0].Definition;
        var parent = page.Selected!.Parent!;
        dto.state = projectStateDto.NewSealed(parent.Editor!.ToProjectSettings(), null!);
        WorkspaceFileStore.SaveDocument(edit, saved, TestContext.Current.CancellationToken);
        page = Open(); WorkspaceRunExecutionTests.SelectRun(page);
        var run = page.Selected!; var id = run.Id;
        run.Editor!.DescriptionOverride.IsOverridden = true; run.Editor.Description = "Unfinished run edit";
        page.Selected = run.Parent;
        Assert.True(page.ResumeProjectEditingCommand.CanExecute(null), page.ResumeProjectEditingHint);
        await page.ResumeProjectEditingAsync(); Assert.Empty(page.OperationError);
        Assert.False(page.Selected!.IsSealed); Assert.True(page.Selected.Editor!.CanEditQuerySelection); Assert.False(run.Editor.CanEditQuerySelection);
        Assert.Equal("Unfinished run edit", run.Editor.Description); Assert.True(page.HasUnsavedChanges);
        Assert.Equal(id, Assert.Single(page.Selected.Children).Id);
        Assert.False(WorkspaceFileStore.LoadWorkspace(root).CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
        page.Selected = run;
        Assert.Single(run.Parent!.Editor!.ResultsQueryOutputs).Keys.Single(key => key.Key == "ChannelCount").Selected = true;
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        Assert.False(Open().Roots[0].Children[0].Children[0].IsSealed);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_generated_files_preserves_open_edits_and_discard_uses_the_resumed_revision(bool externalRemoval)
    {
        var page = await Create();
        await page.GenerateRunParametersAsync(); Assert.Empty(page.OperationError);
        var run = page.Selected!; var project = run.Parent!; var editor = run.Editor!;
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
        var originalDescription = editor.Description;
        editor.DescriptionOverride.IsOverridden = true; editor.Description = "Unfinished run edit";
        page.Selected = page.Roots[0]; page.AddModuleCommand.Execute(null);
        var unrelated = page.Selected!; unrelated.Name = "Unfinished module";
        page.Selected = run;
        await page.RefreshRunParameterFilesAsync();
        if (externalRemoval)
        {
            foreach (var file in Directory.GetFiles(page.RunParametersFolder, "*.msgpack")) File.Delete(file);
            await page.RefreshRunParameterFilesAsync();
        }
        else
        {
            Assert.True(page.RemoveRunParameterFilesCommand.CanExecute(null));
            await page.RemoveRunParameterFilesAsync();
        }
        Assert.Empty(page.OperationError);
        Assert.Same(run, page.Selected); Assert.Same(editor, page.Selected.Editor);
        Assert.False(project.IsSealed); Assert.True(project.Editor!.CanEditQuerySelection);
        Assert.Equal("Unfinished run edit", editor.Description); Assert.True(page.HasUnsavedChanges);
        Assert.Same(unrelated, page.Roots[0].Children[1]);
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.Contains("Save the changes to run", page.GenerationHint);
        page.RefreshDiskStatus(); // Re-reading the same revision must leave drafts untouched.
        Assert.Equal("Unfinished run edit", editor.Description); Assert.Same(unrelated, page.Roots[0].Children[1]);

        page.DiscardChangesCommand.Execute(null); WorkspaceRunExecutionTests.SelectRun(page);
        Assert.False(page.HasUnsavedChanges); Assert.Single(page.Roots[0].Children);
        Assert.NotSame(run, page.Selected); Assert.False(page.Selected!.Parent!.IsSealed);
        Assert.Equal(originalDescription, page.Selected.Editor!.Description);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
    }

    public void Dispose() => temporary.Delete(true);
}
