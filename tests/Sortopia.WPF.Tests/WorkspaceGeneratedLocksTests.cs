using System.IO;
using Sortopia.Functions;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceGeneratedLocksTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-generated-locks-");
    private WorkspacePageViewModel Open() => new(() => { }, _ => null, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
    private static WorkspaceTreeNode Find(WorkspacePageViewModel page, Guid id) => page.Roots[0].DescendantsAndSelf().Single(node => node.Id == id);

    private async Task<(WorkspacePageViewModel Page, Guid[] Runs)> Create(bool branch)
    {
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName);
        var page = Open();
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var ancestor = page.Selected!;
        ancestor.Editor!.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
        ancestor.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        ancestor.Editor.ResultsParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "4";
        if (branch)
        {
            ancestor.Editor.SetupFunctionName = SetupFunctionNames.uniformMsuf4SimpleSorterModels;
            ancestor.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
            ancestor.Editor.SetupParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "4";
            ancestor.Editor.SetupParameterDefaults.Single(row => row.Key == "SorterCount").Value.Text = "1";
        }
        var runs = new List<Guid>();
        for (var child = 0; child < 2; child++)
        {
            page.Selected = ancestor;
            (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
            page.AddProjectCommand.Execute(null);
            var project = page.Selected!;
            for (var run = 0; run < (child == 0 ? 2 : 1); run++)
            {
                page.Selected = project; page.AddRunCommand.Execute(null);
                runs.Add(page.Selected!.Id);
            }
        }
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        foreach (var id in runs)
        {
            page.Selected = Find(page, id);
            await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
            Assert.True(page.SelectedExecution!.Files.HasFiles);
        }
        return (page, runs.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_ancestors_without_remaining_files_unlock_and_inheritance_survives_reload(bool branch)
    {
        var (page, runs) = await Create(branch);
        page = Open();
        var run = Find(page, runs[0]); var project = run.Parent!; var child = project.Parent!; var ancestor = child.Parent!;
        page.Selected = run;
        await WorkspaceRunExecutionTests.GenerateAndExecuteAsync(page);
        var dataFolder = Path.Combine(project.GetFolderPath(page.WorkspaceFolder), "data");
        Assert.NotEmpty(Directory.GetFiles(dataFolder, "*.msgpack", SearchOption.AllDirectories));
        foreach (var node in new[] { project, child, ancestor }) { Assert.True(node.Editor!.IsReadOnly); Assert.False(node.CanRename); }
        var function = ancestor.Editor!.ResultsFunctionName;
        ancestor.Editor.ResultsFunctionName = ResultsFunctionNames.mergeSortableTests;
        Assert.Equal(function, ancestor.Editor.ResultsFunctionName);
        // Remove one run's files; its sibling still locks the project and both modules.
        page.Selected = run; await page.RefreshRunParameterFilesAsync();
        await page.RemoveRunParameterFilesAsync(); Assert.Empty(page.Error);
        Assert.True(project.IsSealed); Assert.True(child.Editor!.IsReadOnly); Assert.True(ancestor.Editor.IsReadOnly);
        page.Selected = Find(page, runs[1]); await page.RefreshRunParameterFilesAsync();
        await page.RemoveRunParameterFilesAsync(); Assert.Empty(page.Error);
        Assert.False(project.IsSealed); Assert.True(project.Editor!.IsEditable); Assert.True(child.Editor.IsEditable);
        Assert.False(Directory.Exists(dataFolder));
        Assert.Empty(page.SelectedExecution!.Files.Rows);
        Assert.True(ancestor.Editor.IsReadOnly); // The other module still has a generated run.
        page.Selected = Find(page, runs[2]); await page.RefreshRunParameterFilesAsync();
        await page.RemoveRunParameterFilesAsync(); Assert.Empty(page.Error);
        Assert.True(ancestor.Editor.IsEditable);
        ancestor.Editor.ResultsParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "8";
        Assert.Equal("8", run.Editor!.InheritedOutputDefaults.Single(row => row.Key == "ChannelCount").Value);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        page = Open(); run = Find(page, runs[0]);
        Assert.True(run.Parent!.Editor!.IsEditable);
        Assert.Equal("8", run.Editor!.InheritedOutputDefaults.Single(row => row.Key == "ChannelCount").Value);
        page.Selected = run; await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
        Assert.True(run.Parent.Editor.IsReadOnly); Assert.True(run.Parent.Parent!.Parent!.Editor!.IsReadOnly);
    }

    [Fact]
    public async Task Removing_runs_unlocks_only_after_saved_deletion_and_external_file_removal_refreshes_locks()
    {
        var (page, runs) = await Create(false);
        page.Selected = Find(page, runs[0]); page.RemoveCommand.Execute(null);
        Assert.True(page.HasPendingRemoval); Assert.True(page.Selected!.Editor!.IsReadOnly);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        page.Selected = Find(page, runs[1]); var projectId = page.Selected.Parent!.Id;
        Assert.True(page.Selected.Parent.IsSealed);
        page.RemoveCommand.Execute(null); await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        Assert.False(Find(page, projectId).IsSealed); Assert.True(Find(page, projectId).Editor!.IsEditable);
        page.Selected = Find(page, runs[2]); await page.RefreshRunParameterFilesAsync();
        foreach (var row in page.SelectedExecution!.Files.Rows) File.Delete(row.Path);
        await page.RefreshRunParameterFilesAsync(); Assert.Empty(page.Error);
        Assert.True(page.Selected.Parent!.Parent!.Parent!.Editor!.IsEditable);
        Assert.False(WorkspaceFileStore.LoadDocument(page.WorkspaceFolder).Modules[0].Modules[1].Projects[0].Definition.isSealed);
    }

    public void Dispose() => temporary.Delete(true);
}
