using Sortopia.Workspaces.Serialization;
using System.IO;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceExportTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-export-ui-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private string Destination => Path.Combine(temporary.FullName, "Export");
    private WorkspaceFolderSettings Settings => new(Path.Combine(temporary.FullName, "settings.txt"));

    private void CreateWorkspace()
    {
        WorkspaceFileStore.CreateDocument(Root, WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", []), TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Destination);
        Settings.Save(Root);
    }

    [Fact]
    public async Task Export_preserves_current_workspace_and_handles_cancel_and_failure()
    {
        CreateWorkspace();
        string? selection = Destination;
        var page = new WorkspacePageViewModel(() => { }, _ => throw new Exception("Wrong picker"), Settings,
            pickExportFolder: _ => selection);
        var selected = page.Selected;
        Assert.True(page.ExportWorkspaceCommand.CanExecute(null));
        await page.ExportWorkspaceAsync();
        Assert.Empty(page.OperationError);
        Assert.Contains(Destination, page.Status);
        Assert.Equal("Research", WorkspaceFileStore.LoadWorkspace(Destination).CreateEditDocument().Definition.workspaceName);
        Assert.Equal(Root, page.WorkspaceFolder);
        Assert.Equal(Root, Settings.Load());
        Assert.Same(selected, page.Selected);
        Assert.False(page.HasUnsavedChanges);
        var status = page.Status;
        selection = null;
        await page.ExportWorkspaceAsync();
        Assert.Equal(status, page.Status);
        selection = Destination;
        await page.ExportWorkspaceAsync();
        Assert.Contains("empty", page.OperationError);
        Assert.Equal("Workspace export failed.", page.Status);
        Assert.True(page.ExportWorkspaceCommand.CanExecute(null));
        Assert.Equal("Research", WorkspaceFileStore.LoadWorkspace(Destination).CreateEditDocument().Definition.workspaceName);
    }

    [Fact]
    public async Task Export_requires_a_saved_workspace_and_no_pending_changes_or_execution()
    {
        var blank = new WorkspacePageViewModel(() => { }, _ => null, Settings);
        Assert.False(blank.ExportWorkspaceCommand.CanExecute(null));
        CreateWorkspace();
        var running = false;
        var picks = 0;
        var page = new WorkspacePageViewModel(() => { }, _ => null, Settings,
            isResultsExecutionBusy: () => running, pickExportFolder: _ => { picks++; return Destination; });
        page.AddModuleCommand.Execute(null);
        Assert.False(page.ExportWorkspaceCommand.CanExecute(null));
        await page.ExportWorkspaceAsync();
        Assert.Equal(0, picks);
        Assert.Contains("Save", page.ExportWorkspaceHint);
        await page.SaveWorkspaceAsync();
        Assert.True(page.ExportWorkspaceCommand.CanExecute(null));
        running = true;
        Assert.False(page.ExportWorkspaceCommand.CanExecute(null));
        running = false;
        await page.ExportWorkspaceAsync();
        Assert.Equal(1, picks);
        Assert.Empty(page.OperationError);
        Assert.Single(WorkspaceFileStore.LoadWorkspace(Destination).CreateEditDocument().Modules);
    }

    public void Dispose() => temporary.Delete(true);
}
