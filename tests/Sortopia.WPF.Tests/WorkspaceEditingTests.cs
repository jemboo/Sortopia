using System.IO;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.Serialization.ModuleDocument;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceEditingTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-editing-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private WorkspaceFolderSettings Settings => new(Path.Combine(temporary.FullName, "settings.txt"));
    private WorkspacePageViewModel Open(WorkspaceDocument? definition = null)
    {
        Directory.CreateDirectory(Root);
        if (definition is not null) WorkspaceFileStore.CreateDocument(Root, definition, TestContext.Current.CancellationToken);
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
    public async Task Folder_follows_renames_through_failed_saves_successful_saves_and_reopening(WorkspaceEntityKind kind)
    {
        var page = Open(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", []));
        page.AddModuleCommand.Execute(null);
        if (kind == WorkspaceEntityKind.Project)
        {
            page.AddProjectCommand.Execute(null);
            WorkspaceHierarchyTests.ConfigureProject(page.Selected!);
        }
        var node = page.Selected!;
        node.Name = "First name";
        Assert.True(node.HasNamedFolder);
        Assert.Equal("First name", node.Name);
        node.Name = "Second name";
        Assert.Equal("Second name", node.Name);
        using (var locked = new FileStream(WorkspaceTestFiles.DefinitionPath(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
        }
        node.Name = "Saved name";
        Assert.True(node.HasNamedFolder);
        Assert.Equal("Saved name", node.Name);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        node = Entity(page, kind);
        Assert.True(node.HasNamedFolder);
        var oldFolder = node.GetFolderPath(Root);
        File.WriteAllText(Path.Combine(oldFolder, "keep.txt"), "Keep contents");
        node.Name = "New display name";
        Assert.Equal("New display name", node.Name);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var reopened = new WorkspacePageViewModel(() => { }, _ => null, Settings);
        node = Entity(reopened, kind);
        Assert.Equal("New display name", node.Name);
        Assert.Equal("New display name", node.Name);
        Assert.False(Directory.Exists(oldFolder));
        Assert.Equal("Keep contents", File.ReadAllText(Path.Combine(node.GetFolderPath(Root), "keep.txt")));
        node.Name = "Another display name";
        Assert.Equal("Another display name", node.Name);
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Project)]
    public void Folder_path_is_derived_from_the_only_name_property(WorkspaceEntityKind kind)
    {
        var node = new WorkspaceTreeNode(kind, "New entity");
        node.Name = "First name";
        Assert.Equal("First name", node.PathName);
        node.Name = "Second name";
        Assert.Equal("Second name", node.PathName);
        node.Name = "Third name";
        Assert.Equal("Third name", node.PathName);
        Assert.True(node.HasNamedFolder);
        Assert.Null(typeof(WorkspaceTreeNode).GetProperty("FolderName"));
        Assert.Null(typeof(WorkspaceTreeNode).GetProperty("FolderNameInput"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task First_module_name_sets_the_location_even_after_saving_the_placeholder(bool branch, bool savePlaceholder)
    {
        var page = Open(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", []));
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        WorkspaceHierarchyTests.ConfigureProject(page.Selected!);
        var node = page.Selected!;
        var id = node.Id;
        var originalPath = node.GetFolderPath(Root);
        if (savePlaceholder)
        {
            await page.SaveWorkspaceAsync();
            Assert.Empty(page.Error);
            node = Entity(page, WorkspaceEntityKind.Module);
            File.WriteAllText(Path.Combine(originalPath, "notes.txt"), "Keep while naming");
        }
        node.NameInput = "Prefix tests";
        Assert.True(node.HasNamedFolder);
        Assert.Equal("Prefix tests", node.Name);
        Assert.Equal("Prefix tests", node.NameInput);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var saved = Entity(page, WorkspaceEntityKind.Module);
        Assert.Equal(id, saved.Id);
        Assert.True(saved.HasNamedFolder);
        Assert.Equal(Path.Combine(Root, "Prefix tests"), saved.GetFolderPath(Root));
        Assert.True(Directory.Exists(saved.GetFolderPath(Root)));
        Assert.False(Directory.Exists(originalPath));
        if (savePlaceholder) Assert.Equal("Keep while naming", File.ReadAllText(Path.Combine(saved.GetFolderPath(Root), "notes.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflicting_module_edits_are_rejected_against_saved_siblings_and_reported_immediately(bool nested)
    {
        WorkspaceModule[] siblings = [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Alpha", [], []), WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Beta", [], [])];
        var page = Open(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", nested ? [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "parent", siblings, [])] : siblings));
        var parent = nested ? page.Roots.Single().Children.Single() : page.Roots.Single();
        var beta = parent.Children[1];
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        beta.Name = "ALPHA";
        Assert.Equal("Beta", beta.Name);
        Assert.Equal("ALPHA", beta.NameInput);
        Assert.Equal("Beta", beta.PathName);
        Assert.Contains("saved workspace", beta.NameError);
        Assert.Contains("Duplicate name among module and project children", page.Error);
        Assert.True(page.HasUnsavedChanges);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        await page.SaveWorkspaceAsync();
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        beta.Name = "Beta";
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
    public async Task Saved_names_stay_reserved_until_removals_or_renames_are_saved(bool remove)
    {
        var page = Open(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Alpha", [], []), WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Beta", [], [])]));
        var parent = page.Roots.Single();
        var alpha = parent.Children[0];
        var beta = parent.Children[1];
        if (remove) { page.Selected = alpha; page.RemoveCommand.Execute(null); }
        else alpha.Name = "Gamma";
        beta.Name = "Alpha";
        Assert.Equal("Beta", beta.Name);
        Assert.Contains("saved workspace", beta.NameError);
        Assert.Contains("Save workspace", beta.NameError);
        beta.Name = "Beta";
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        beta = page.Roots.Single().Children.Single(child => child.Name == "Beta");
        beta.Name = "Alpha";
        Assert.Empty(page.Error);
        Assert.Equal("Alpha", beta.Name);
        Assert.Equal("Alpha", beta.PathName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removed_project_name_can_be_reused_by_a_module_after_saving_removal(bool savedProject)
    {
        var page = Open();
        page.AddModuleCommand.Execute(null);
        page.Selected!.Name = "SortableTests";
        page.AddProjectCommand.Execute(null);
        page.Selected!.Name = "Prefix";
        var projectId = page.Selected.Id;
        var folder = page.Selected.GetFolderPath(Root);
        if (savedProject)
        {
            await page.SaveWorkspaceAsync();
            Assert.Empty(page.OperationError);
            page.Selected = page.Roots[0].DescendantsAndSelf().Single(node => node.Id == projectId);
            File.WriteAllText(Path.Combine(folder, "old-project.txt"), "Removed with project");
        }
        page.RemoveCommand.Execute(null);
        if (savedProject)
        {
            Assert.True(page.HasPendingRemoval);
            Assert.False(page.AddModuleCommand.CanExecute(null));
            Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
            await page.SaveWorkspaceAsync();
            Assert.Empty(page.OperationError);
            Assert.False(Directory.Exists(folder));
            page.Selected = page.Roots[0].Children[0];
        }
        Assert.False(page.HasPendingRemoval);
        page.AddModuleCommand.Execute(null);
        var replacement = page.Selected!;
        var replacementId = replacement.Id;
        replacement.NameInput = "Prefix";
        Assert.Empty(replacement.NameError);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.OperationError);
        Assert.Equal("module", WorkspaceDefinitionFiles.TryKind(folder));
        Assert.False(File.Exists(Path.Combine(folder, WorkspaceDefinitionFiles.Name(projectId))));
        Assert.False(File.Exists(Path.Combine(folder, "old-project.txt")));
        var reopened = Open();
        var module = Assert.Single(reopened.Roots[0].Children[0].Children);
        Assert.True(module.Kind == WorkspaceEntityKind.Module);
        Assert.Equal("Prefix", module.Name);
        Assert.Equal(replacementId, module.Id);
        Assert.False(reopened.HasUnsavedChanges);
    }

    [Fact]
    public void Automatic_folder_collision_rejects_the_rename_until_a_unique_name_is_chosen()
    {
        var page = Open(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "reserved", [], [])]));
        page.AddModuleCommand.Execute(null);
        var node = page.Selected!;
        var original = node.Name;
        node.Name = "reserved";
        Assert.Equal(original, node.Name);
        Assert.Equal(original, node.PathName);
        Assert.True(node.HasNamedFolder);
        Assert.Contains("Duplicate name", node.NameError);
        node.Name = "Unique name";
        Assert.Equal("Unique name", node.Name);
        Assert.Empty(page.Error);
    }

    [Fact]
    public void Discard_restores_the_saved_hierarchy_names_folders_and_run_settings_without_writing()
    {
        var definition = WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "sorting", [],
            [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "search", [WorkspaceHierarchyTests.MakeRun("Search", "Trial")])])]);
        var page = Open(definition);
        var before = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        Assert.False(page.DiscardChangesCommand.CanExecute(null));
        page.WorkspaceName = "Changed workspace";
        var module = page.Roots.Single().Children.Single();
        module.Name = "Changed module";
        var run = module.Children.Single().Children.Single();
        run.Name = "Changed run";
        page.Selected = module;
        page.AddProjectCommand.Execute(null);
        WorkspaceHierarchyTests.ConfigureProject(page.Selected!);
        run.Editor!.ResultsParameters.Clear();
        Assert.True(page.DiscardChangesCommand.CanExecute(null));
        page.DiscardChangesCommand.Execute(null);
        Assert.True(WorkspaceDocumentModule.sameContent(definition, page.Roots.Single().ToDocument()));
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
        var page = Open(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "alpha", [], []), WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "beta", [], [])]));
        page.Roots.Single().Children[1].Name = "Alpha";
        Assert.True(page.HasUnsavedChanges);
        Assert.True(page.HasEditErrors);
        Assert.True(page.DiscardChangesCommand.CanExecute(null));
        page.DiscardChangesCommand.Execute(null);
        Assert.Empty(page.Error);
        Assert.False(page.HasEditErrors);
        Assert.Equal("beta", page.Roots.Single().Children[1].NameInput);
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
    public async Task Removing_a_module_deletes_its_folder_on_save()
    {
        var page = Open(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "sorting", [], [])]));
        page.Selected = page.Roots.Single().Children.Single();
        page.RemoveCommand.Execute(null);
        Assert.Contains("delete", page.Status);
        Assert.True(Directory.Exists(Path.Combine(Root, "sorting")));
        page.DiscardChangesCommand.Execute(null);
        Assert.True(Directory.Exists(Path.Combine(Root, "sorting")));
        page.Selected = page.Roots.Single().Children.Single();
        page.RemoveCommand.Execute(null);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.False(page.HasRetainedFolders);
        page.WorkspaceName = "Edited";
        page.DiscardChangesCommand.Execute(null);
        Assert.False(page.HasRetainedFolders);
        Assert.False(Directory.Exists(Path.Combine(Root, "sorting")));
    }

    [Fact]
    public async Task Adding_is_paused_until_removal_is_saved_and_the_default_name_is_available_again()
    {
        var page = Open(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "New module", [], [])]));
        page.Selected = page.Roots.Single().Children.Single();
        page.RemoveCommand.Execute(null);
        Assert.False(page.AddModuleCommand.CanExecute(null));
        page.AddModuleCommand.Execute(null);
        Assert.Empty(page.Roots[0].Children);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.OperationError);
        Assert.True(page.AddModuleCommand.CanExecute(null));
        page.AddModuleCommand.Execute(null);
        Assert.Equal("New module", page.Selected!.Name);
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Module, false)]
    [InlineData(WorkspaceEntityKind.Project, false)]
    [InlineData(WorkspaceEntityKind.Run, false)]
    [InlineData(WorkspaceEntityKind.Module, true)]
    [InlineData(WorkspaceEntityKind.Project, true)]
    [InlineData(WorkspaceEntityKind.Run, true)]
    public async Task Pending_removal_keeps_edits_paused_after_failed_save_until_retry_or_discard(WorkspaceEntityKind kind, bool discard)
    {
        var page = Open(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace",
            [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "sorting", [],
                [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "search", [WorkspaceHierarchyTests.MakeRun("search", "Trial")])])]));
        page.Selected = Entity(page, kind);
        var removed = page.Selected;
        var path = removed.GetFolderPath(Root);
        var parentFile = WorkspaceDefinitionFiles.Require(removed.Parent!.GetFolderPath(Root), removed.Parent.Kind.ToString().ToLowerInvariant());
        page.RemoveCommand.Execute(null);
        Assert.True(page.HasPendingRemoval);
        Assert.False(page.CanModifyWorkspace);
        Assert.Contains(removed.Name, page.PendingRemovalMessage);
        Assert.Contains("Save workspace", page.PendingRemovalMessage);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.True(page.DiscardChangesCommand.CanExecute(null));
        foreach (var node in page.Roots[0].DescendantsAndSelf())
        {
            page.Selected = node;
            foreach (var command in new[] { page.AddModuleCommand, page.AddBranchModuleCommand, page.AddProjectCommand,
                page.AddRunCommand, page.AddExistingModuleCommand, page.AddExistingProjectCommand, page.CutCommand,
                page.PasteCommand, page.RemoveCommand, page.CreateProjectVersionCommand, page.ResumeProjectEditingCommand,
                page.GenerateRunParametersCommand, page.ExecutePageCommand })
                Assert.False(command.CanExecute(null));
        }
        using (var locked = new FileStream(parentFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.OperationError);
            Assert.True(page.HasPendingRemoval);
            Assert.False(page.CanModifyWorkspace);
            Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
            Assert.True(page.DiscardChangesCommand.CanExecute(null));
            Assert.True(File.Exists(path) || Directory.Exists(path));
        }
        if (discard) page.DiscardChangesCommand.Execute(null);
        else await page.SaveWorkspaceAsync();
        Assert.Empty(page.OperationError);
        Assert.False(page.HasPendingRemoval);
        Assert.Empty(page.PendingRemovalMessage);
        Assert.True(page.CanModifyWorkspace);
        Assert.Equal(discard, File.Exists(path) || Directory.Exists(path));
        Assert.Equal(discard, page.Roots[0].DescendantsAndSelf().Any(node => node.Id == removed.Id));
    }

    [Fact]
    public void Removal_requires_other_name_errors_to_be_fixed_but_can_remove_the_invalid_item_itself()
    {
        var page = Open(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace",
            [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Alpha", [], []),
             WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Beta", [], [])]));
        var alpha = page.Roots[0].Children[0];
        var beta = page.Roots[0].Children[1];
        beta.NameInput = "";
        page.Selected = alpha;
        Assert.False(page.RemoveCommand.CanExecute(null));
        Assert.Contains("Fix name errors", page.RemoveHint);
        page.RemoveCommand.Execute(null);
        Assert.False(page.HasPendingRemoval);
        Assert.Equal(2, page.Roots[0].Children.Count);
        page.Selected = beta;
        Assert.True(page.RemoveCommand.CanExecute(null));
        page.RemoveCommand.Execute(null);
        Assert.True(page.HasPendingRemoval);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
