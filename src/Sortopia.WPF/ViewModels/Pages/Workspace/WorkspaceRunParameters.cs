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
            // Prepared work is independent of the editable configuration's validity.
            if (Selected?.IsRun != true || savedDocument is null) return "";
            var file = Roots.SelectMany(root => root.DescendantsAndSelf())
                .FirstOrDefault(node => node.IsRun && node.Id == Selected.Id)?.SavedFolderPath;
            return file is null ? "" : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(file))!, "runParameters");
        }
    }
    public Task OpenExecutePageAsync() => ExecutePageCommand.CanExecute(null)
        ? openResultsExecution!(RunParametersFolder, Selected!.Id) : Task.CompletedTask;
    private void NotifyRunParameterCommands()
    {
        OnPropertyChanged(nameof(RunParametersFolder));
        GenerateRunParametersCommand.RaiseCanExecuteChanged(); ExecutePageCommand.RaiseCanExecuteChanged();
    }

    public Task GenerateRunParametersAsync()
    {
        if (!CanGenerateRunParameters) return Task.CompletedTask;
        var scope = savedRunScope!;
        var value = scope.Run;
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
                var report = await Task.Run(() => RunParameterExecution.generateInScope(scope, root, progress, token));
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
