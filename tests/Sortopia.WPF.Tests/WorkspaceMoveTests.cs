using System.IO;
using System.Security.Cryptography;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.workspaceModule;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceMoveTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-move-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private string? importSource;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private WorkspacePageViewModel Open(bool saved = true)
    {
        var value = workspace.create(Guid.NewGuid(), "Research", "Workspace", [
            WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "A", "A", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Nested", "Nested", [], [])],
                [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "P", "P", [WorkspaceHierarchyTests.MakeRun("P", "R")])]),
            WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "B", "B", [], [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "Destination", "Destination", [])])]);
        Directory.CreateDirectory(Root);
        if (saved) WorkspaceFileStore.CreateWorkspace(Root, value, Token);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Root);
        return new(() => { }, _ => null, settings, () => WorkspaceUnsavedChoice.Discard, _ => importSource);
    }
    private static WorkspaceTreeNode Find(WorkspacePageViewModel page, string name) => page.Roots.Single().DescendantsAndSelf().Single(node => node.Name == name);
    private static string Snapshot(string folder) => string.Join("\n", Directory.GetDirectories(folder, "*", SearchOption.AllDirectories)
        .Select(path => "D:" + Path.GetRelativePath(folder, path)).Concat(Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .Select(path => "F:" + Path.GetRelativePath(folder, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))).Order());
    private static void Data(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "output", "empty"));
        File.WriteAllText(Path.Combine(folder, "output", "result.txt"), "Keep these results");
    }
    private static async Task Move(WorkspacePageViewModel page, string name, string parent)
    {
        page.Selected = Find(page, name);
        Assert.True(page.CutCommand.CanExecute(null));
        page.CutCommand.Execute(null);
        page.Selected = Find(page, parent);
        Assert.True(page.PasteCommand.CanExecute(null));
        await page.PasteAsync();
        Assert.Empty(page.Error);
        Assert.False(page.HasCutItem);
        Assert.Equal(parent, page.Selected!.Parent!.Name);
    }

    [Theory]
    [InlineData("A", "B")]
    [InlineData("Nested", "Research")]
    [InlineData("P", "B")]
    [InlineData("R", "Destination")]
    public async Task Paste_reparents_the_draft_and_save_moves_all_contents_with_stable_guid_filenames(string name, string parent)
    {
        var page = Open();
        var node = Find(page, name);
        var source = node.GetFolderPath(Root);
        if (!node.IsRun) Data(source);
        var filenames = node.IsRun ? [Path.GetFileName(source)] : Directory.GetFiles(source, "*.msgpack", SearchOption.AllDirectories).Select(Path.GetFileName).Order().ToArray();
        var before = Snapshot(Root);
        if (node.IsRun)
        {
            page.Selected = node; page.CutCommand.Execute(null);
            page.Selected = Find(page, parent); await page.PasteAsync();
            Assert.Contains("parent project", page.Error);
            Assert.Equal(before, Snapshot(Root));
            return;
        }
        await Move(page, name, parent);
        var destination = page.Selected!.GetFolderPath(Root);
        Assert.Equal(before, Snapshot(Root));
        Assert.Equal("Pending move", page.Selected.DiskStatus);
        Assert.True(page.HasUnsavedChanges);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.False(Directory.Exists(source) || File.Exists(source));
        if (node.IsRun)
        {
            Assert.True(File.Exists(destination));
            Assert.Equal(filenames.Single(), Path.GetFileName(destination));
            var savedRun = WorkspaceHierarchyTests.ReadRun(destination);
            Assert.Equal(node.Id, savedRun.Id);
            Assert.Equal("Destination", savedRun.ProjectName);
        }
        else
        {
            Assert.True(Directory.Exists(Path.Combine(destination, "output", "empty")));
            Assert.Equal("Keep these results", File.ReadAllText(Path.Combine(destination, "output", "result.txt")));
            Assert.Equal(filenames, Directory.GetFiles(destination, "*.msgpack", SearchOption.AllDirectories).Select(Path.GetFileName).Order());
        }
        Assert.Empty(page.RetainedFolders);
        Assert.Empty(Directory.GetDirectories(Root, ".sortopia-*") );
        Assert.False(page.HasMissingComponents);
    }

    [Fact]
    public async Task A_failed_parent_move_restores_run_files_and_can_be_retried()
    {
        var page = Open();
        var source = Find(page, "R").GetFolderPath(Root);
        var id = Find(page, "R").Id;
        var before = Snapshot(Root);
        await Move(page, "P", "B");
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Find(page, "B").GetFolderPath(Root), "module"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
        }
        Assert.Equal(before, Snapshot(Root));
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.False(File.Exists(source));
        var saved = WorkspaceHierarchyTests.ReadRun(Find(page, "R").GetFolderPath(Root));
        Assert.Equal(id, saved.Id);
        Assert.Equal("P", saved.ProjectName);
    }

    [Theory]
    [InlineData("A", "Relocated")]
    [InlineData("Nested", "Renamed nested")]
    [InlineData("A", "a")]
    public async Task Editing_module_location_moves_its_entire_folder_on_save(string name, string folder)
    {
        var page = Open();
        var node = Find(page, name);
        var source = node.GetFolderPath(Root);
        Data(source);
        var before = Snapshot(Root);
        var filenames = Directory.GetFiles(source, "*.msgpack", SearchOption.AllDirectories).Select(Path.GetFileName).Order().ToArray();
        node.FolderNameInput = folder;
        var destination = node.GetFolderPath(Root);
        page.RefreshDiskStatus();
        Assert.Equal("Pending move", node.DiskStatus);
        Assert.All(node.DescendantsAndSelf(), child => Assert.Equal("Pending move", child.DiskStatus));
        Assert.Equal(before, Snapshot(Root));
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Empty(page.RetainedFolders);
        Assert.Equal(node.Id, Find(page, name).Id);
        Assert.Equal(folder, WorkspaceFileStore.LoadModuleFolder(destination, Token).FolderName);
        // Enumerating checks the actual spelling on disk, including a case-only rename.
        Assert.Contains(destination, Directory.GetDirectories(Path.GetDirectoryName(destination)!));
        Assert.DoesNotContain(source, Directory.GetDirectories(Path.GetDirectoryName(source)!));
        Assert.Equal("Keep these results", File.ReadAllText(Path.Combine(destination, "output", "result.txt")));
        Assert.True(Directory.Exists(Path.Combine(destination, "output", "empty")));
        Assert.Equal(filenames, Directory.GetFiles(destination, "*.msgpack", SearchOption.AllDirectories).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Parent_and_child_location_edits_rollback_together_and_can_be_retried()
    {
        var page = Open();
        var parent = Find(page, "A");
        var child = Find(page, "Nested");
        var source = parent.GetFolderPath(Root);
        Data(source);
        Data(child.GetFolderPath(Root));
        var before = Snapshot(Root);
        parent.FolderNameInput = "Renamed parent";
        child.FolderNameInput = "Renamed child";
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
        }
        Assert.Equal(before, Snapshot(Root));
        Assert.True(page.HasUnsavedChanges);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.False(Directory.Exists(source));
        Assert.Equal("Keep these results", File.ReadAllText(Path.Combine(Find(page, "A").GetFolderPath(Root), "output", "result.txt")));
        Assert.Equal("Keep these results", File.ReadAllText(Path.Combine(Find(page, "Nested").GetFolderPath(Root), "output", "result.txt")));
        Assert.Empty(Directory.GetDirectories(Root, ".sortopia-*"));
    }

    [Fact]
    public async Task Location_edits_cannot_merge_into_an_existing_folder()
    {
        var page = Open();
        var node = Find(page, "A");
        node.FolderNameInput = "Occupied";
        var destination = node.GetFolderPath(Root);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "external.txt"), "Leave intact");
        var before = Snapshot(Root);
        await page.SaveWorkspaceAsync();
        Assert.Contains("already exists", page.Error);
        Assert.Equal(before, Snapshot(Root));
    }

    [Fact]
    public async Task Cut_alone_does_not_dirty_or_remove_the_node_and_discard_cancels_pasted_moves()
    {
        var page = Open();
        var before = Snapshot(Root);
        var node = Find(page, "P");
        page.Selected = node;
        page.CutCommand.Execute(null);
        Assert.True(node.IsCut);
        Assert.Same(Find(page, "A"), node.Parent);
        Assert.False(page.HasUnsavedChanges);
        page.CancelCutCommand.Execute(null);
        Assert.False(node.IsCut);
        Assert.False(page.HasCutItem);
        await Move(page, "P", "B");
        page.DiscardChangesCommand.Execute(null);
        Assert.Equal("A", Find(page, "P").Parent!.Name);
        Assert.False(page.HasCutItem);
        Assert.False(page.HasUnsavedChanges);
        Assert.Equal(before, Snapshot(Root));
    }

    [Fact]
    public async Task Invalid_parents_cycles_and_duplicate_names_or_folders_cannot_change_the_tree()
    {
        var page = Open();
        Assert.False(page.CutCommand.CanExecute(null));
        page.Selected = Find(page, "A"); page.CutCommand.Execute(null);
        foreach (var target in new[] { "A", "Nested", "P", "R" })
        { page.Selected = Find(page, target); Assert.False(page.PasteCommand.CanExecute(null)); }
        page.CancelCutCommand.Execute(null);
        page.Selected = Find(page, "B"); page.AddProjectCommand.Execute(null); WorkspaceHierarchyTests.ConfigureProject(page.Selected!);
        page.Selected!.Name = "P";
        page.Selected.FolderName = "Different folder";
        var node = Find(page, "A").Children.Single(child => child.Name == "P");
        page.Selected = node; page.CutCommand.Execute(null); page.Selected = Find(page, "B");
        await page.PasteAsync();
        Assert.Contains("Duplicate name", page.Error);
        Assert.True(page.HasCutItem);
        Assert.Equal("A", node.Parent!.Name);
        var collision = Find(page, "B").Children.Single(child => child.Name == "P");
        collision.Name = "Other"; collision.FolderName = "P";
        await page.PasteAsync();
        Assert.Contains("Duplicate folder name", page.Error);
        Assert.Equal("A", node.Parent!.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Occupied_destinations_are_rejected_before_changing_the_draft(bool file)
    {
        var page = Open();
        var destination = Path.Combine(Root, "B", "P");
        if (file) File.WriteAllText(destination, "keep"); else Directory.CreateDirectory(destination);
        var before = Snapshot(Root);
        page.Selected = Find(page, "P"); page.CutCommand.Execute(null); page.Selected = Find(page, "B");
        await page.PasteAsync();
        Assert.Contains("already exists", page.Error);
        Assert.Equal("A", Find(page, "P").Parent!.Name);
        Assert.False(page.HasUnsavedChanges);
        Assert.Equal(before, Snapshot(Root));
    }

    [Fact]
    public async Task Nested_moves_rollback_every_folder_and_definition_on_failure_and_can_be_retried()
    {
        var page = Open();
        Data(Find(page, "P").GetFolderPath(Root));
        var before = Snapshot(Root);
        await Move(page, "P", "B");
        await Move(page, "A", "B");
        page.Selected = Find(page, "A"); page.AddProjectCommand.Execute(null); WorkspaceHierarchyTests.ConfigureProject(page.Selected!); page.Selected!.Name = "New after move";
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
        }
        Assert.Equal(before, Snapshot(Root));
        Assert.True(page.HasUnsavedChanges);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.False(Directory.Exists(Path.Combine(Root, "A")));
        Assert.True(Directory.Exists(Path.Combine(Root, "B", "A", "New after move")));
        Assert.Equal("Keep these results", File.ReadAllText(Path.Combine(Root, "B", "P", "output", "result.txt")));
    }

    [Fact]
    public async Task Pasting_back_to_the_original_parent_and_same_parent_reordering_preserve_disk_contents()
    {
        var page = Open();
        var before = Snapshot(Root);
        await Move(page, "P", "B");
        await Move(page, "P", "A");
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Equal(before, Snapshot(Root));
        await Move(page, "A", "Research");
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Equal(new[] { "B", "A" }, WorkspaceFileStore.LoadDefinition(Root).Modules.Select(module => module.ModuleName));
    }

    [Fact]
    public async Task New_entities_can_be_reparented_before_the_first_save()
    {
        var page = Open(saved: false);
        page.AddModuleCommand.Execute(null); page.Selected!.Name = "A";
        page.AddModuleCommand.Execute(null); page.Selected!.Name = "Nested";
        await Move(page, "Nested", "Workspace");
        Assert.Empty(Directory.GetFileSystemEntries(Root));
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Equal(2, WorkspaceFileStore.LoadDefinition(Root).Modules.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moves_and_imports_nested_together_are_transactional(bool moveIntoImport)
    {
        var page = Open();
        importSource = Path.Combine(temporary.FullName, "Imported");
        WorkspaceImportTests.WriteModule(importSource, "Imported");
        var sourceBefore = Snapshot(importSource);
        var before = Snapshot(Root);
        if (moveIntoImport)
        {
            await page.AddExistingAsync(WorkspaceEntityKind.Module);
            await Move(page, "P", "Imported");
        }
        else
        {
            await Move(page, "A", "B");
            await page.AddExistingAsync(WorkspaceEntityKind.Module);
        }
        Assert.Empty(page.Error);
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
        }
        Assert.Equal(before, Snapshot(Root));
        Assert.Equal(sourceBefore, Snapshot(importSource));
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Equal(sourceBefore, Snapshot(importSource));
        Assert.Empty(Directory.GetDirectories(Root, ".sortopia-*"));
    }

    [Fact]
    public async Task A_child_cut_out_of_a_pending_import_is_copied_once_to_its_new_parent()
    {
        var page = Open();
        var beforeRoot = Snapshot(Root);
        importSource = Path.Combine(temporary.FullName, "Imported");
        WorkspaceImportTests.WriteModule(importSource, "Imported");
        var before = Snapshot(importSource);
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        await Move(page, "Child project", "B");
        var importedProjectId = page.Selected!.Id;
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
        }
        Assert.Equal(beforeRoot, Snapshot(Root));
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Equal(before, Snapshot(importSource));
        Assert.False(Directory.Exists(Path.Combine(Root, "Imported", "child-project")));
        var importedRun = WorkspaceHierarchyTests.ReadRun(Assert.Single(Directory.GetFiles(Path.Combine(Root, "B", "child-project", "runs"))));
        Assert.Equal(importedProjectId, importedRun.ParentProjectId);
        Assert.Equal("Keep results", File.ReadAllText(Path.Combine(Root, "B", "child-project", "results.txt")));
    }

    [Fact]
    public async Task A_destination_created_after_paste_cannot_be_overwritten_during_save()
    {
        var page = Open();
        await Move(page, "P", "B");
        var target = page.Selected!.GetFolderPath(Root);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "external.txt"), "external data");
        var before = Snapshot(Root);
        await page.SaveWorkspaceAsync();
        Assert.Contains("already exists", page.Error);
        Assert.Equal(before, Snapshot(Root));
        Assert.True(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Location_edits_within_a_pending_module_import_keep_the_contents_with_each_module()
    {
        var page = Open();
        importSource = Path.Combine(temporary.FullName, "Imported");
        WorkspaceImportTests.WriteModule(importSource, "Imported");
        var innerFolder = Path.Combine(importSource, "Inner");
        WorkspaceImportTests.WriteModule(innerFolder, "Inner");
        var innerDefinition = WorkspaceFileStore.LoadModuleFolder(innerFolder, Token);
        var outerFile = WorkspaceDefinitionFiles.Require(importSource, "module");
        var outer = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(outerFile));
        outer.modules = [.. outer.modules, new childFolderReferenceDto { id = innerDefinition.Id, folderName = "Inner" }];
        File.WriteAllBytes(outerFile, Sortopia.Core.Serialization.MessagePackSetup.serialize(outer));
        var before = Snapshot(importSource);
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(page.Error);
        Find(page, "Imported").FolderNameInput = "Relocated import";
        var inner = Find(page, "Inner");
        inner.FolderNameInput = "Renamed inner";
        var id = inner.Id;
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Equal(before, Snapshot(importSource));
        Assert.Equal(id, Find(page, "Inner").Id);
        var destination = Find(page, "Inner").GetFolderPath(Root);
        Assert.Equal("Keep results", File.ReadAllText(Path.Combine(destination, "child-project", "results.txt")));
        Assert.False(Directory.Exists(Path.Combine(Root, "Imported")));
        Assert.False(Directory.Exists(Path.Combine(Root, "Relocated import", "Inner")));
        Assert.Empty(page.RetainedFolders);
    }

    [Fact]
    public async Task Pending_moves_still_report_a_missing_source_definition_and_save_restores_it()
    {
        var page = Open();
        var source = Find(page, "P").GetFolderPath(Root);
        Data(source);
        await Move(page, "P", "B");
        File.Delete(WorkspaceDefinitionFiles.Require(source, "project"));
        page.RefreshDiskStatus();
        Assert.Equal("Missing definition", page.Selected!.DiskStatus);
        Assert.True(page.HasMissingComponents);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.False(page.HasMissingComponents);
        Assert.Equal("Keep these results", File.ReadAllText(Path.Combine(Find(page, "P").GetFolderPath(Root), "output", "result.txt")));
    }

    [Fact]
    public async Task Leaving_or_removing_a_cut_ancestor_clears_the_workspace_clipboard()
    {
        var page = Open();
        page.Selected = Find(page, "P"); page.CutCommand.Execute(null);
        Assert.True(await page.ReturnHomeAsync());
        Assert.False(page.HasCutItem);
        Assert.False(Find(page, "P").IsCut);
        page.CutCommand.Execute(null);
        page.Selected = Find(page, "A"); page.RemoveCommand.Execute(null);
        Assert.False(page.HasCutItem);
        Assert.False(page.PasteCommand.CanExecute(null));
        page.DiscardChangesCommand.Execute(null);
        Assert.Equal("A", Find(page, "P").Parent!.Name);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
