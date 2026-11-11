using System.IO;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.Commands;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspacePageViewModel
{
    public RelayCommand GenerateRunParametersCommand { get; private set; } = null!;
    public ResultsExecutionPageViewModel ResultsExecution { get; private set; } = null!;
    private void InitializeRunParameterCommands()
    {
        ResultsExecution = new ResultsExecutionPageViewModel(() => { }, pickFolder);
        GenerateRunParametersCommand = new RelayCommand(async () => await GenerateRunParametersAsync(), () => CanExecuteRun);
    }
    public Task RefreshRunParameterFilesAsync()
    {
        var run = SelectedSavedRun;
        var folder = run is not null && savedDefinition is not null
            ? Path.Combine(WorkspaceData.projectFolder(savedDefinition, WorkspaceFolder, run.ParentProjectId), "runParameters") : "";
        return ResultsExecution.SetFolderAsync(folder, run?.Id);
    }
    private void RefreshRunParameterFiles() => _ = RefreshRunParameterFilesAsync();
    private void NotifyRunParameterCommands() => GenerateRunParametersCommand.RaiseCanExecuteChanged();

    public Task GenerateRunParametersAsync()
    {
        if (!CanExecuteRun) return Task.CompletedTask;
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
                await RefreshRunParameterFilesAsync();
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
