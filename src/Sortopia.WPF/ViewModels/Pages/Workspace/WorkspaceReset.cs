using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceResetRequest(string Path, bool IsRun, int ProjectCount, int RunCount)
{
    public string Description => IsRun
        ? "Remove this run's parameter files and generated data that is no longer used by other runs. Project reports will also be removed because they may include this run."
        : $"Remove all run parameter files and generated data, including setup data, results, and reports, from this item and all its descendants ({ProjectCount} {(ProjectCount == 1 ? "project" : "projects")}, {RunCount} {(RunCount == 1 ? "run" : "runs")}).";
    public string EditingNotice => "Workspace structure, function choices, parameters, and run definitions will be kept. Projects and modules become editable when no descendant run has generated parameter files. This cannot be undone.";
}

public sealed partial class WorkspacePageViewModel
{
    private Func<WorkspaceResetRequest, bool> confirmReset = _ => false;
    private Func<bool> isResultsExecutionBusy = () => false;
    public RelayCommand ResetGeneratedFilesCommand { get; private set; } = null!;
    private void InitializeResetCommand(Func<WorkspaceResetRequest, bool>? confirm, Func<bool>? executionBusy)
    {
        confirmReset = confirm ?? (_ => false);
        isResultsExecutionBusy = executionBusy ?? (() => false);
        ResetGeneratedFilesCommand = new RelayCommand(async () => await ResetGeneratedFilesAsync(), () => CanResetGeneratedFiles);
    }
    private bool CanResetGeneratedFiles => CanModifyWorkspace && !resolvingNavigation && !HasUnsavedChanges
        && !isResultsExecutionBusy() && !HasStructuralErrors && savedDocument is not null && Selected is not null && savedNodeIds.Contains(Selected.Id);
    internal void NotifyResetAvailability()
    {
        ResetGeneratedFilesCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ResetGeneratedFilesHint));
        ExportWorkspaceCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ExportWorkspaceHint));
    }
    public string ResetGeneratedFilesHint => isResultsExecutionBusy()
        ? "Wait for Results execution to finish or finish cancelling before resetting generated files."
        : HasUnsavedChanges
        ? "Save or discard workspace changes before resetting generated files."
        : "Reset generated parameter files and data for the selected item and all descendants. Confirmation is required. Ancestors unlock when no descendant run has generated parameter files.";

    public async Task ResetGeneratedFilesAsync()
    {
        if (!CanResetGeneratedFiles) return;
        var node = Selected!;
        var nodes = node.DescendantsAndSelf().ToArray();
        var request = new WorkspaceResetRequest(SelectedConfigurationPath, node.IsRun,
            nodes.Count(n => n.IsProject), nodes.Count(n => n.IsRun));
        if (!confirmReset(request)) return;
        var previous = savedDocument!;
        await RunAsync(async token =>
        {
            Status = $"Resetting generated files for '{request.Path}'…";
            var result = await Task.Run(() => WorkspaceFileStore.ResetGeneratedFiles(WorkspaceFolder, previous,
                node.Kind.ToString().ToLowerInvariant(), node.Id, token), token);
            // Remove cached file rows and generation statuses along with their files.
            foreach (var run in nodes.Where(n => n.IsRun)) executions.Remove(run.Id);
            AcceptSavedDocument(result.Definition);
            Selected = Roots.SelectMany(root => root.DescendantsAndSelf()).Single(n => n.Kind == node.Kind && n.Id == node.Id);
            Error = string.Join(Environment.NewLine, result.CleanupWarnings);
            Status = $"Reset generated files for '{request.Path}'. Projects and modules with no remaining descendant parameter files are editable.";
            if (Selected.IsRun)
                await SelectedExecution!.Files.RefreshAsync(RunParametersFolder, Selected.Parent!.Id, Selected.Id);
        });
    }
}
