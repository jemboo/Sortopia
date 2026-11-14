using System.IO;
using Sortopia.Core.Serialization;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceDraftSavingTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-drafts-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private WorkspacePageViewModel Open(Func<WorkspaceUnsavedChoice>? confirm = null, Func<string, Guid?, Task>? execute = null)
    {
        Directory.CreateDirectory(Root);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(Root);
        return new(() => { }, _ => null, settings, confirm ?? (() => throw new Exception("Unexpected unsaved-changes prompt")), openResultsExecution: execute);
    }
    private static WorkspaceTreeNode Module(WorkspacePageViewModel page) => page.Roots[0].Children[0];
    private static WorkspaceTreeNode Project(WorkspacePageViewModel page) => Module(page).Children[0];
    private static WorkspaceTreeNode Run(WorkspacePageViewModel page) => Project(page).Children[0];
    private static void Configure(WorkspaceTreeNode module, bool branch)
    {
        module.Editor!.ResultsFunctionName = "sortable-tests.standard";
        WorkspaceParameterInheritanceTests.AddDefault(module.Editor, "ChannelCount", "4");
        WorkspaceParameterInheritanceTests.AddDefault(module.Editor, "SortableDataFormat", "Bools");
        if (branch)
        {
            module.Editor.SetupFunctionName = "sortable-tests-with-prefix.standard";
            WorkspaceParameterInheritanceTests.AddDefault(module.Editor, "ChannelCount", "4", true);
            WorkspaceParameterInheritanceTests.AddDefault(module.Editor, "SortableDataFormat", "Bools", true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_functions_and_project_databases_survive_save_on_close(bool branch)
    {
        var prompts = 0;
        var page = Open(() => { prompts++; return WorkspaceUnsavedChoice.Save; });
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var moduleId = page.Selected!.Id;
        page.AddProjectCommand.Execute(null); var projectId = page.Selected!.Id;
        page.Selected.Editor!.ResultsDatabaseName = " ";
        if (branch) page.Selected.Editor.SetupDatabaseName = "";
        Assert.NotEmpty(page.SaveIssues); Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.True(await page.TryLeaveAsync()); Assert.Equal(1, prompts);
        Assert.Empty(page.OperationError); Assert.False(page.HasUnsavedChanges);
        Assert.Contains("Draft saved", page.Status);

        var reopened = Open();
        Assert.Equal(moduleId, Module(reopened).Id); Assert.Equal(projectId, Project(reopened).Id);
        Assert.Equal(" ", Project(reopened).Editor!.ResultsDatabaseName);
        if (branch) Assert.Equal("", Project(reopened).Editor!.SetupDatabaseName);
        Assert.Contains(reopened.SaveIssues, issue => issue.Message.Contains("No results function"));
        Assert.False(reopened.HasUnsavedChanges); Assert.False(reopened.SaveWorkspaceCommand.CanExecute(null));
        Assert.False(reopened.DiscardChangesCommand.CanExecute(null));
        Assert.True(await reopened.TryLeaveAsync());
        Assert.Equal(3, Directory.GetFiles(Root, "*.msgpack", SearchOption.AllDirectories).Length);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Invalid_defaults_and_run_inputs_round_trip_then_can_be_completed(bool branch, bool setup)
    {
        var page = Open();
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null); Configure(page.Selected!, branch);
        page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null);
        var runId = page.Selected!.Id;
        var defaults = setup ? Module(page).Editor!.SetupParameterDefaults : Module(page).Editor!.ResultsParameterDefaults;
        defaults.Single(row => row.Key == "ChannelCount").Value.Text = "not a number";
        var run = Run(page); run.Editor!.DescriptionOverride.IsOverridden = true; run.Editor.Description = "Finish this later";
        var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", setup, "also unfinished");
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.OperationError); Assert.False(page.HasUnsavedChanges); Assert.False(Project(page).IsSealed);
        var document = WorkspaceFileStore.LoadDocument(Root);
        var raw = setup ? document.Modules[0].Definition.localOverrides.setupParameterDefaults : document.Modules[0].Definition.localOverrides.resultsParameterDefaults;
        Assert.Equal("not a number", raw.Single(row => row.keyName == "ChannelCount").value);
        Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.LoadDefinition(Root));

        var reopened = Open(); run = Run(reopened); reopened.Selected = run;
        Assert.Equal(runId, run.Id); Assert.Equal("Finish this later", run.Editor!.Description);
        var restoredSpans = setup ? run.Editor.SetupParameters : run.Editor.ResultsParameters;
        Assert.Equal("also unfinished", Assert.Single(restoredSpans).Values[0].Text);
        Assert.NotEmpty(reopened.SaveIssues); Assert.False(reopened.GenerateRunParametersCommand.CanExecute(null));
        Assert.Empty((setup ? run.Editor.SetupParameterSetsPreview : run.Editor.ResultsParameterSetsPreview).Rows);
        Assert.False(reopened.HasUnsavedChanges);
        Assert.True(await reopened.TryLeaveAsync());

        Assert.Single(restoredSpans).RemoveCommand.Execute(null);
        var projectEditor = Project(reopened).Editor!;
        Assert.Single(setup ? projectEditor.ExcludedSetupDefaults : projectEditor.ExcludedResultsDefaults).RestoreCommand.Execute(null);
        defaults = setup ? Module(reopened).Editor!.SetupParameterDefaults : Module(reopened).Editor!.ResultsParameterDefaults;
        defaults.Single(row => row.Key == "ChannelCount").Value.Text = "4";
        await reopened.SaveWorkspaceAsync(); Assert.True(reopened.Error.Length == 0, reopened.Error); Assert.False(Project(reopened).IsSealed);
        reopened.Selected = Run(reopened);
        Assert.True(reopened.GenerateRunParametersCommand.CanExecute(null));
        await reopened.GenerateRunParametersAsync(); Assert.Empty(reopened.Error);
        Assert.True(Project(reopened).IsSealed);
        Assert.Single(Directory.GetFiles(reopened.RunParametersFolder, "*.msgpack"));
    }

    [Fact]
    public async Task Duplicate_and_unfinished_default_rows_are_preserved()
    {
        var page = Open(); page.AddModuleCommand.Execute(null); Configure(page.Selected!, false);
        var editor = page.Selected!.Editor!;
        WorkspaceParameterInheritanceTests.AddDefault(editor, "ChannelCount", "bad duplicate");
        WorkspaceParameterInheritanceTests.AddDefault(editor, "", "unfinished");
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        var restored = Module(Open()).Editor!.ResultsParameterDefaults;
        Assert.Equal(4, restored.Count);
        Assert.Equal("bad duplicate", restored.Last(row => row.Key == "ChannelCount").Value.Text);
        Assert.Equal("unfinished", restored.Single(row => row.Key == "").Value.Text);
    }

    [Fact]
    public async Task Unknown_catalog_function_can_be_saved_and_reopened()
    {
        var page = Open(); page.AddModuleCommand.Execute(null);
        page.Selected!.Editor!.ResultsFunctionName = "unfinished.function";
        page.AddProjectCommand.Execute(null);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        var reopened = Open();
        Assert.Equal("unfinished.function", Module(reopened).Editor!.ResultsFunctionName);
        Assert.NotEmpty(reopened.SaveIssues); Assert.False(reopened.HasUnsavedChanges);
    }

    [Fact]
    public async Task Invalid_module_edits_do_not_change_a_sealed_project()
    {
        var page = Open(); page.AddModuleCommand.Execute(null); Configure(page.Selected!, false);
        page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error); Assert.False(Project(page).IsSealed);
        page.Selected = Run(page); await page.GenerateRunParametersAsync(); Assert.Empty(page.Error); Assert.True(Project(page).IsSealed);
        Module(page).Editor!.ResultsParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "bad";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        var reopened = Open(); Assert.True(Project(reopened).IsSealed);
        Assert.Equal("bad", Module(reopened).Editor!.ResultsParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text);
        Assert.Equal("4", Run(reopened).Editor!.InheritedOutputDefaults.Single(row => row.Key == "ChannelCount").Value);
    }

    [Fact]
    public async Task Failed_draft_save_explains_the_io_error_and_keeps_unsaved_changes()
    {
        var page = Open(); page.AddModuleCommand.Execute(null); page.AddProjectCommand.Execute(null);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        var path = WorkspaceTestFiles.DefinitionPath(Root, "workspace");
        page.WorkspaceName = "Changed";
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.Contains("Could not save", page.OperationError); Assert.Contains("Your edits are still open", page.OperationError);
            Assert.True(page.HasUnsavedChanges); Assert.NotEmpty(page.SaveIssues);
        }
        Assert.NotEqual("Changed", WorkspaceFileStore.LoadDocument(Root).Definition.workspaceName);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError); Assert.False(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Saved_invalid_edits_keep_existing_prepared_work_accessible()
    {
        string? opened = null; Guid? openedRun = null;
        var page = Open(execute: (path, run) => { opened = path; openedRun = run; return Task.CompletedTask; });
        page.AddModuleCommand.Execute(null); Configure(page.Selected!, false);
        page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null);
        await page.SaveWorkspaceAsync(); page.Selected = Run(page);
        await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
        var folder = page.RunParametersFolder; var runId = Run(page).Id;
        Module(page).Editor!.ResultsParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "invalid";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError); page.Selected = Run(page);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
        Assert.True(page.ExecutePageCommand.CanExecute(null)); await page.OpenExecutePageAsync();
        Assert.Equal(folder, opened); Assert.Equal(runId, openedRun); Assert.Single(Directory.GetFiles(folder, "*.msgpack"));
    }

    [Fact]
    public async Task Incomplete_source_projects_leave_consumers_editable_until_both_are_ready()
    {
        var page = Open(); page.AddBranchModuleCommand.Execute(null); var module = page.Selected!; Configure(module, true);
        var source = WorkspaceHierarchyTests.AddSetupSource(module); var sourceId = source.Id;
        module.Editor!.SetupParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "8";
        page.Selected = module; page.AddProjectCommand.Execute(null); var consumerId = page.Selected!.Id;
        page.AddRunCommand.Execute(null);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        var consumer = page.Roots[0].DescendantsAndSelf().Single(node => node.Id == consumerId);
        Assert.False(consumer.IsSealed);
        Assert.Contains(page.SaveIssues, issue => issue.Message.Contains("Generate its run parameters"));
        var reopened = Open();
        reopened.Selected = reopened.Roots[0].DescendantsAndSelf().Single(node => node.Id == sourceId);
        Assert.True(reopened.AddRunCommand.CanExecute(null)); reopened.AddRunCommand.Execute(null);
        await reopened.SaveWorkspaceAsync(); Assert.Empty(reopened.OperationError);
        Assert.False(reopened.Roots[0].DescendantsAndSelf().Single(node => node.Id == sourceId).IsSealed);
        reopened.Selected = reopened.Roots[0].DescendantsAndSelf().Single(node => node.Id == sourceId).Children[0];
        await WorkspaceRunExecutionTests.GenerateAndExecuteAsync(reopened);
        Assert.False(reopened.Roots[0].DescendantsAndSelf().Single(node => node.Id == consumerId).IsSealed);
        Assert.True(reopened.Roots[0].DescendantsAndSelf().Single(node => node.Id == sourceId).IsSealed);
    }

    [Fact]
    public async Task Draft_module_folder_moves_keep_identity_and_files()
    {
        var page = Open(); page.AddModuleCommand.Execute(null); page.AddProjectCommand.Execute(null);
        await page.SaveWorkspaceAsync();
        var module = Module(page); var id = module.Id; var oldPath = module.GetFolderPath(Root);
        File.WriteAllText(Path.Combine(oldPath, "notes.txt"), "unfinished");
        module.FolderName = "Moved";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        Assert.False(Directory.Exists(oldPath)); Assert.Equal("unfinished", File.ReadAllText(Path.Combine(Root, "Moved", "notes.txt")));
        Assert.Equal(id, Module(Open()).Id);
    }
    public void Dispose() => temporary.Delete(true);
}
