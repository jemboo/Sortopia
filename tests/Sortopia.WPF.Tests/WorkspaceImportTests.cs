using System.IO;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.workspaceModule;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceImportTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-import-");
    private string Root => Path.Combine(temporary.FullName, "Root");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private WorkspacePageViewModel Open(Func<WorkspaceFolderRequest, string?> picker, bool empty = false)
    {
        var modules = empty ? Array.Empty<WorkspaceModule>() : [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Existing", "existing", [],
            [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "Parent project", "parent-project", [WorkspaceHierarchyTests.MakeRun("Parent project", "Trial")])])];
        WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "Research", "Root", modules), Token);
        var page = new WorkspacePageViewModel(() => { }, _ => Root,
            new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")), pickExistingFolder: picker);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        return page;
    }
    private static WorkspaceTreeNode Parent(WorkspacePageViewModel page, WorkspaceEntityKind kind)
    {
        var node = page.Roots.Single();
        while (node.Kind != kind) node = node.Children.First();
        return node;
    }
    internal static void WriteProject(string folder, string name, hierarchyKind family = hierarchyKind.Root)
    {
        var run = WorkspaceHierarchyTests.MakeRun(name, "Existing trial", family == hierarchyKind.Branch);
        var value = WorkspaceHierarchyTests.MakeProject(family, Guid.NewGuid(), name, Path.GetFileName(folder), [run]);
        var inputs = RunDto.fromDomain(value.Runs[0]);
        inputs.id = Guid.NewGuid();
        value = project.withRuns(value, [RunDto.toDomain(value, inputs)]);
        Directory.CreateDirectory(Path.Combine(folder, "runs"));
        File.WriteAllBytes(Path.Combine(folder, WorkspaceDefinitionFiles.Name(value.Id)), WorkspaceDefinitionCodec.encodeProject(value));
        File.WriteAllBytes(Path.Combine(folder, "runs", WorkspaceDefinitionFiles.Name(value.Runs[0].Id)), WorkspaceCodec.encodeRun(value.Runs[0]));
        File.WriteAllText(Path.Combine(folder, "results.txt"), "Keep results");
    }
    internal static void WriteModule(string folder, string name, hierarchyKind family = hierarchyKind.Root)
    {
        Directory.CreateDirectory(folder);
        WriteProject(Path.Combine(folder, "child-project"), "Child project", family);
        var child = WorkspaceFileStore.LoadProjectFolder(Path.Combine(folder, "child-project"), Token);
        var value = WorkspaceHierarchyTests.MakeModule(family, Guid.NewGuid(), name, Path.GetFileName(folder), [], [child]);
        File.WriteAllBytes(Path.Combine(folder, WorkspaceDefinitionFiles.Name(value.Id)), WorkspaceDefinitionCodec.encodeModule(value));
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Workspace, true, false, false)]
    [InlineData(WorkspaceEntityKind.Module, true, true, false)]
    [InlineData(WorkspaceEntityKind.Project, false, false, true)]
    [InlineData(WorkspaceEntityKind.Run, false, false, false)]
    public async Task Creation_and_import_commands_obey_the_selected_parent(WorkspaceEntityKind kind, bool modules, bool projects, bool runs)
    {
        var picks = 0;
        var page = Open(_ => { picks++; return null; });
        page.Selected = Parent(page, kind);
        Assert.Equal(modules, page.AddModuleCommand.CanExecute(null));
        Assert.Equal(projects, page.AddProjectCommand.CanExecute(null));
        Assert.Equal(runs, page.AddRunCommand.CanExecute(null));
        Assert.Equal(modules, page.AddExistingModuleCommand.CanExecute(null));
        Assert.Equal(projects, page.AddExistingProjectCommand.CanExecute(null));
        if (!modules) await page.AddExistingAsync(WorkspaceEntityKind.Module);
        if (!projects) await page.AddExistingAsync(WorkspaceEntityKind.Project);
        await page.AddExistingAsync(WorkspaceEntityKind.Run);
        Assert.Equal(0, picks);
        Assert.False(page.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Workspace, WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Module, WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Module, WorkspaceEntityKind.Project)]
    public async Task Import_starts_in_the_correct_container_reads_the_subtree_and_writes_only_on_save(WorkspaceEntityKind parentKind, WorkspaceEntityKind childKind)
    {
        WorkspaceFolderRequest? request = null;
        string? selected = null;
        var page = Open(value => { request = value; return selected; });
        page.Selected = Parent(page, parentKind);
        var parentFolder = parentKind switch
        {
            WorkspaceEntityKind.Workspace => Root,
            WorkspaceEntityKind.Module => Path.Combine(Root, "existing"),
            _ => Path.Combine(Root, "existing", "parent-project")
        };
        var container = parentFolder;
        selected = Path.Combine(container, "imported-folder");
        if (childKind == WorkspaceEntityKind.Module) WriteModule(selected, "Imported module");
        else WriteProject(selected, "Imported project");
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        var originalEntity = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(selected, childKind.ToString().ToLowerInvariant()));
        await page.AddExistingAsync(childKind);
        Assert.Empty(page.Error);
        Assert.Equal(container, request!.InitialDirectory);
        Assert.Equal(container, request.ContainerFolder);
        Assert.Equal(childKind, request.Kind);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        Assert.Equal(originalEntity, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(selected, childKind.ToString().ToLowerInvariant())));
        Assert.True(page.HasUnsavedChanges);
        Assert.Equal("imported-folder", page.Selected!.FolderName);
        Assert.False(page.Selected.FolderFollowsName);
        var importedProject = childKind == WorkspaceEntityKind.Module ? page.Selected.Children.Single() : page.Selected;
        Assert.Equal("Existing trial", importedProject.Children.Single().Name);
        Assert.False(importedProject.Children.Single().HasFolder);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.False(page.HasUnsavedChanges);
        var output = childKind == WorkspaceEntityKind.Module ? Path.Combine(selected, "child-project") : selected;
        Assert.Equal("Keep results", File.ReadAllText(Path.Combine(output, "results.txt")));
        page = new WorkspacePageViewModel(() => { }, _ => null, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
        Assert.Empty(page.Error);
        Assert.Contains(Parent(page, parentKind).Children, child => child.FolderName == "imported-folder");
    }

    [Fact]
    public async Task Import_uses_parent_folder_and_cancelling_picker_does_not_change_the_workspace()
    {
        WorkspaceFolderRequest? request = null;
        var page = Open(value => { request = value; return null; }, empty: true);
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Equal(Root, request!.InitialDirectory);
        Assert.Equal(Root, request.ContainerFolder);
        Assert.True(Directory.Exists(request.ContainerFolder));
        Assert.False(page.HasUnsavedChanges);
        page.AddModuleCommand.Execute(null);
        Assert.True(page.AddExistingModuleCommand.CanExecute(null));
        Assert.True(page.AddExistingProjectCommand.CanExecute(null));
        await page.AddExistingAsync(WorkspaceEntityKind.Project);
        Assert.Equal(Root, request.InitialDirectory);
        Assert.Equal(Path.Combine(Root, "New module"), request.ContainerFolder);
    }

    [Theory]
    [InlineData("wrong type")]
    [InlineData("corrupt")]
    [InlineData("illegal children")]
    [InlineData("duplicate name")]
    [InlineData("duplicate folder")]
    [InlineData("corrupt run")]
    public async Task Invalid_imports_leave_the_draft_and_workspace_file_unchanged(string problem)
    {
        string? selected = null;
        var page = Open(_ => selected);
        selected = Path.Combine(Root, "candidate");
        switch (problem)
        {
            case "wrong type": WriteProject(selected, "A project"); break;
            case "corrupt": Directory.CreateDirectory(selected); File.WriteAllBytes(WorkspaceTestFiles.DefinitionPath(selected, "module"), [0xc1]); break;
            case "illegal children": Directory.CreateDirectory(Path.Combine(selected, "runs")); break;
            case "duplicate name": WriteModule(selected, "EXISTING"); break;
            case "duplicate folder": selected = Path.Combine(Root, "existing"); break;
            case "corrupt run":
                var runsFolder = Directory.CreateDirectory(Path.Combine(selected, "P", "runs")).FullName;
                File.WriteAllBytes(Path.Combine(runsFolder, WorkspaceDefinitionFiles.Name(Guid.NewGuid())), [0xc1]); break;
        }
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.NotEmpty(page.Error);
        Assert.False(page.HasUnsavedChanges);
        Assert.Single(page.Roots.Single().Children);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
    }

    [Fact]
    public async Task An_import_requires_metadata_to_identify_its_family()
    {
        var folder = Path.Combine(Root, "Plain module");
        var page = Open(_ => folder, empty: true);
        Directory.CreateDirectory(folder);
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Contains("Missing module definition", page.Error);
        Assert.Empty(page.Roots.Single().Children);
        Assert.False(page.HasUnsavedChanges);
        Assert.False(File.Exists(WorkspaceTestFiles.DefinitionPath(folder, "module")));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
