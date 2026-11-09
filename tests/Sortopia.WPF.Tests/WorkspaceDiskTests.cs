using System.IO;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.workspaceModule;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceDiskTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-disk-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private string ModuleFolder => Path.Combine(Root, "modules", "sorting");
    private string ProjectFolder => Path.Combine(ModuleFolder, "projects", "search");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static workspace Sample() => workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Sorting", "sorting", [],
        [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "Search", "search", [WorkspaceHierarchyTests.MakeRun("Search", "Trial")])])]);
    private WorkspacePageViewModel Open(Func<WorkspaceFolderRequest, string?>? picker = null)
    {
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Root);
        return new(() => { }, _ => null, settings, pickExistingFolder: picker);
    }
    private static Dictionary<string, byte[]> Files(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(folder, path), File.ReadAllBytes);
    private static void AssertFilesEqual(Dictionary<string, byte[]> expected, string folder)
    {
        var actual = Files(folder);
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
    }
    private static string[] Markers(string folder) => Directory.GetFiles(folder, "*.msgpack", SearchOption.AllDirectories);
    [Fact]
    public void Every_entity_has_a_unique_guid_filename_that_is_stable_when_saved_again()
    {
        var original = Sample();
        WorkspaceFileStore.CreateWorkspace(Root, original, Token);
        var paths = Markers(Root).Order().ToArray();
        Assert.Equal(4, paths.Length);
        var ids = paths.Select(path => Guid.Parse(Path.GetFileNameWithoutExtension(path))).ToArray();
        Assert.Equal(4, ids.Distinct().Count());
        Assert.Equal(new[] { original.Id, original.Modules.Single().Id, original.Modules.Single().Projects.Single().Id,
            original.Modules.Single().Projects.Single().Runs.Single().Id }.Order(), ids.Order());
        Assert.All(paths, path => Assert.True(WorkspaceDefinitionFiles.HasGuid(Path.GetFileName(path))));
        WorkspaceFileStore.SaveWorkspace(Root, workspace.create(original.Id, "Renamed research", "Workspace", original.Modules), original, Token);
        Assert.Equal(paths, Markers(Root).Order());
        Assert.Equal("Renamed research", WorkspaceFileStore.LoadDefinition(Root).WorkspaceName);
    }



    [Fact]
    public async Task Copying_guid_definitions_assigns_new_filenames_and_does_not_modify_the_source()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var source = Path.Combine(temporary.FullName, "Library");
        WorkspaceFileStore.CreateWorkspace(source, Sample(), Token);
        var sourceProject = Path.Combine(source, "modules", "sorting", "projects", "search");
        var originalFiles = Files(source);
        var page = Open(_ => sourceProject);
        page.AddModuleCommand.Execute(null);
        page.Selected!.Name = "Destination";
        await page.AddExistingAsync(WorkspaceEntityKind.Project);
        Assert.Empty(page.Error);
        page.RefreshDiskStatus();
        Assert.Equal("Pending copy", page.Selected!.DiskStatus);
        Assert.Equal("Pending copy", page.Selected.Children.Single().DiskStatus);
        Assert.False(page.HasMissingComponents);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var destination = Path.Combine(Root, "modules", "Destination", "projects", "search");
        Assert.Equal(2, Markers(destination).Length);
        Assert.Empty(Markers(sourceProject).Select(Path.GetFileName).Intersect(Markers(destination).Select(Path.GetFileName)));
        AssertFilesEqual(originalFiles, source);
        Assert.Equal("Search", WorkspaceFileStore.LoadProjectFolder(destination, Token).ProjectName);
    }

    [Fact]
    public void Ambiguous_definition_files_are_reported_and_never_arbitrarily_selected()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var first = WorkspaceDefinitionFiles.Require(Root, "workspace");
        var second = Path.Combine(Root, WorkspaceDefinitionFiles.Name(Guid.NewGuid()));
        File.Copy(first, second);
        var before = Files(Root);
        var page = Open();
        Assert.Contains("Multiple workspace definition files", page.Error);
        Assert.Contains("Multiple workspace definition files", page.WorkspaceFileStatus);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.Throws<IOException>(() => WorkspaceFileStore.LoadDefinition(Root));
        AssertFilesEqual(before, Root);
    }

    [Fact]
    public async Task Missing_folders_and_definitions_are_marked_without_dirtying_the_draft_and_save_restores_them()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var page = Open();
        var module = page.Roots.Single().Children.Single();
        var project = module.Children.Single();
        var run = project.Children.Single();
        var moved = Path.Combine(temporary.FullName, "Moved project");
        Directory.Move(ProjectFolder, moved);
        File.Delete(WorkspaceDefinitionFiles.Require(ModuleFolder, "module"));
        page.RefreshDiskStatus();
        Assert.Equal("Missing definition", module.DiskStatus);
        Assert.Equal("Missing folder", project.DiskStatus);
        Assert.Equal("Missing definition", run.DiskStatus);
        Assert.True(run.HasDiskWarning);
        Assert.Contains(run.GetFolderPath(Root), run.DiskStatusDetail);
        Assert.Equal("3 saved components are missing from disk.", page.MissingComponentsMessage);
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.DiscardChangesCommand.CanExecute(null));
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.False(page.HasMissingComponents);
        Assert.True(File.Exists(run.GetFolderPath(Root)));
        Assert.True(Directory.Exists(moved));
    }

    [Fact]
    public async Task Missing_workspace_definition_keeps_the_loaded_hierarchy_visible_and_can_be_restored()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var page = Open();
        var originalFile = WorkspaceDefinitionFiles.Require(Root, "workspace");
        File.Delete(originalFile);
        page.RefreshDiskStatus();
        Assert.False(page.HasSavedWorkspace);
        Assert.False(page.ShowNewWorkspace);
        Assert.True(page.ShowWorkspaceEditor);
        Assert.Equal("Missing definition", page.Roots.Single().DiskStatus);
        Assert.Single(page.Roots.Single().Children);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.True(page.HasSavedWorkspace);
        Assert.False(page.HasMissingComponents);
        Assert.Equal(originalFile, WorkspaceDefinitionFiles.Require(Root, "workspace"));
    }

    [Fact]
    public void New_items_and_edited_folder_locations_are_not_reported_as_missing_saved_components()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var page = Open();
        page.AddModuleCommand.Execute(null);
        var added = page.Selected!;
        var saved = page.Roots.Single().Children.First();
        saved.FolderName = "new-location";
        page.RefreshDiskStatus();
        Assert.Equal("Not saved", added.DiskStatus);
        Assert.Equal("Not saved", saved.DiskStatus);
        Assert.False(page.HasMissingComponents);
        page.DiscardChangesCommand.Execute(null);
        Assert.Empty(page.Roots.Single().Children.Single().DiskStatus);
        Assert.False(page.HasMissingComponents);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
