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
    public async Task Folder_edits_and_paste_reject_cross_type_case_insensitive_collisions()
    {
        var page = Open(); page.AddModuleCommand.Execute(null); var parent = page.Selected!;
        parent.Name = "Parent"; parent.Editor!.LoadFixtureSettings(WorkspaceHierarchyTests.ProjectSettings());
        page.AddModuleCommand.Execute(null); var module = page.Selected!; module.Name = "Shared";
        page.Selected = parent; page.AddProjectCommand.Execute(null); var project = page.Selected!;
        project.Name = "sHaReD";
        Assert.Contains("module and project children", project.NameError); Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        project.Name = "Project"; Assert.Empty(project.NameError);
        Assert.Equal(Path.Combine(Root, "Parent", "Project"), page.SelectedFolderPath);
        page.Selected = page.Roots[0]; page.AddModuleCommand.Execute(null); var other = page.Selected!; other.Name = "Other";
        other.Editor!.LoadFixtureSettings(WorkspaceHierarchyTests.ProjectSettings());
        page.AddProjectCommand.Execute(null); page.Selected!.Name = "Shared";
        Assert.True(page.CutCommand.CanExecute(null)); page.CutCommand.Execute(null); page.Selected = parent;
        await page.PasteAsync(); Assert.Contains("module and project children", page.Error);
        Assert.Single(other.Children); Assert.Equal("Shared", other.Children[0].Name);
        Assert.Equal(2, parent.Children.Count);
        page.Cancel();
    }
    public void Dispose() => temporary.Delete(true);
}
