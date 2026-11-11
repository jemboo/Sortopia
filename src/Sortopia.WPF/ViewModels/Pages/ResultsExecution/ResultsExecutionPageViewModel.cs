using System.IO;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.ResultsExecution;

public sealed record RunFileChoice(Guid? Id, string Label);
public sealed record WorkItemStatusRow(workItemOutputStatus Value)
{
    public string Label => $"{Value.Outputs.State} · {Value.Outputs.Available.Length}/{Value.Outputs.ExpectedCount} outputs · {Value.Id}";
    public string Error => Value.Outputs.Error;
}

/// Shared by the workspace run editor and the standalone folder-execution page.
public sealed class ResultsExecutionPageViewModel : ViewModelBase, IDisposable
{
    private readonly Func<string?, string?> pickFolder;
    private CancellationTokenSource? cancellation;
    private string folder = "", status = "Choose a project's runParameters folder.", error = "";
    private RunFileChoice? selectedRun;
    private IReadOnlyList<RunFileChoice> runs = [];
    private IReadOnlyList<WorkItemStatusRow> outputs = [];
    private runParameters[] files = [];
    public ResultsExecutionPageViewModel(Action goBack, Func<string?, string?> pickFolder)
    {
        this.pickFolder = pickFolder;
        ReturnCommand = new RelayCommand(() => { Cancel(); goBack(); });
        BrowseCommand = new RelayCommand(async () =>
        {
            try { if (this.pickFolder(Folder) is { } path) await SetFolderAsync(path); }
            catch (Exception e) { Error = e.GetBaseException().Message; }
        }, () => !IsBusy);
        RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy && Folder.Length > 0);
        CheckCommand = new RelayCommand(async () => await CheckAsync(), () => CanExecute);
        ExecuteCommand = new RelayCommand(async () => await ExecuteAsync(), () => CanExecute);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }
    public string Folder { get => folder; private set => SetProperty(ref folder, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public IReadOnlyList<RunFileChoice> Runs { get => runs; private set => SetProperty(ref runs, value); }
    public IReadOnlyList<WorkItemStatusRow> Outputs { get => outputs; private set => SetProperty(ref outputs, value); }
    public RunFileChoice? SelectedRun
    {
        get => selectedRun;
        set { if (SetProperty(ref selectedRun, value)) { Outputs = []; Status = SelectionSummary; Notify(); } }
    }
    public int FileCount => files.Count(item => SelectedRun?.Id is not { } id || item.RunId == id);
    public string SelectionSummary => $"{FileCount:N0} parameter files selected. Every matching file in this folder is eligible, including older files. Check outputs to see pending work.";
    public string CommandLine => Folder.Length == 0 ? "" : $"sortopia execute-results --parameters-folder \"{Folder}\"" + (SelectedRun?.Id is { } id ? $" --run {id}" : "");
    public bool IsBusy => cancellation is not null;
    public bool CanChooseRun => !IsBusy;
    public bool CanExecute => !IsBusy && FileCount > 0 && Error.Length == 0;
    public runExecutionReport? LastReport { get; private set; }
    public RelayCommand ReturnCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CheckCommand { get; }
    public RelayCommand ExecuteCommand { get; }
    public RelayCommand CancelCommand { get; }
    private FSharpOption<Guid>? Filter => SelectedRun?.Id is { } id ? FSharpOption<Guid>.Some(id) : null;

    public async Task SetFolderAsync(string path, Guid? runId = null)
    {
        Cancel();
        Folder = path;
        await LoadAsync(runId);
    }
    public Task RefreshAsync() => LoadAsync(SelectedRun?.Id);
    private Task LoadAsync(Guid? runId) => WorkAsync(async token =>
    {
        files = []; Runs = []; SelectedRun = null; Outputs = []; LastReport = null;
        if (Folder.Length == 0) { Status = "Select a saved run or browse to a runParameters folder."; return; }
        if (!Directory.Exists(Folder)) { Status = "No runParameters folder yet. Generate run parameters locally first."; return; }
        var path = Folder;
        var loaded = await Task.Run(() => RunParameterFiles.listWorkItems(path, token), token);
        token.ThrowIfCancellationRequested();
        files = loaded;
        Runs = [new(null, $"All runs · {files.Length:N0} files"), .. files.GroupBy(item => item.RunId).Select(group => new RunFileChoice(group.Key, $"{group.Key} · {group.Count():N0} files · {string.Join(", ", group.Select(item => item.Results.FunctionName).Distinct())}"))];
        if (runId is { } requested && !Runs.Any(item => item.Id == requested))
            Runs = [.. Runs, new(requested, $"{requested} · 0 files · Generate this run's parameters first")];
        SelectedRun = Runs.FirstOrDefault(item => item.Id == runId) ?? Runs[0];
        Status = SelectionSummary;
    });
    public Task CheckAsync() => CanExecute ? WorkAsync(CheckCoreAsync) : Task.CompletedTask;
    private async Task CheckCoreAsync(CancellationToken token)
    {
        Status = "Checking expected output files…";
        var path = Folder; var filter = Filter;
        var values = await Task.Run(() => RunParameterExecution.inspectFolder(path, filter!, token), token);
        token.ThrowIfCancellationRequested();
        Outputs = values.Select(value => new WorkItemStatusRow(value)).ToArray();
        Status = string.Join(" · ", new[] { outputState.Complete, outputState.Partial, outputState.Pending, outputState.Invalid }
            .Select(state => $"{values.Count(value => value.Outputs.State == state):N0} {state.ToString().ToLowerInvariant()}"))
            + ". Complete means all required outputs are available, including shared outputs from another run.";
    }
    public Task ExecuteAsync() => CanExecute ? WorkAsync(async token =>
    {
        var path = Folder; var filter = Filter;
        Outputs = []; Status = "Executing pending results…";
        var acceptingProgress = true;
        var progress = new Progress<runExecutionProgress>(value => { if (acceptingProgress && !token.IsCancellationRequested && IsBusy) Status = $"{value.Message} ({value.Completed}/{value.Total})"; });
        LastReport = await Task.Run(() => RunParameterExecution.executeFolder(path, filter!, progress, token), token);
        acceptingProgress = false;
        token.ThrowIfCancellationRequested();
        if (LastReport.Status != runExecutionStatus.Completed) throw new InvalidOperationException(LastReport.Error);
        await CheckCoreAsync(token);
    }) : Task.CompletedTask;
    private async Task WorkAsync(Func<CancellationToken, Task> action)
    {
        cancellation?.Cancel();
        using var request = new CancellationTokenSource();
        cancellation = request; Error = ""; Notify();
        try { await action(request.Token); }
        catch (OperationCanceledException) { if (ReferenceEquals(cancellation, request)) Status = "Cancelled. Published files and completed outputs are retained."; }
        catch (Exception e) { if (ReferenceEquals(cancellation, request)) { Error = e.GetBaseException().Message; Status = "The operation could not complete. Refresh after correcting the reported problem."; } }
        finally { if (ReferenceEquals(cancellation, request)) { cancellation = null; Notify(); } }
    }
    private void Notify()
    {
        foreach (var property in new[] { nameof(IsBusy), nameof(CanChooseRun), nameof(CanExecute), nameof(FileCount), nameof(CommandLine) }) OnPropertyChanged(property);
        foreach (var command in new[] { BrowseCommand, RefreshCommand, CheckCommand, ExecuteCommand, CancelCommand }) command?.RaiseCanExecuteChanged();
    }
    public void Cancel() => cancellation?.Cancel();
    public void Dispose() => Cancel();
}
