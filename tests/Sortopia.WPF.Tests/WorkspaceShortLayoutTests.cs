using System.IO;
using Sortopia.Core.Serialization;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceShortLayoutTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-short-layout-ui-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private WorkspacePageViewModel Open()
    {
        Directory.CreateDirectory(Root);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(Root);
        return new(() => { }, _ => null, settings);
    }

    [Fact]
    public async Task Existing_workspaces_offer_explicit_conversion_without_modifying_files_on_open()
    {
        var module = workspaceModule.createInherited(Guid.NewGuid(), "Sorting", "sorting", ModuleDefaults.configuration(false), ModuleOverridesDto.toDomain(hierarchyKind.Root, new()), [], []);
        var workspace = Sortopia.Workspaces.workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [module]);
        var w = WorkspaceDefinitionCodec.workspace(workspace); w.schemaVersion = 15;
        var m = WorkspaceDefinitionCodec.moduleWithOrigin(null!, module); m.schemaVersion = 15;
        var folder = Path.Combine(Root, "modules", "sorting"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(Root, WorkspaceDefinitionFiles.Name(workspace.Id)), MessagePackSetup.serialize(w));
        File.WriteAllBytes(Path.Combine(folder, WorkspaceDefinitionFiles.Name(module.Id)), MessagePackSetup.serialize(m));
        var page = Open(); Assert.Empty(page.Error);
        Assert.True(page.NeedsLayoutConversion); Assert.False(page.CanEdit); Assert.False(page.ShowWorkspaceEditor);
        Assert.True(page.ConvertFolderLayoutCommand.CanExecute(null));
        Assert.True(Directory.Exists(folder)); Assert.False(Directory.Exists(Path.Combine(Root, "sorting")));
        await page.ConvertFolderLayoutAsync(); Assert.Empty(page.Error);
        Assert.False(page.NeedsLayoutConversion); Assert.True(page.CanEdit); Assert.True(page.ShowWorkspaceEditor);
        Assert.Equal(module.Id, page.Roots.Single().Children.Single().Id);
        page.Selected = page.Roots.Single().Children.Single();
        Assert.Equal(Path.Combine(Root, "sorting"), page.SelectedFolderPath);
        Assert.False(Directory.Exists(Path.Combine(Root, "modules")));
        Assert.Equal(workspace.Id, WorkspaceFileStore.LoadDefinition(Root).Id);
        page.Cancel();
    }

    [Fact]
    public async Task Folder_edits_and_paste_reject_cross_type_case_insensitive_collisions()
    {
        var page = Open(); page.AddModuleCommand.Execute(null); var parent = page.Selected!;
        parent.Name = "Parent"; parent.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings());
        page.AddModuleCommand.Execute(null); var module = page.Selected!; module.Name = "Shared";
        page.Selected = parent; page.AddProjectCommand.Execute(null); var project = page.Selected!;
        project.FolderName = "sHaReD";
        Assert.Contains("module and project children", project.FolderNameError); Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        project.FolderName = "Project"; Assert.Empty(project.FolderNameError);
        Assert.Equal(Path.Combine(Root, "Parent", "Project"), page.SelectedFolderPath);
        page.Selected = page.Roots[0]; page.AddModuleCommand.Execute(null); var other = page.Selected!; other.Name = "Other";
        other.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings());
        page.AddProjectCommand.Execute(null); page.Selected!.Name = "Shared";
        Assert.True(page.CutCommand.CanExecute(null)); page.CutCommand.Execute(null); page.Selected = parent;
        await page.PasteAsync(); Assert.Contains("folder name", page.Error);
        Assert.Single(other.Children); Assert.Equal("Shared", other.Children[0].FolderName);
        Assert.Equal(2, parent.Children.Count);
        page.Cancel();
    }
    public void Dispose() => temporary.Delete(true);
}
