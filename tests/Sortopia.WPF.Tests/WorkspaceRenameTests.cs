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
    public async Task Generated_parameter_files_lock_module_and_project_names_without_changing_saved_work(bool branch, bool caseOnly)
    {
        var page = await Create(branch);
        var run = page.Selected!; var project = run.Parent!; var module = project.Parent!;
        var oldFolder = project.GetFolderPath(page.WorkspaceFolder);
        var projectId = project.Id; var runId = run.Id;
        var moduleName = module.Name; var projectName = project.Name;
        var settings = MessagePackSetup.serialize(project.Editor!.ToProjectSettings());
        var files = Directory.GetFiles(oldFolder, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetDirectoryName(path) != oldFolder)
            .ToDictionary(path => Path.GetRelativePath(oldFolder, path), File.ReadAllBytes);
        Assert.NotEmpty(files);
        module.NameInput = caseOnly ? module.Name.ToUpperInvariant() : "Renamed module";
        project.NameInput = caseOnly ? project.Name.ToUpperInvariant() : "Renamed project";
        Assert.Equal(moduleName, module.NameInput); Assert.Equal(projectName, project.NameInput);
        Assert.False(module.CanRename); Assert.False(project.CanRename);
        Assert.Equal(module.Name, module.PathName); Assert.Equal(project.Name, project.PathName);
        Assert.True(project.Editor.IsReadOnly); Assert.True(project.IsSealed);
        Assert.Equal(project.Name, WorkspaceRunDocument.resolveByRunId(page.Roots[0].ToDocument(), runId).Run.ProjectName);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.True(Directory.Exists(oldFolder));
        var newFolder = project.GetFolderPath(page.WorkspaceFolder);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        WorkspaceRunExecutionTests.SelectRun(page);
        var saved = new WorkspaceDocumentIndex(WorkspaceFileStore.LoadWorkspace(page.WorkspaceFolder).CreateEditDocument()).TryFindRun(runId).Value.Parent.Document;
        Assert.Equal(project.Name, saved.Definition.projectName); Assert.Equal(runId, saved.Runs[0].id);
        Assert.Equal(projectId, saved.ResolvedRuns()[0].ParentProjectId); Assert.True(saved.Definition.isSealed);
        Assert.Equal(settings, MessagePackSetup.serialize(SettingsDto.fromDomain(saved.Configuration())));
        Assert.Contains(newFolder, Directory.GetDirectories(Path.GetDirectoryName(newFolder)!));
        Assert.Equal(oldFolder, newFolder);
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
        await page.RemoveRunParameterFilesAsync(); Assert.Empty(page.Error);
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
    public void Invalid_names_do_not_change_the_accepted_name_or_path(string name)
    {
        foreach (var kind in new[] { WorkspaceEntityKind.Module, WorkspaceEntityKind.Project })
        {
            var node = new WorkspaceTreeNode(kind, "Original");
            node.NameInput = node.NameInput;
            Assert.Equal("Original", node.Name);
            node.NameInput = name;
            Assert.NotEmpty(node.NameError); Assert.Equal("Original", node.Name); Assert.Equal("Original", node.PathName);
            node.NameInput = "Valid name";
            Assert.Empty(node.NameError); Assert.Equal("Valid name", node.Name);
        }
    }
    public void Dispose() => temporary.Delete(true);
}
