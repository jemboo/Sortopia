using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspacePreparationChoice(preparedRun Preparation)
{
    public Guid Id => Preparation.Id;
    public string Label => $"{Preparation.CreatedUtc.ToLocalTime():g} · {Preparation.WorkItemIds.Length:N0} work items · {Id.ToString()[..8]}";
}

public sealed partial class WorkspacePageViewModel
{
    public RelayCommand PrepareRunCommand { get; private set; } = null!;
    public RelayCommand ExecutePreparedRunCommand { get; private set; } = null!;
    public RelayCommand RefreshPreparationsCommand { get; private set; } = null!;
    public RelayCommand OpenPreparedProjectFolderCommand { get; private set; } = null!;
    public IReadOnlyList<WorkspacePreparationChoice> Preparations { get; private set; } = [];
    private WorkspacePreparationChoice? selectedPreparation;
    public WorkspacePreparationChoice? SelectedPreparation
    {
        get => selectedPreparation;
        set
        {
            if (!SetProperty(ref selectedPreparation, value)) return;
            OnPropertyChanged(nameof(PreparedExecutionHint));
            OnPropertyChanged(nameof(PreparedCommand));
            ExecutePreparedRunCommand?.RaiseCanExecuteChanged();
        }
    }
    public string PreparationLoadError { get; private set; } = "";
    private string? PreparedProjectFolder => SelectedSavedRun is { } run && savedDefinition is not null
        ? WorkspaceData.projectFolder(savedDefinition, WorkspaceFolder, run.ParentProjectId) : null;
    public string PreparedExecutionHint => SelectedPreparation is { } choice
        ? $"Saved preparation: {choice.Preparation.WorkItemIds.Length:N0} results work items from {choice.Preparation.SetupSetCount:N0} setup sets. These inputs are fixed; later run edits require a new preparation. Copy the complete project folder to execute elsewhere."
        : "Prepare this saved run locally first. Preparation saves parameter files and all setup inputs, without executing results.";
    public string PreparedCommand => SelectedPreparation is { } choice && PreparedProjectFolder is { } folder
        ? $"sortopia execute-results --project \"{folder}\" --preparation {choice.Id}"
        : "";
    private bool CanExecutePrepared => CanEdit && !HasUnsavedChanges && !HasEditErrors
        && SelectedSavedRun is not null && SelectedPreparation is not null;

    private void InitializePreparedRunCommands()
    {
        PrepareRunCommand = new RelayCommand(async () => await PrepareRunAsync(), () => CanExecuteRun);
        ExecutePreparedRunCommand = new RelayCommand(async () => await ExecutePreparedRunAsync(), () => CanExecutePrepared);
        RefreshPreparationsCommand = new RelayCommand(RefreshPreparations, () => CanEdit && SelectedSavedRun is not null);
        OpenPreparedProjectFolderCommand = new RelayCommand(() =>
        {
            try { openFolder!(PreparedProjectFolder!); }
            catch (Exception error) { Error = $"Could not open the project folder: {error.Message}"; }
        }, () => CanEdit && openFolder is not null && SelectedSavedRun is not null);
    }

    public void RefreshPreparations()
    {
        var previous = SelectedPreparation?.Id;
        try
        {
            Preparations = PreparedProjectFolder is { } folder && SelectedSavedRun is { } run
                ? PreparedRunFiles.listPreparations(folder).Where(item => item.RunId == run.Id)
                    .Select(item => new WorkspacePreparationChoice(item)).ToArray() : [];
            PreparationLoadError = "";
        }
        catch (Exception error)
        {
            Preparations = [];
            PreparationLoadError = $"Saved preparations could not be read: {error.GetBaseException().Message}";
        }
        OnPropertyChanged(nameof(Preparations));
        SelectedPreparation = Preparations.FirstOrDefault(item => item.Id == previous) ?? Preparations.FirstOrDefault();
        OnPropertyChanged(nameof(PreparationLoadError));
        OnPropertyChanged(nameof(PreparedExecutionHint));
        OnPropertyChanged(nameof(PreparedCommand));
    }

    private void NotifyPreparedRunCommands()
    {
        PrepareRunCommand.RaiseCanExecuteChanged();
        ExecutePreparedRunCommand.RaiseCanExecuteChanged();
        RefreshPreparationsCommand.RaiseCanExecuteChanged();
        OpenPreparedProjectFolderCommand.RaiseCanExecuteChanged();
    }

    public Task PrepareRunAsync()
    {
        if (!CanExecuteRun) return Task.CompletedTask;
        var value = SelectedSavedRun!;
        var workspace = savedDefinition!;
        var root = WorkspaceFolder;
        var state = SelectedExecution!;
        return RunAsync(async token =>
        {
            state.Begin("Preparing locally…");
            Status = $"Preparing '{value.RunName}'…";
            var acceptingProgress = true;
            var progress = new ThrottledExecutionProgress(new Progress<runExecutionProgress>(update =>
            {
                if (acceptingProgress && state.IsRunning) state.Update(update);
            }));
            try
            {
                var report = await Task.Run(() => PreparedRunExecution.prepare(workspace, root, value, progress, token));
                acceptingProgress = false;
                state.FinishPreparation(report);
                RefreshPreparations();
                if (report.Preparation is { } prepared)
                    SelectedPreparation = Preparations.FirstOrDefault(item => item.Id == prepared.Value.Id);
                Status = $"Run '{value.RunName}': {state.Summary}";
                Error = report.Execution.Error;
            }
            catch (Exception error)
            {
                acceptingProgress = false;
                state.Finish(new runExecutionReport(runExecutionStatus.Failed, [], [], error.GetBaseException().Message));
                Error = state.Error;
                Status = "Preparation failed.";
            }
        });
    }

    public Task ExecutePreparedRunAsync()
    {
        if (!CanExecutePrepared) return Task.CompletedTask;
        var choice = SelectedPreparation!;
        var folder = PreparedProjectFolder!;
        var state = SelectedExecution!;
        return RunAsync(async token =>
        {
            state.Begin("Executing prepared results…");
            Status = "Executing prepared results…";
            var acceptingProgress = true;
            var progress = new ThrottledExecutionProgress(new Progress<runExecutionProgress>(update =>
            {
                if (acceptingProgress && state.IsRunning) state.Update(update);
            }));
            try
            {
                var report = await Task.Run(() => PreparedRunExecution.executePreparation(folder, choice.Id, progress, token));
                acceptingProgress = false;
                state.Finish(report);
                Error = report.Error;
                Status = state.Summary;
            }
            catch (Exception error)
            {
                acceptingProgress = false;
                state.Finish(new runExecutionReport(runExecutionStatus.Failed, [], [], error.GetBaseException().Message));
                Error = state.Error;
                Status = "Prepared results execution failed.";
            }
        });
    }
}
