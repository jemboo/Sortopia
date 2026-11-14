using System.IO;
using Sortopia.Core.Serialization;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceRenameTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-rename-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private async Task<WorkspacePageViewModel> Create(bool branch = false)
    {
        var page = WorkspaceRunExecutionTests.Create(temporary.FullName, branch);
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        await page.SaveWorkspaceAsync(); WorkspaceRunExecutionTests.SelectRun(page);
        await WorkspaceRunExecutionTests.ExecuteSourceAsync(page);
        await WorkspaceRunExecutionTests.GenerateAndExecuteAsync(page);
        Assert.Empty(page.Error);
        return page;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Renaming_a_module_and_sealed_project_moves_all_generated_work_without_changing_identity(bool branch, bool caseOnly)
    {
        var page = await Create(branch);
        var run = page.Selected!; var project = run.Parent!; var module = project.Parent!;
        var oldFolder = project.GetFolderPath(page.WorkspaceFolder);
        var projectId = project.Id; var runId = run.Id;
        var settings = MessagePackSetup.serialize(project.Editor!.ToProjectSettings());
        var files = Directory.GetFiles(oldFolder, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetDirectoryName(path) != oldFolder)
            .ToDictionary(path => Path.GetRelativePath(oldFolder, path), File.ReadAllBytes);
        Assert.NotEmpty(files);
        module.NameInput = caseOnly ? module.Name.ToUpperInvariant() : "Renamed module";
        project.NameInput = caseOnly ? project.Name.ToUpperInvariant() : "Renamed project";
        Assert.Equal(module.Name, module.FolderName); Assert.Equal(project.Name, project.FolderName);
        Assert.True(project.Editor.IsReadOnly); Assert.True(project.IsSealed);
        Assert.Equal(project.Name, page.Roots[0].ToWorkspace().ProjectIndex[projectId].Runs[0].ProjectName);
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.True(Directory.Exists(oldFolder));
        var newFolder = project.GetFolderPath(page.WorkspaceFolder);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        WorkspaceRunExecutionTests.SelectRun(page);
        var saved = WorkspaceFileStore.LoadDefinition(page.WorkspaceFolder).ProjectIndex[projectId];
        Assert.Equal(project.Name, saved.ProjectName); Assert.Equal(runId, saved.Runs[0].Id);
        Assert.Equal(projectId, saved.Runs[0].ParentProjectId); Assert.True(saved.IsSealed);
        Assert.Equal(settings, MessagePackSetup.serialize(SettingsDto.fromDomain(saved.Configuration)));
        Assert.Contains(newFolder, Directory.GetDirectories(Path.GetDirectoryName(newFolder)!));
        if (!caseOnly) Assert.False(Directory.Exists(oldFolder));
        foreach (var (relative, content) in files) Assert.Equal(content, File.ReadAllBytes(Path.Combine(newFolder, relative)));
        Assert.All(RunParameterExecution.inspectFolder(page.RunParametersFolder, null!, Token), row => Assert.Equal(outputState.Complete, row.Outputs.State));
        await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
        Assert.Equal(runExecutionStatus.Completed, page.SelectedExecution!.Report!.Status);
        Assert.Empty(Directory.GetDirectories(page.WorkspaceFolder, ".sortopia-*"));
    }

    [Fact]
    public async Task A_failed_rename_restores_the_old_folder_and_retry_moves_its_contents()
    {
        var page = await Create();
        var node = page.Selected!.Parent!;
        var oldFolder = node.GetFolderPath(page.WorkspaceFolder);
        var before = Directory.GetFiles(page.WorkspaceFolder, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        node.Name = "Retry rename";
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(node.Parent!.GetFolderPath(page.WorkspaceFolder), "module"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync(); Assert.NotEmpty(page.OperationError);
            Assert.True(page.HasUnsavedChanges);
        }
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(page.WorkspaceFolder, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, content) in before) Assert.Equal(content, File.ReadAllBytes(path));
        Assert.False(Directory.Exists(node.GetFolderPath(page.WorkspaceFolder)));
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        Assert.False(Directory.Exists(oldFolder));
        Assert.True(Directory.Exists(node.GetFolderPath(page.WorkspaceFolder)));
    }

    [Theory]
    [InlineData("Bad:name")]
    [InlineData("CON")]
    [InlineData("Trailing.")]
    public void Invalid_folder_names_do_not_change_either_accepted_name(string name)
    {
        foreach (var kind in new[] { WorkspaceEntityKind.Module, WorkspaceEntityKind.Project })
        {
            var node = new WorkspaceTreeNode(kind, "Original", "existing-folder");
            node.NameInput = node.NameInput;
            Assert.Equal("existing-folder", node.FolderName);
            node.NameInput = name;
            Assert.NotEmpty(node.NameError); Assert.Equal("Original", node.Name); Assert.Equal("existing-folder", node.FolderName);
            node.NameInput = "Valid name";
            Assert.Empty(node.NameError); Assert.Equal("Valid name", node.FolderName);
        }
    }
    public void Dispose() => temporary.Delete(true);
}
