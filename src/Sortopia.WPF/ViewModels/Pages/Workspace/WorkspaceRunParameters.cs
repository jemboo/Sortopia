using System.IO;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspacePageViewModel
{
    public RelayCommand GenerateRunParametersCommand { get; private set; } = null!;
    public RelayCommand ExecutePageCommand { get; private set; } = null!;
    private Func<string, Guid?, Task>? openResultsExecution;
    private void InitializeRunParameterCommands(Func<string, Guid?, Task>? openResultsExecution)
    {
        this.openResultsExecution = openResultsExecution;
        GenerateRunParametersCommand = new RelayCommand(async () => await GenerateRunParametersAsync(), () => CanGenerateRunParameters);
        ExecutePageCommand = new RelayCommand(async () => await OpenExecutePageAsync(), () => !IsBusy && this.openResultsExecution is not null && RunParametersFolder.Length > 0);
    }
    public string RunParametersFolder
    {
        get
        {
            var run = SelectedSavedRun;
            return run is not null && savedDefinition is not null
                ? Path.Combine(WorkspaceData.projectFolder(savedDefinition, WorkspaceFolder, run.ParentProjectId), "runParameters") : "";
        }
    }
    public Task OpenExecutePageAsync() => ExecutePageCommand.CanExecute(null)
        ? openResultsExecution!(RunParametersFolder, SelectedSavedRun!.Id) : Task.CompletedTask;
    private void NotifyRunParameterCommands()
    {
        OnPropertyChanged(nameof(RunParametersFolder));
        GenerateRunParametersCommand.RaiseCanExecuteChanged(); ExecutePageCommand.RaiseCanExecuteChanged();
    }

    public Task GenerateRunParametersAsync()
    {
        if (!CanGenerateRunParameters) return Task.CompletedTask;
        var value = SelectedSavedRun!;
        var workspace = savedDefinition!;
        var root = WorkspaceFolder;
        var state = SelectedExecution!;
        return RunAsync(async token =>
        {
            state.Begin("Generating run parameters locally…");
            Status = $"Generating parameter files for '{value.RunName}'…";
            var acceptingProgress = true;
            var progress = new ThrottledExecutionProgress(new Progress<runExecutionProgress>(update =>
            {
                if (acceptingProgress && state.IsRunning) state.Update(update);
            }));
            try
            {
                var report = await Task.Run(() => RunParameterExecution.generate(workspace, root, value, progress, token));
                acceptingProgress = false;
                state.FinishGeneration(report);
                Status = $"Run '{value.RunName}': {state.Summary}";
                Error = report.Execution.Error;
            }
            catch (Exception error)
            {
                acceptingProgress = false;
                state.Finish(new runExecutionReport(runExecutionStatus.Failed, [], [], error.GetBaseException().Message));
                Error = state.Error;
                Status = "Parameter generation failed.";
            }
        });
    }
}
