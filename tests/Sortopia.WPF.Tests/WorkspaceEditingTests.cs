using System.IO;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.workspaceModule;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceEditingTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-editing-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private WorkspaceFolderSettings Settings => new(Path.Combine(temporary.FullName, "settings.txt"));
    private WorkspacePageViewModel Open(workspace? definition = null)
    {
        Directory.CreateDirectory(Root);
        if (definition is not null) WorkspaceFileStore.CreateWorkspace(Root, definition, TestContext.Current.CancellationToken);
        var page = new WorkspacePageViewModel(() => { }, _ => Root, Settings);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        return page;
    }
    private static WorkspaceTreeNode Entity(WorkspacePageViewModel page, WorkspaceEntityKind kind)
    {
        var node = page.Roots.Single().Children.Single();
        while (node.Kind != kind) node = node.Children.Single();
        return node;
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Project)]
    public async Task Folder_follows_name_through_failed_save_then_stops_after_first_successful_save(WorkspaceEntityKind kind)
    {
        var page = Open(workspace.create(Guid.NewGuid(), "Research", "Workspace", []));
        page.AddModuleCommand.Execute(null);
        if (kind == WorkspaceEntityKind.Project)
        {
            page.AddProjectCommand.Execute(null);
            WorkspaceHierarchyTests.ConfigureProject(page.Selected!);
        }
        var node = page.Selected!;
        node.Name = "First name";
        Assert.True(node.FolderFollowsName);
        Assert.Equal("First name", node.FolderName);
        node.Name = "Second name";
        Assert.Equal("Second name", node.FolderName);
        using (var locked = new FileStream(WorkspaceTestFiles.DefinitionPath(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
        }
        node.Name = "Saved name";
        Assert.True(node.FolderFollowsName);
        Assert.Equal("Saved name", node.FolderName);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        node = Entity(page, kind);
        Assert.False(node.FolderFollowsName);
        node.Name = "New display name";
        Assert.Equal("Saved name", node.FolderName);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var reopened = new WorkspacePageViewModel(() => { }, _ => null, Settings);
        node = Entity(reopened, kind);
        Assert.Equal("New display name", node.Name);
        Assert.Equal("Saved name", node.FolderName);
        node.Name = "Another display name";
        Assert.Equal("Saved name", node.FolderName);
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Project)]
    public void Manually_editing_the_folder_stops_following_even_if_it_later_matches_the_name(WorkspaceEntityKind kind)
    {
        var node = new WorkspaceTreeNode(kind, "New entity");
        node.Name = "First name";
        Assert.Equal("First name", node.FolderName);
        node.FolderName = "custom-folder";
        node.Name = "Second name";
        Assert.Equal("custom-folder", node.FolderName);
        node.FolderName = "Second name";
        node.Name = "Third name";
        Assert.Equal("Second name", node.FolderName);
        Assert.False(node.FolderFollowsName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflicting_module_edits_are_rejected_against_saved_siblings_and_reported_immediately(bool nested)
    {
        WorkspaceModule[] siblings = [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Alpha", "alpha-folder", [], []), WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Beta", "beta-folder", [], [])];
        var page = Open(workspace.create(Guid.NewGuid(), "Research", "Workspace", nested ? [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Parent", "parent", siblings, [])] : siblings));
        var parent = nested ? page.Roots.Single().Children.Single() : page.Roots.Single();
        var beta = parent.Children[1];
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        beta.Name = "ALPHA";
        Assert.Equal("Beta", beta.Name);
        Assert.Equal("ALPHA", beta.NameInput);
        Assert.Equal("beta-folder", beta.FolderName);
        Assert.Contains("saved workspace", beta.NameError);
        Assert.Contains("Duplicate name in modules", page.Error);
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        await page.SaveWorkspaceAsync();
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        beta.Name = "Beta";
        Assert.Empty(page.Error);
        beta.FolderName = "ALPHA-FOLDER";
        Assert.Equal("beta-folder", beta.FolderName);
        Assert.Contains("Duplicate folder name in modules", beta.FolderNameError);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        beta.FolderName = "beta-folder";
        Assert.Empty(page.Error);
        // Case-only edits to the same entity do not collide with its own saved identity.
        beta.Name = "BETA";
        Assert.Empty(beta.NameError);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_names_and_folders_stay_reserved_until_removals_or_renames_are_saved(bool remove)
    {
        var page = Open(workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Alpha", "alpha-folder", [], []), WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Beta", "beta-folder", [], [])]));
        var parent = page.Roots.Single();
        var alpha = parent.Children[0];
        var beta = parent.Children[1];
        if (remove) { page.Selected = alpha; page.RemoveCommand.Execute(null); }
        else { alpha.Name = "Gamma"; alpha.FolderName = "gamma-folder"; }
        beta.Name = "Alpha";
        Assert.Equal("Beta", beta.Name);
        Assert.Contains("saved workspace", beta.NameError);
        beta.Name = "Beta";
        beta.FolderName = "alpha-folder";
        Assert.Equal("beta-folder", beta.FolderName);
        Assert.Contains("saved workspace", beta.FolderNameError);
        beta.FolderName = "beta-folder";
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        beta = page.Roots.Single().Children.Single(child => child.Name == "Beta");
        beta.Name = "Alpha";
        beta.FolderName = "alpha-folder";
        Assert.Empty(page.Error);
        Assert.Equal("Alpha", beta.Name);
        Assert.Equal("alpha-folder", beta.FolderName);
    }

    [Fact]
    public void Automatic_folder_collision_rejects_both_changes_and_can_be_resolved_with_a_manual_folder()
    {
        var page = Open(workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Existing", "reserved", [], [])]));
        page.AddModuleCommand.Execute(null);
        var node = page.Selected!;
        var original = node.Name;
        node.Name = "reserved";
        Assert.Equal(original, node.Name);
        Assert.Equal(original, node.FolderName);
        Assert.True(node.FolderFollowsName);
        Assert.Contains("Duplicate folder name", node.NameError);
        node.FolderName = "unique-folder";
        Assert.Equal("reserved", node.Name);
        Assert.Equal("unique-folder", node.FolderName);
        Assert.Empty(page.Error);
    }

    [Fact]
    public void Discard_restores_the_saved_hierarchy_names_folders_and_run_settings_without_writing()
    {
        var definition = workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Sorting", "sorting", [],
            [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "Search", "search", [WorkspaceHierarchyTests.MakeRun("Search", "Trial")])])]);
        var page = Open(definition);
        var before = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        Assert.False(page.DiscardChangesCommand.CanExecute(null));
        page.WorkspaceName = "Changed workspace";
        var module = page.Roots.Single().Children.Single();
        module.Name = "Changed module";
        module.FolderName = "another-folder";
        var run = module.Children.Single().Children.Single();
        run.Name = "Changed run";
        page.Selected = module;
        page.AddProjectCommand.Execute(null);
        WorkspaceHierarchyTests.ConfigureProject(page.Selected!);
        run.Editor!.OutputParameters.Clear();
        Assert.True(page.DiscardChangesCommand.CanExecute(null));
        page.DiscardChangesCommand.Execute(null);
        Assert.Equal(WorkspaceCodec.encodeWorkspace(definition), WorkspaceCodec.encodeWorkspace(page.Roots.Single().ToWorkspace()));
        Assert.Same(page.Roots.Single(), page.Selected);
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.DiscardChangesCommand.CanExecute(null));
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.Empty(page.Error);
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Discard_clears_rejected_edits_even_when_the_accepted_definition_has_not_changed()
    {
        var page = Open(workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Alpha", "alpha", [], []), WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Beta", "beta", [], [])]));
        page.Roots.Single().Children[1].Name = "Alpha";
        Assert.False(page.HasUnsavedChanges);
        Assert.True(page.HasEditErrors);
        Assert.True(page.DiscardChangesCommand.CanExecute(null));
        page.DiscardChangesCommand.Execute(null);
        Assert.Empty(page.Error);
        Assert.False(page.HasEditErrors);
        Assert.Equal("Beta", page.Roots.Single().Children[1].NameInput);
        Assert.False(page.DiscardChangesCommand.CanExecute(null));
    }

    [Fact]
    public void Discarding_a_new_workspace_restores_its_empty_draft_without_creating_files()
    {
        var page = Open();
        Assert.False(page.DiscardChangesCommand.CanExecute(null));
        page.WorkspaceName = "";
        page.AddModuleCommand.Execute(null);
        page.DiscardChangesCommand.Execute(null);
        Assert.Equal("Workspace", page.WorkspaceName);
        Assert.Empty(page.Roots.Single().Children);
        Assert.Empty(Directory.GetFileSystemEntries(Root));
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.DiscardChangesCommand.CanExecute(null));
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_reports_retained_folders_and_discard_clears_the_report()
    {
        var page = Open(workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Sorting", "sorting", [], [])]));
        page.Selected = page.Roots.Single().Children.Single();
        page.RemoveCommand.Execute(null);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.True(page.HasRetainedFolders);
        Assert.Contains(Path.Combine("modules", "sorting"), page.RetainedFolders);
        page.WorkspaceName = "Edited";
        page.DiscardChangesCommand.Execute(null);
        Assert.False(page.HasRetainedFolders);
        Assert.True(Directory.Exists(Path.Combine(Root, "modules", "sorting")));
    }

    [Fact]
    public void New_entity_defaults_skip_saved_names_even_after_the_entity_is_removed_from_the_draft()
    {
        var page = Open(workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "New module", "New module", [], [])]));
        page.Selected = page.Roots.Single().Children.Single();
        page.RemoveCommand.Execute(null);
        page.AddModuleCommand.Execute(null);
        Assert.Equal("New module 2", page.Selected!.Name);
        Assert.Equal("New module 2", page.Selected.FolderName);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
