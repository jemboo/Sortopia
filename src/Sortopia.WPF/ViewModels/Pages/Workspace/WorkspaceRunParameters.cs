using System.IO;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspacePageViewModel
{
    public RelayCommand GenerateRunParametersCommand { get; private set; } = null!;
    public RelayCommand ExecutePageCommand { get; private set; } = null!;
    public RelayCommand RefreshRunParameterFilesCommand { get; private set; } = null!;
    private Func<string, Guid?, Task>? openResultsExecution;
    private void InitializeRunParameterCommands(Func<string, Guid?, Task>? openResultsExecution)
    {
        this.openResultsExecution = openResultsExecution;
        GenerateRunParametersCommand = new RelayCommand(async () => await GenerateRunParametersAsync(), () => CanGenerateRunParameters);
        ExecutePageCommand = new RelayCommand(async () => await OpenExecutePageAsync(), () => CanModifyWorkspace && this.openResultsExecution is not null && RunParametersFolder.Length > 0);
        RefreshRunParameterFilesCommand = new RelayCommand(async () => await RefreshRunParameterFilesAsync(),
            () => !IsBusy && Selected is { IsRun: true } && SelectedExecution?.Files.IsRefreshing != true);
    }
    public string RunParameterFilesDraftNotice => HasUnsavedChanges
        ? "Unsaved workspace edits are shown in the preview. Saved files retain the configuration used when they were generated. Save relevant edits before generating files."
        : "The preview uses the current configuration. The table shows saved files, including files from earlier generations.";
    public string RunParameterPreviewSummary
    {
        get
        {
            if (Selected is not { IsRun: true, Editor: { } editor }) return "";
            var results = editor.ResultsParameterSetsPreview;
            var setup = editor.SetupParameterSetsPreview;
            if (results.Error.Length > 0 || Selected.IsBranch && setup.Error.Length > 0)
                return "Preview unavailable until the parameter errors are corrected. Existing files can still be inspected.";
            return Selected.IsBranch ? $"Preview: {setup.TotalCount:N0} setup × {results.TotalCount:N0} results = {setup.TotalCount * results.TotalCount:N0} file combinations. No files are written by this preview."
                : $"Preview: {results.TotalCount:N0} results parameter set(s). No files are written by this preview.";
        }
    }
    public Task RefreshRunParameterFilesAsync() => IsBusy || Selected is not { IsRun: true, Parent: { } parent } node
        ? Task.CompletedTask : SelectedExecution!.Files.RefreshAsync(RunParametersFolder, parent.Id, node.Id);
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
        OnPropertyChanged(nameof(RunParameterFilesDraftNotice)); OnPropertyChanged(nameof(RunParameterPreviewSummary));
        GenerateRunParametersCommand.RaiseCanExecuteChanged(); ExecutePageCommand.RaiseCanExecuteChanged();
        RefreshRunParameterFilesCommand.RaiseCanExecuteChanged();
    }

    public Task GenerateRunParametersAsync()
    {
        if (!CanGenerateRunParameters) return Task.CompletedTask;
        var scope = savedRunScope!;
        var value = scope.Run;
        var root = WorkspaceFolder;
        var state = SelectedExecution!;
        var saved = savedDocument!;
        var filesFolder = RunParametersFolder;
        var fileState = state.Files;
        return RunAsync(async token =>
        {
            state.Begin("Generating run parameters locally…");
            Status = $"Generating parameter files for '{value.RunName}'…";
            var acceptingProgress = true;
            Guid[] generatedIds = [];
            var progress = new ThrottledExecutionProgress(new Progress<runExecutionProgress>(update =>
            {
                if (acceptingProgress && state.IsRunning) state.Update(update);
            }));
            try
            {
                var generated = await Task.Run(() => WorkspaceRunGeneration.generate(root, saved, value.Id, progress, token));
                if (!ReferenceEquals(saved, generated.Document))
                {
                    var dirty = HasUnsavedChanges;
                    savedDocument = generated.Document;
                    ClearRunScope();
                    Roots.SelectMany(node => node.DescendantsAndSelf()).Single(node => node.IsProject && node.Id == scope.Parent.Id).MarkSealed(generated.Scope.Parent);
                    HasUnsavedChanges = dirty;
                    NotifyDraftProperties();
                }
                var report = generated.Report;
                generatedIds = report.WorkItemIds;
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
            finally { await fileState.RefreshAsync(filesFolder, value.ParentProjectId, value.Id, generatedIds); }
        });
    }
}
