using System.IO;
using System.Runtime.ExceptionServices;
using Sortopia.WPF.Settings;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces;
using Sortopia.WPF.ViewModels;
using Sortopia.WPF.ViewModels.Pages.Home;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspacePageTests : IDisposable
{
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("Sortopia-workspace-settings-");
    private string SettingsPath => Path.Combine(temp.FullName, "settings", "workspace-folder.txt");
    private WorkspaceFolderSettings Settings => new(SettingsPath);

    [Fact]
    public async Task Missing_workspace_can_be_named_saved_and_detected_when_switching_folders()
    {
        var first = temp.CreateSubdirectory("First root").FullName;
        var second = temp.CreateSubdirectory("Second root").FullName;
        var selections = new Queue<string>([first, second, first]);
        var page = new WorkspacePageViewModel(() => { }, _ => selections.Dequeue(), Settings);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.False(page.DiscardChangesCommand.CanExecute(null));

        page.BrowseWorkspaceFolderCommand.Execute(null);
        Assert.False(page.HasSavedWorkspace);
        Assert.True(page.ShowNewWorkspace);
        Assert.Contains("No workspace file", page.WorkspaceFileStatus);
        Assert.False(File.Exists(WorkspaceTestFiles.DefinitionPath(first, "workspace")));
        page.WorkspaceName = "";
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.NotEmpty(page.WorkspaceNameError);
        page.WorkspaceName = "Sorting research";
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        await page.SaveWorkspaceAsync();

        Assert.Empty(page.Error);
        Assert.True(page.HasSavedWorkspace);
        Assert.False(page.ShowNewWorkspace);
        Assert.Contains("Saved workspace file", page.WorkspaceFileStatus);
        Assert.Contains("Sorting research", page.Status);
        Assert.Equal("Sorting research", WorkspaceFileStore.LoadDefinition(first).WorkspaceName);
        Assert.False(page.DiscardChangesCommand.CanExecute(null));
        Assert.True(new WorkspacePageViewModel(() => { }, _ => null, Settings).HasSavedWorkspace);

        page.BrowseWorkspaceFolderCommand.Execute(null);
        Assert.True(page.ShowNewWorkspace);
        Assert.False(page.HasSavedWorkspace);
        Assert.False(page.DiscardChangesCommand.CanExecute(null));
        Assert.Equal("Second root", page.WorkspaceName);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        Assert.True(page.HasSavedWorkspace);
        Assert.False(page.ShowNewWorkspace);
        Assert.Equal("Sorting research", page.WorkspaceName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_workspace_module_and_run_names_can_change_but_sealed_project_identity_is_fixed(bool branch)
    {
        var folder = temp.CreateSubdirectory("Root").FullName;
        var page = new WorkspacePageViewModel(() => { }, _ => folder, Settings);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        page.WorkspaceName = "Research";
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        page.Selected!.Name = "Sorting";
        page.Selected.FolderName = "sorters";
        page.AddProjectCommand.Execute(null);
        WorkspaceHierarchyTests.ConfigureProject(page.Selected!);
        page.Selected!.Name = "Search";
        page.Selected.FolderName = "search";
        page.AddRunCommand.Execute(null);
        page.Selected!.Name = "Trial";
        var runId = page.Selected.Id;
        page.Selected.Editor!.LoadRun(Sortopia.Workspaces.Serialization.RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("Search", "Trial", branch)));
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var projectFolder = Path.Combine(folder, "sorters", "search");
        var output = Path.Combine(projectFolder, "results.txt");
        File.WriteAllText(output, "Keep results");

        // Opening a saved root restores its hierarchy before any edits can be saved.
        var reopened = new WorkspacePageViewModel(() => { }, _ => null, Settings);
        Assert.Equal("Research", reopened.WorkspaceName);
        Assert.False(reopened.SaveWorkspaceCommand.CanExecute(null));
        reopened.Roots.Single().Name = "Research: updated";
        var module = reopened.Roots.Single().Children.Single();
        module.Name = "Sorting: updated";
        module.Children.Single().Name = "Search: updated";
        module.Children.Single().Children.Single().Name = "Trial: renamed";
        Assert.True(reopened.SaveWorkspaceCommand.CanExecute(null));
        Assert.Contains("Unsaved changes", reopened.WorkspaceFileStatus);
        await reopened.SaveWorkspaceAsync();
        Assert.Empty(reopened.Error);
        Assert.False(reopened.HasUnsavedChanges);
        Assert.False(reopened.SaveWorkspaceCommand.CanExecute(null));
        Assert.DoesNotContain("Unsaved changes", reopened.WorkspaceFileStatus);
        var saved = WorkspaceFileStore.LoadDefinition(folder);
        Assert.Equal("Research: updated", saved.WorkspaceName);
        Assert.Equal("Root", saved.FolderName);
        Assert.Equal("Sorting: updated", saved.Modules.Single().ModuleName);
        Assert.Equal("sorters", saved.Modules.Single().FolderName);
        var project = saved.Modules.Single().Projects.Single();
        Assert.Equal("Search", project.ProjectName);
        Assert.Equal("search", project.FolderName);
        Assert.Equal("Search", project.Runs.Single().ProjectName);
        Assert.Equal("Trial: renamed", project.Runs.Single().RunName);
        Assert.Equal(runId, project.Runs.Single().Id);
        var expectedDto = Sortopia.Workspaces.Serialization.RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("Search", "Trial: renamed", branch));
        expectedDto.id = runId;
        expectedDto.parentProjectId = project.Id;
        var expectedRun = WorkspaceHierarchyTests.FromRunDto(expectedDto);
        Assert.Equal(Sortopia.Workspaces.Serialization.WorkspaceCodec.encodeRun(expectedRun),
            File.ReadAllBytes(Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(runId))));
        Assert.Equal("Keep results", File.ReadAllText(output));
        Assert.Single(Directory.GetDirectories(folder));
        Assert.Single(Directory.GetDirectories(Path.Combine(folder, "sorters")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(projectFolder, "runs")));
        Assert.Equal("Research: updated", new WorkspacePageViewModel(() => { }, _ => null, Settings).WorkspaceName);
    }

    [Fact]
    public async Task Duplicate_run_names_are_reported_without_writing_and_can_be_corrected()
    {
        var folder = temp.CreateSubdirectory("Root").FullName;
        var page = new WorkspacePageViewModel(() => { }, _ => folder, Settings);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        page.AddModuleCommand.Execute(null);
        page.AddProjectCommand.Execute(null);
        WorkspaceHierarchyTests.ConfigureProject(page.Selected!);
        var parent = page.Selected!;
        page.AddRunCommand.Execute(null);
        var first = page.Selected!;
        first.Name = "First";
        first.FolderName = "trial";
        first.Editor!.LoadRun(Sortopia.Workspaces.Serialization.RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun(parent.Name, first.Name)));
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(folder, "workspace"));
        parent = page.Roots.Single().Children.Single().Children.Single();
        page.Selected = parent;
        page.AddRunCommand.Execute(null);
        var second = page.Selected!;
        second.Name = "FIRST";
        second.FolderName = "another-trial";
        second.Editor!.LoadRun(first.Editor!.ToRunInputs(second.Id, second.Name, parent.Id));
        await page.SaveWorkspaceAsync();
        Assert.Contains("Duplicate name in runs", page.Error);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(folder, "workspace")));
        second.Name = "Second";
        Assert.False(second.HasFolder);
        Assert.NotEqual(first.Id, second.Id);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Equal(2, WorkspaceFileStore.LoadDefinition(folder).Modules.Single().Projects.Single().Runs.Length);
    }

    [Fact]
    public async Task Failed_rename_keeps_edits_available_for_retry_and_rejects_external_workspace_changes()
    {
        var folder = temp.CreateSubdirectory("Root").FullName;
        var page = new WorkspacePageViewModel(() => { }, _ => folder, Settings);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        await page.SaveWorkspaceAsync();
        page.WorkspaceName = "Updated";
        using (var locked = new FileStream(WorkspaceTestFiles.DefinitionPath(folder, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
            Assert.True(page.HasUnsavedChanges);
            Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
            Assert.Equal("Root", WorkspaceFileStore.LoadDefinition(folder).WorkspaceName);
        }
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Equal("Updated", WorkspaceFileStore.LoadDefinition(folder).WorkspaceName);
        page.WorkspaceName = "My edit";
        var external = workspace.create(Guid.NewGuid(), "External edit", "Root", []);
        WorkspaceFileStore.SaveWorkspace(folder, external, WorkspaceFileStore.LoadDefinition(folder), CancellationToken.None);
        await page.SaveWorkspaceAsync();
        Assert.Contains("changed since it was loaded", page.Error);
        Assert.Equal("My edit", page.WorkspaceName);
        Assert.Equal("External edit", WorkspaceFileStore.LoadDefinition(folder).WorkspaceName);
        page.DiscardChangesCommand.Execute(null);
        Assert.Equal("Updated", page.WorkspaceName); // Discard restores our saved snapshot without touching disk.
        Assert.False(page.HasUnsavedChanges);
        Assert.Equal("External edit", new WorkspacePageViewModel(() => { }, _ => null, Settings).WorkspaceName);
    }

    [Fact]
    public async Task Failed_workspace_creation_keeps_the_unsaved_indicator_and_can_be_retried()
    {
        var folder = temp.CreateSubdirectory("Root").FullName;
        var page = new WorkspacePageViewModel(() => { }, _ => folder, Settings);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        page.WorkspaceName = "Research";
        page.AddModuleCommand.Execute(null);
        page.Selected!.FolderName = "../outside";
        await page.SaveWorkspaceAsync();
        Assert.NotEmpty(page.Error);
        Assert.False(page.HasSavedWorkspace);
        Assert.True(page.ShowNewWorkspace);
        Assert.False(File.Exists(WorkspaceTestFiles.DefinitionPath(folder, "workspace")));
        page.Selected.Name = "Sorting";
        page.Selected.FolderName = "sorting";
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.True(page.HasSavedWorkspace);
        Assert.Equal("Sorting", WorkspaceFileStore.LoadDefinition(folder).Modules.Single().ModuleName);
    }

    [Fact]
    public async Task Creating_a_workspace_does_not_replace_one_added_since_selecting_the_folder()
    {
        var folder = temp.CreateSubdirectory("Root").FullName;
        var page = new WorkspacePageViewModel(() => { }, _ => folder, Settings);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        page.WorkspaceName = "My draft";
        WorkspaceFileStore.CreateWorkspace(folder, workspace.create(Guid.NewGuid(), "Already saved", "Root", []), CancellationToken.None);
        await page.SaveWorkspaceAsync();
        Assert.Contains("already exists", page.Error);
        Assert.True(page.HasSavedWorkspace);
        Assert.False(page.ShowNewWorkspace);
        Assert.Equal("Already saved", WorkspaceFileStore.LoadDefinition(folder).WorkspaceName);
    }

    [Fact]
    public void Unreadable_workspace_reports_the_problem_and_cannot_be_replaced_by_save()
    {
        var folder = temp.CreateSubdirectory("Root").FullName;
        var file = WorkspaceTestFiles.DefinitionPath(folder, "workspace");
        File.WriteAllBytes(file, [0xc1]);
        var page = new WorkspacePageViewModel(() => { }, _ => folder, Settings);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        Assert.False(page.HasSavedWorkspace);
        Assert.Contains("Could not load the saved workspace", page.Error);
        page.WorkspaceName = "My edit";
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.Equal(new byte[] { 0xc1 }, File.ReadAllBytes(file));
    }

    [Fact]
    public void Existing_folder_selection_is_retained_and_new_setting_takes_precedence()
    {
        var original = temp.CreateSubdirectory("Original").FullName;
        var replacement = temp.CreateSubdirectory("Replacement").FullName;
        var settingsDirectory = Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var legacyPath = Path.Combine(settingsDirectory.FullName, "projects-folder.txt");
        File.WriteAllText(legacyPath, original);

        var page = new WorkspacePageViewModel(() => { }, _ => replacement, Settings);
        Assert.Equal(original, page.WorkspaceFolder);
        Assert.Empty(page.Error);
        page.BrowseWorkspaceFolderCommand.Execute(null);

        Assert.Equal(replacement, File.ReadAllText(SettingsPath));
        Assert.Equal(replacement, Settings.Load());
        Assert.Equal(original, File.ReadAllText(legacyPath));
        Assert.Equal(replacement, new WorkspacePageViewModel(() => { }, _ => null, Settings).WorkspaceFolder);
    }

    [Fact]
    public void Selecting_a_folder_persists_it_and_cancel_or_invalid_selection_keeps_it()
    {
        var folder = temp.CreateSubdirectory("Workspace folder").FullName;
        var missing = Path.Combine(temp.FullName, "missing");
        var selections = new Queue<string?>([null, folder + Path.DirectorySeparatorChar, null, missing]);
        var initialFolders = new List<string?>();
        var page = new WorkspacePageViewModel(() => { }, current =>
        {
            initialFolders.Add(current);
            return selections.Dequeue();
        }, Settings);

        page.BrowseWorkspaceFolderCommand.Execute(null);
        Assert.Empty(page.WorkspaceFolder);
        Assert.False(File.Exists(SettingsPath));
        page.BrowseWorkspaceFolderCommand.Execute(null);
        Assert.Equal(folder, page.WorkspaceFolder);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.Empty(page.Error);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        Assert.Equal(folder, page.WorkspaceFolder);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        Assert.Equal(folder, page.WorkspaceFolder);
        Assert.Contains("Could not set", page.Error);
        Assert.Equal(new string?[] { null, null, folder, folder }, initialFolders);

        var reopened = new WorkspacePageViewModel(() => { }, _ => null, Settings);
        Assert.Equal(folder, reopened.WorkspaceFolder);
        Assert.Empty(reopened.Error);
    }

    [Fact]
    public void Failed_save_preserves_the_previous_setting_and_can_be_retried()
    {
        var first = temp.CreateSubdirectory("First").FullName;
        var second = temp.CreateSubdirectory("Second").FullName;
        Settings.Save(first);
        var page = new WorkspacePageViewModel(() => { }, _ => second, Settings);

        using (var lockedSettings = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            page.BrowseWorkspaceFolderCommand.Execute(null);
            Assert.Equal(first, page.WorkspaceFolder);
            Assert.Equal(first, Settings.Load());
            Assert.Contains("Could not set", page.Error);
        }
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(SettingsPath)!));
        page.BrowseWorkspaceFolderCommand.Execute(null);
        Assert.Equal(second, page.WorkspaceFolder);
        Assert.Equal(second, Settings.Load());
        Assert.Empty(page.Error);
    }

    [Fact]
    public void Unavailable_saved_folder_is_shown_and_can_be_replaced()
    {
        var missing = Path.Combine(temp.FullName, "unavailable");
        Settings.Save(missing);
        var replacement = temp.CreateSubdirectory("Available").FullName;
        var page = new WorkspacePageViewModel(() => { }, _ => replacement, Settings);
        Assert.Equal(missing, page.WorkspaceFolder);
        Assert.Contains("unavailable", page.Error);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        Assert.Equal(replacement, Settings.Load());
        Assert.Empty(page.Error);
    }

    [Fact]
    public void Unreadable_settings_report_an_error_without_preventing_navigation()
    {
        Directory.CreateDirectory(SettingsPath);
        bool returned = false;
        var page = new WorkspacePageViewModel(() => returned = true, _ => null, Settings);
        Assert.Empty(page.WorkspaceFolder);
        Assert.Contains("Could not load", page.Error);
        page.ReturnHomeCommand.Execute(null);
        Assert.True(returned);
    }

    [Fact]
    public void Workspace_selection_stays_on_the_page_and_survives_navigation()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var folder = temp.CreateSubdirectory("Workspace").FullName;
                using var shell = new MainWindowViewModel(_ => folder, Settings);
                var home = Assert.IsType<HomePageViewModel>(shell.CurrentPage);
                home.OpenWorkspaceCommand.Execute(null);
                var workspace = Assert.IsType<WorkspacePageViewModel>(shell.CurrentPage);
                workspace.BrowseWorkspaceFolderCommand.Execute(null);
                Assert.Same(workspace, shell.CurrentPage);
                Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
                workspace.ReturnHomeCommand.Execute(null);
                Assert.Same(home, shell.CurrentPage);
                home.OpenWorkspaceCommand.Execute(null);
                Assert.Same(workspace, shell.CurrentPage);
                Assert.Equal(folder, workspace.WorkspaceFolder);
                workspace.WorkspaceName = "Edited workspace";
                // With no confirmation callback, cancellation is the safe default for both routes.
                Assert.False(shell.TryCloseAsync().GetAwaiter().GetResult());
                workspace.ReturnHomeCommand.Execute(null);
                Assert.Same(workspace, shell.CurrentPage);
                Assert.True(workspace.HasUnsavedChanges);
            }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    public void Dispose() => temp.Delete(recursive: true);
}
