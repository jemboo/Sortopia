using Sortopia.Workspaces.Serialization;
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
    private WorkspacePageViewModel Open(Func<WorkspaceResetRequest, bool>? confirmReset = null, Func<bool>? isResultsExecutionBusy = null) => new(() => { }, _ => null,
        new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")), confirmReset: confirmReset, isResultsExecutionBusy: isResultsExecutionBusy);
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
        Assert.False(WorkspaceFileStore.LoadWorkspace(page.WorkspaceFolder).CreateEditDocument().Modules[0].Modules[1].Projects[0].Definition.isSealed);
    }

    public void Dispose() => temporary.Delete(true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reset_subtree_clears_artifacts_unlocks_editors_and_keeps_other_branches(bool branch)
    {
        var (page, runs) = await Create(branch);
        WorkspaceResetRequest? confirmation = null;
        page = Open(request => { confirmation = request; return true; });
        var run = Find(page, runs[0]); var project = run.Parent!; var child = project.Parent!;
        var ancestorId = child.Parent!.Id; var childId = child.Id; var projectId = project.Id;
        var projectFolder = project.GetFolderPath(page.WorkspaceFolder);
        var otherFolder = Find(page, runs[2]).Parent!.GetFolderPath(page.WorkspaceFolder);
        var otherFiles = Directory.GetFiles(otherFolder, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        var reports = Directory.CreateDirectory(Path.Combine(projectFolder, "data", "results", "reports"));
        File.WriteAllText(Path.Combine(reports.FullName, "summary.csv"), "old report");
        // Container reset does not require parameter files or results to be readable.
        File.WriteAllText(Path.Combine(projectFolder, "runParameters", "broken.msgpack"), "broken");
        page.Selected = child;
        var path = page.SelectedConfigurationPath;
        Assert.True(page.ResetGeneratedFilesCommand.CanExecute(null));
        await page.ResetGeneratedFilesAsync(); Assert.Empty(page.Error);
        Assert.Equal(path, confirmation!.Path); Assert.Equal(1, confirmation.ProjectCount); Assert.Equal(2, confirmation.RunCount);
        Assert.Equal(childId, page.Selected!.Id);
        Assert.False(Directory.Exists(Path.Combine(projectFolder, "runParameters")));
        Assert.False(Directory.Exists(Path.Combine(projectFolder, "data")));
        Assert.True(Find(page, projectId).Editor!.IsEditable);
        Assert.True(Find(page, childId).Editor!.IsEditable);
        Assert.True(Find(page, ancestorId).Editor!.IsReadOnly);
        Assert.Equal(otherFiles.Keys.Order(), Directory.GetFiles(otherFolder, "*", SearchOption.AllDirectories).Order());
        foreach (var (file, bytes) in otherFiles) Assert.Equal(bytes, File.ReadAllBytes(file));
        page.Selected = Find(page, runs[0]); await page.RefreshRunParameterFilesAsync();
        Assert.Empty(page.SelectedExecution!.Files.Rows);
        Assert.False(page.HasUnsavedChanges);
        page = Open(_ => true);
        Assert.True(Find(page, projectId).Editor!.IsEditable);
        Assert.True(Find(page, childId).Editor!.IsEditable);
        page.Selected = page.Roots.Single();
        await page.ResetGeneratedFilesAsync(); Assert.Empty(page.Error);
        Assert.All(page.Roots[0].DescendantsAndSelf().Where(n => !n.IsRun && !n.IsWorkspace), n => Assert.True(n.Editor!.IsEditable));
        Assert.Equal(3, page.Roots[0].DescendantsAndSelf().Count(n => n.IsRun));
        Assert.False(Directory.Exists(Path.Combine(otherFolder, "runParameters")));
        Find(page, ancestorId).Editor!.ResultsParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "8";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        page = Open();
        Assert.Equal("8", Find(page, runs[0]).Editor!.InheritedOutputDefaults.Single(row => row.Key == "ChannelCount").Value);
    }

    [Fact]
    public async Task Reset_requires_confirmation_and_a_saved_workspace()
    {
        var (page, runs) = await Create(false);
        var files = Directory.GetFiles(page.WorkspaceFolder, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        page.Selected = Find(page, runs[0]).Parent;
        await page.ResetGeneratedFilesAsync(); // Missing dialog callback defaults to cancellation.
        var prompts = 0;
        page = Open(_ => { prompts++; return false; });
        page.Selected = page.Roots[0];
        await page.ResetGeneratedFilesAsync(); Assert.Equal(1, prompts);
        page.AddModuleCommand.Execute(null);
        Assert.True(page.HasUnsavedChanges);
        Assert.False(page.ResetGeneratedFilesCommand.CanExecute(null));
        await page.ResetGeneratedFilesAsync(); Assert.Equal(1, prompts);
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(page.WorkspaceFolder, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Reset_waits_for_results_execution_to_finish_cancelling()
    {
        await Create(false);
        var busy = true; var prompts = 0;
        var page = Open(_ => { prompts++; return true; }, () => busy);
        page.Selected = page.Roots[0];
        Assert.False(page.ResetGeneratedFilesCommand.CanExecute(null));
        await page.ResetGeneratedFilesAsync(); Assert.Equal(0, prompts);
        busy = false;
        Assert.True(page.ResetGeneratedFilesCommand.CanExecute(null));
        await page.ResetGeneratedFilesAsync(); Assert.Equal(1, prompts); Assert.Empty(page.Error);
    }
}
