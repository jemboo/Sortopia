using System.IO;
using System.Security.Cryptography;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.workspaceModule;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceCopyTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-copy-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private string Source => Path.Combine(temporary.FullName, "Library", "imported");
    private string? selection;
    private WorkspaceFolderRequest? request;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private WorkspacePageViewModel Open()
    {
        var definition = workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Parent module", "parent-module", [],
            [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "Parent project", "parent-project", [])])]);
        WorkspaceFileStore.CreateWorkspace(Root, definition, Token);
        var page = new WorkspacePageViewModel(() => { }, _ => Root,
            new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")), () => WorkspaceUnsavedChoice.Discard,
            value => { request = value; return selection; });
        page.BrowseWorkspaceFolderCommand.Execute(null);
        return page;
    }
    private static string Snapshot(string folder) => string.Join("\n",
        Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).Select(path => "D:" + Path.GetRelativePath(folder, path))
        .Concat(Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(path => "F:" + Path.GetRelativePath(folder, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
        .Order(StringComparer.Ordinal));
    private void AssertNoStaging() => Assert.Empty(Directory.GetDirectories(Root, ".sortopia-import-*"));
    private static void AddData(string source)
    {
        Directory.CreateDirectory(Path.Combine(source, "other-data", "empty"));
        File.WriteAllBytes(Path.Combine(source, "other-data", "payload.bin"), Enumerable.Range(0, 4096).Select(index => (byte)index).ToArray());
        File.WriteAllText(Path.Combine(source, ".hidden-data"), "Preserve every file");
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Workspace, WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Module, WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Module, WorkspaceEntityKind.Project)]
    public async Task External_imports_copy_every_file_and_empty_folder_on_save_and_leave_source_unchanged(WorkspaceEntityKind parentKind, WorkspaceEntityKind childKind)
    {
        var page = Open();
        while (page.Selected!.Kind != parentKind) page.Selected = page.Selected.Children.First();
        var parentFolder = parentKind switch
        {
            WorkspaceEntityKind.Workspace => Root,
            WorkspaceEntityKind.Module => Path.Combine(Root, "parent-module"),
            _ => Path.Combine(Root, "parent-module", "parent-project")
        };
        if (childKind == WorkspaceEntityKind.Module) WorkspaceImportTests.WriteModule(Source, "Imported module");
        else WorkspaceImportTests.WriteProject(Source, "Imported project");
        AddData(Source);
        var before = Snapshot(Source);
        var destination = Path.Combine(parentFolder, "imported");
        selection = Source;
        await page.AddExistingAsync(childKind);
        Assert.Empty(page.Error);
        Assert.Equal(Path.GetDirectoryName(destination), request!.ContainerFolder);
        Assert.False(Directory.Exists(destination));
        Assert.Contains(Source, page.Selected!.PendingCopyDescription);
        page.Selected.Name = "Renamed in workspace";
        var importedName = page.Selected.Name;
        Assert.False(Directory.Exists(destination));
        destination = Path.Combine(parentFolder, importedName);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.False(page.HasUnsavedChanges);
        Assert.Equal(before, Snapshot(Source));
        Assert.True(Directory.Exists(Path.Combine(destination, "other-data", "empty")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Source, "other-data", "payload.bin")), File.ReadAllBytes(Path.Combine(destination, "other-data", "payload.bin")));
        Assert.Equal("Preserve every file", File.ReadAllText(Path.Combine(destination, ".hidden-data")));
        var runParent = childKind == WorkspaceEntityKind.Module ? Path.Combine(destination, "child-project") : destination;
        Assert.Equal("Keep results", File.ReadAllText(Path.Combine(runParent, "results.txt")));
        AssertNoStaging();
        page = new WorkspacePageViewModel(() => { }, _ => null, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
        Assert.Empty(page.Error);
        var parent = page.Roots.Single();
        while (parent.Kind != parentKind) parent = parent.Children.First();
        Assert.Contains(parent.Children, child => child.Name == importedName && child.FolderName == importedName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discarding_or_removing_an_import_does_not_copy_files(bool remove)
    {
        var page = Open();
        WorkspaceImportTests.WriteModule(Source, "Imported");
        var before = Snapshot(Source);
        selection = Source;
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(page.Error);
        if (remove) { page.RemoveCommand.Execute(null); await page.SaveWorkspaceAsync(); }
        else page.DiscardChangesCommand.Execute(null);
        Assert.False(page.HasUnsavedChanges);
        Assert.False(Directory.Exists(Path.Combine(Root, "imported")));
        Assert.Equal(before, Snapshot(Source));
        AssertNoStaging();
    }

    [Fact]
    public async Task A_failed_commit_rolls_back_the_entire_copy_and_can_be_retried()
    {
        var page = Open();
        WorkspaceImportTests.WriteModule(Source, "Imported");
        AddData(Source);
        var before = Snapshot(Source);
        var rootBefore = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        selection = Source;
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        page.Selected!.Name = "Updated metadata";
        var destination = Path.Combine(Root, "Updated metadata");
        using (var locked = new FileStream(WorkspaceTestFiles.DefinitionPath(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
        }
        Assert.False(Directory.Exists(destination));
        Assert.Equal(rootBefore, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        Assert.Equal(before, Snapshot(Source));
        Assert.True(page.HasUnsavedChanges);
        AssertNoStaging();
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.True(Directory.Exists(destination));
        Assert.Equal(before, Snapshot(Source));
    }

    [Fact]
    public async Task Removing_a_project_from_a_pending_import_excludes_its_contents_and_preserves_source()
    {
        var page = Open(); WorkspaceImportTests.WriteModule(Source, "Imported");
        var before = Snapshot(Source); selection = Source;
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        var imported = page.Selected!; var folder = imported.GetFolderPath(Root);
        page.Selected = imported.Children.Single(node => node.IsProject);
        var projectFolder = page.Selected.GetFolderPath(Root);
        page.RemoveCommand.Execute(null);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        Assert.True(Directory.Exists(folder)); Assert.False(Directory.Exists(projectFolder));
        Assert.Equal(before, Snapshot(Source));
    }

    [Fact]
    public async Task Nested_pending_imports_are_copied_together_on_save()
    {
        var page = Open();
        WorkspaceImportTests.WriteModule(Source, "Imported module");
        selection = Source;
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(page.Error);
        var second = Path.Combine(temporary.FullName, "Library", "second-project");
        WorkspaceImportTests.WriteProject(second, "Second project");
        var firstBefore = Snapshot(Source);
        var secondBefore = Snapshot(second);
        selection = second;
        Assert.True(page.AddExistingProjectCommand.CanExecute(null));
        await page.AddExistingAsync(WorkspaceEntityKind.Project);
        Assert.Empty(page.Error);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Contains("Copied 2 imported components", page.Status);
        Assert.Equal("Keep results", File.ReadAllText(Path.Combine(Root, "imported", "second-project", "results.txt")));
        Assert.Equal(firstBefore, Snapshot(Source));
        Assert.Equal(secondBefore, Snapshot(second));
        AssertNoStaging();
    }

    [Fact]
    public async Task Import_destination_tracks_unsaved_parent_and_import_folder_names()
    {
        var page = Open();
        page.AddModuleCommand.Execute(null);
        page.Selected!.Name = "New parent";
        var parent = page.Selected;
        WorkspaceImportTests.WriteProject(Source, "Imported project");
        selection = Source;
        await page.AddExistingAsync(WorkspaceEntityKind.Project);
        Assert.Empty(page.Error);
        page.Selected!.FolderName = "renamed-folder";
        Assert.Equal("renamed-folder", page.Selected.FolderName);
        parent.Name = "Renamed parent";
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.True(File.Exists(WorkspaceTestFiles.DefinitionPath(Root, "Renamed parent", "renamed-folder", "project")));
        Assert.False(Directory.Exists(Path.Combine(Root, "New parent")));
    }

    [Fact]
    public async Task A_changed_source_after_adding_is_not_copied_or_overwritten()
    {
        var page = Open();
        WorkspaceImportTests.WriteModule(Source, "Imported");
        AddData(Source);
        selection = Source;
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(page.Error);
        File.WriteAllText(Path.Combine(Source, "other-data", "payload.bin"), "New external content");
        await page.SaveWorkspaceAsync();
        Assert.Contains("source changed", page.Error);
        Assert.False(Directory.Exists(Path.Combine(Root, "imported")));
        Assert.Equal("New external content", File.ReadAllText(Path.Combine(Source, "other-data", "payload.bin")));
        AssertNoStaging();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Occupied_destinations_are_rejected_without_merging_or_overwriting(bool file)
    {
        var page = Open();
        WorkspaceImportTests.WriteModule(Source, "Imported");
        var destination = Path.Combine(Root, "imported");
        var sentinel = file ? destination : Path.Combine(Directory.CreateDirectory(destination).FullName, "keep.txt");
        File.WriteAllText(sentinel, "Existing content");
        selection = Source;
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Contains("already exists", page.Error);
        Assert.False(page.HasUnsavedChanges);
        Assert.Equal("Existing content", File.ReadAllText(sentinel));
        Assert.Single(page.Roots.Single().Children);
    }

    [Fact]
    public void Copy_into_a_descendant_of_the_source_is_rejected()
    {
        Open();
        Assert.Throws<IOException>(() => WorkspaceFolderCopy.Prepare(Root, Root, Path.Combine(Root, "recursive"), Token));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
