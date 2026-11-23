using System.IO;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspacePageViewModel
{
    private Func<string?, string?> pickExportFolder = _ => null;
    public RelayCommand ExportWorkspaceCommand { get; private set; } = null!;

    private void InitializeExportCommand(Func<string?, string?> picker)
    {
        pickExportFolder = picker;
        ExportWorkspaceCommand = new RelayCommand(async () => await ExportWorkspaceAsync(), () => CanExportWorkspace);
    }

    private bool CanExportWorkspace => CanEdit && !resolvingNavigation && !isResultsExecutionBusy()
        && savedWorkspace is not null && HasSavedWorkspace && !HasUnsavedChanges && !HasStructuralErrors && !HasMissingComponents;

    public string ExportWorkspaceHint => IsBusy || isResultsExecutionBusy()
        ? "Wait for the current operation to finish before exporting."
        : savedWorkspace is null || !HasSavedWorkspace || HasUnsavedChanges || HasStructuralErrors || HasMissingComponents
        ? "Save workspace changes and restore any missing files before exporting."
        : "Copy all modules, projects, runs, and generated files to an empty folder.";

    public async Task ExportWorkspaceAsync()
    {
        if (!CanExportWorkspace) return;
        try
        {
            var destination = pickExportFolder(Path.GetDirectoryName(WorkspaceFolder));
            if (destination is null) return;
            await RunAsync(async token =>
            {
                Status = "Exporting workspace…";
                try
                {
                    await Task.Run(() => WorkspaceFileStore.ExportWorkspace(WorkspaceFolder, destination, token), token);
                    Status = $"Workspace exported to '{destination}'.";
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    Status = "Workspace export failed.";
                    Error = $"Could not export workspace. {exception.Message}";
                }
            });
        }
        catch (Exception exception) when (IsFolderError(exception))
        {
            Error = $"Could not select the export folder. {exception.Message}";
        }
    }
}
