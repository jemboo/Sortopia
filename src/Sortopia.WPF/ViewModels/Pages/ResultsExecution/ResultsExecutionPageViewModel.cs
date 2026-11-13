using System.IO;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.ResultsExecution;

public sealed record RunFileChoice(Guid? Id, string Label);

public sealed class WorkItemStatusRow(string path, runParameters? parameters, workItemOutputStatus? inspection, string error = "", TimeProvider? timeProvider = null) : ViewModelBase
{
    private bool selected;
    private workItemExecutionProgress? execution;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private long? started;
    private TimeSpan? elapsed;
    public string Path => path;
    public string FileName => System.IO.Path.GetFileName(path);
    public runParameters? Parameters => parameters;
    public Guid? RunId => parameters?.RunId;
    public string FunctionName => parameters?.Results.FunctionName ?? "Unreadable";
    public outputState State => execution?.State == workItemExecutionState.Complete ? outputState.Complete : inspection?.Outputs.State ?? outputState.Invalid;
    public string Status => execution?.State.ToString() ?? State.ToString();
    public TimeSpan? Elapsed => started is { } timestamp ? clock.GetElapsedTime(timestamp) : elapsed;
    public string ElapsedText => Elapsed is { } value ? $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds / 100}" : "—";
    public string OutputSummary => inspection is null ? "" : $"{(State == outputState.Complete ? inspection.Outputs.ExpectedCount : inspection.Outputs.Available.Length)}/{inspection.Outputs.ExpectedCount} outputs";
    public string Error => execution is { Error.Length: > 0 } ? execution.Error : error.Length > 0 ? error : inspection?.Outputs.Error ?? "";
    public bool HasError => Error.Length > 0;
    public bool CanExecute => parameters is not null && State != outputState.Invalid;
    public string[] Cells { get; private set; } = [];
    public bool IsSelected { get => selected; set { if (SetProperty(ref selected, value)) SelectionChanged?.Invoke(); } }
    internal event Action? SelectionChanged;
    internal void UpdateExecution(workItemExecutionProgress update)
    {
        if (update.State == workItemExecutionState.Running)
        {
            if (started is null) started = clock.GetTimestamp();
            elapsed = null;
        }
        else
        {
            // Validation failures have no running interval. Do not invent a duration.
            elapsed = started is { } timestamp ? clock.GetElapsedTime(timestamp) : null;
            started = null;
        }
        execution = update;
        foreach (var property in new[] { nameof(Status), nameof(State), nameof(OutputSummary), nameof(Error), nameof(HasError), nameof(CanExecute) }) OnPropertyChanged(property);
        RefreshElapsed();
    }
    internal void RestoreElapsed(TimeSpan value) { elapsed = value; RefreshElapsed(); }
    internal void RefreshElapsed() { OnPropertyChanged(nameof(Elapsed)); OnPropertyChanged(nameof(ElapsedText)); }
    internal void SetColumns(IEnumerable<string> keys)
    {
        Cells = keys.Select(key => parameters?.Parameters.TryFind(key)?.Value.FormattedValue ?? "—").ToArray();
        OnPropertyChanged(nameof(Cells));
    }
}

/// Executes an explicit selection of existing parameter files; never runs setup.
public sealed class ResultsExecutionPageViewModel : ViewModelBase, IDisposable
{
    private readonly Func<string?, string?> pickFolder;
    private Action goBack;
    private readonly Action<int>? saveParallelJobs;
    private readonly TimeProvider clock;
    private string parallelJobs;
    private CancellationTokenSource? cancellation;
    private string folder = "", status = "Choose a project's runParameters folder.", error = "", returnLabel = "← Back to Sortopia";
    private RunFileChoice? selectedRun;
    private IReadOnlyList<RunFileChoice> runs = [];
    private IReadOnlyList<WorkItemStatusRow> rows = [];
    private string[] parameterColumns = [];
    private WorkItemStatusRow[] files = [];
    // Keep failed/cancelled attempts inspectable during this folder session; no tracking files.
    private readonly Dictionary<Guid, workItemExecutionProgress> outcomes = [];
    private readonly Dictionary<Guid, TimeSpan> elapsedTimes = [];
    private readonly HashSet<WorkItemStatusRow> runningRows = [];
    private ResultsExecutionProgress executionProgress = new(0);
    private bool changingSelection;
    public ResultsExecutionPageViewModel(Action goBack, Func<string?, string?> pickFolder, Func<int>? loadParallelJobs = null, Action<int>? saveParallelJobs = null, TimeProvider? timeProvider = null)
    {
        this.goBack = goBack; this.pickFolder = pickFolder;
        this.saveParallelJobs = saveParallelJobs;
        clock = timeProvider ?? TimeProvider.System;
        parallelJobs = Math.Max(1, loadParallelJobs?.Invoke() ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        ReturnCommand = new RelayCommand(() => { Cancel(); this.goBack(); });
        BrowseCommand = new RelayCommand(async () =>
        {
            try { if (this.pickFolder(Folder) is { } path) await SetFolderAsync(path); }
            catch (Exception e) { Error = e.GetBaseException().Message; }
        }, () => !IsBusy);
        RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy && Folder.Length > 0);
        ExecuteCommand = new RelayCommand(async () => await ExecuteAsync(), () => CanExecute);
        SelectAllCommand = new RelayCommand(() => SelectRows(_ => true), () => !IsBusy && Rows.Count > 0);
        SelectPendingCommand = new RelayCommand(() => SelectRows(row => row.State is outputState.Pending or outputState.Partial), () => !IsBusy && Rows.Count > 0);
        ClearSelectionCommand = new RelayCommand(() => SelectRows(_ => false), () => !IsBusy && SelectedCount > 0);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }
    public string Folder { get => folder; private set => SetProperty(ref folder, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string ParallelJobs
    {
        get => parallelJobs;
        set
        {
            if (!SetProperty(ref parallelJobs, value)) return;
            OnPropertyChanged(nameof(ParallelJobsError));
            if (ParallelJobsError.Length == 0)
            {
                try { saveParallelJobs?.Invoke(int.Parse(value)); }
                catch (Exception e) { Error = $"Could not remember the parallel jobs setting: {e.GetBaseException().Message}"; }
            }
            Notify();
        }
    }
    public string ParallelJobsError => int.TryParse(ParallelJobs, out var count) && count > 0 ? "" : "Enter a positive whole number of parallel jobs.";
    public string ReturnLabel { get => returnLabel; private set => SetProperty(ref returnLabel, value); }
    public IReadOnlyList<RunFileChoice> Runs { get => runs; private set => SetProperty(ref runs, value); }
    public IReadOnlyList<WorkItemStatusRow> Rows { get => rows; private set => SetProperty(ref rows, value); }
    public string[] ParameterColumns { get => parameterColumns; private set { if (!parameterColumns.SequenceEqual(value)) SetProperty(ref parameterColumns, value); } }
    public RunFileChoice? SelectedRun
    {
        get => selectedRun;
        set { if (SetProperty(ref selectedRun, value)) { SelectRows(_ => false); ApplyFilter(); } }
    }
    public int FileCount => Rows.Count;
    public int SelectedCount => Rows.Count(row => row.IsSelected);
    public string SelectionSummary => $"{SelectedCount:N0} of {FileCount:N0} files selected."
        + (Rows.Any(row => row.IsSelected && !row.CanExecute) ? " Some selected files cannot run. Inspect Error details, then fix and refresh, or deselect them." : "");
    public string CommandLine => string.Join(Environment.NewLine, Rows.Where(row => row.IsSelected && row.CanExecute)
        .Select(row => $"sortopia execute-results --parameters \"{row.Path}\""));
    public bool IsBusy => cancellation is not null;
    public bool CanChooseRun => !IsBusy;
    public bool CanExecute => !IsBusy && ParallelJobsError.Length == 0 && SelectedCount > 0 && Rows.Where(row => row.IsSelected).All(row => row.CanExecute);
    public runExecutionReport? LastReport { get; private set; }
    public ResultsExecutionProgress ExecutionProgress { get => executionProgress; private set => SetProperty(ref executionProgress, value); }
    public RelayCommand ReturnCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ExecuteCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectPendingCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand CancelCommand { get; }

    public void SetReturnAction(Action action, bool toWorkspace)
    { goBack = action; ReturnLabel = toWorkspace ? "← Back to workspace" : "← Back to Sortopia"; }

    public async Task SetFolderAsync(string path, Guid? runId = null)
    {
        Cancel();
        outcomes.Clear();
        elapsedTimes.Clear(); runningRows.Clear();
        ExecutionProgress = new(0);
        foreach (var row in files) row.SelectionChanged -= Notify;
        files = []; Rows = []; Runs = []; ParameterColumns = [];
        selectedRun = null; OnPropertyChanged(nameof(SelectedRun));
        Folder = path; LastReport = null;
        await WorkAsync(token => LoadCoreAsync(runId, [], token));
    }
    public Task RefreshAsync() => WorkAsync(token => LoadCoreAsync(SelectedRun?.Id, SelectedPaths(), token));
    private string[] SelectedPaths() => Rows.Where(row => row.IsSelected).Select(row => row.Path).ToArray();
    private void SelectRows(Func<WorkItemStatusRow, bool> select)
    {
        changingSelection = true;
        try { foreach (var row in Rows) row.IsSelected = select(row); }
        finally { changingSelection = false; Notify(); }
    }
    public void UpdateSelection(IEnumerable<WorkItemStatusRow> added, IEnumerable<WorkItemStatusRow> removed)
    {
        changingSelection = true;
        try
        {
            foreach (var row in removed) row.IsSelected = false;
            foreach (var row in added) row.IsSelected = true;
        }
        finally { changingSelection = false; Notify(); }
    }
    private void ApplyFilter()
    {
        Rows = files.Where(row => SelectedRun?.Id is not { } id || row.RunId == id).ToArray();
        ParameterColumns = Rows.Where(row => row.Parameters is not null).SelectMany(row =>
        {
            var definition = ResultsFunctionCatalog.functions.TryGetValue(row.FunctionName, out var known) ? known : null;
            return (definition?.RequiredParameters.Select(key => key.Name) ?? []).Concat(row.Parameters!.Parameters.ParamMap.Keys);
        }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var row in Rows) row.SetColumns(ParameterColumns);
        Status = string.Join(" · ", new[] { "Pending", "Partial", "Running", "Complete", "Failed", "Cancelled", "Invalid" }
            .Select(state => (State: state, Count: Rows.Count(row => row.Status == state)))
            .Where(value => value.Count > 0 || value.State is "Pending" or "Complete")
            .Select(value => $"{value.Count:N0} {value.State.ToLowerInvariant()}"));
        if (SelectedRun?.Id is not null && files.Any(row => row.Parameters is null))
            Status += ". Unreadable files cannot be assigned to a run; choose All runs to inspect them.";
        Notify();
    }
    private async Task LoadCoreAsync(Guid? runId, string[] selectedPaths, CancellationToken token)
    {
        var path = Folder;
        var selected = selectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Status = "Reading parameter files and checking their outputs…";
        var loaded = await Task.Run(() =>
        {
            if (path.Length == 0 || !Directory.Exists(path)) return [];
            RunParameterFiles.projectFolder(path);
            return Directory.GetFiles(path, "*.msgpack").Order(StringComparer.Ordinal).Select(file =>
            {
                token.ThrowIfCancellationRequested();
                runParameters? parameters = null;
                try
                {
                    parameters = RunParameterFiles.loadFile(file);
                    return new WorkItemStatusRow(file, parameters, RunParameterExecution.inspectFile(file, token), timeProvider: clock);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { return new WorkItemStatusRow(file, parameters, null, e.GetBaseException().Message, clock); }
            }).ToArray();
        }, token);
        token.ThrowIfCancellationRequested();
        // A file removed after selection still needs a row for its execution error.
        var loadedPaths = loaded.Select(row => row.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        loaded = [.. loaded, .. files.Where(row => !loadedPaths.Contains(row.Path) && row.Parameters is not null
                && outcomes.TryGetValue(row.Parameters.Id, out var outcome) && outcome.State == workItemExecutionState.Failed)
            .Select(row => new WorkItemStatusRow(row.Path, row.Parameters, null, "The parameter file no longer exists.", clock))];
        foreach (var row in loaded)
            if (Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(row.Path), out var id))
            {
                if (outcomes.TryGetValue(id, out var outcome)) row.UpdateExecution(outcome);
                if (elapsedTimes.TryGetValue(id, out var elapsed)) row.RestoreElapsed(elapsed);
            }
        foreach (var row in files) row.SelectionChanged -= Notify;
        files = loaded;
        Runs = [new(null, $"All runs · {files.Length:N0} files"), .. files.Where(row => row.RunId is not null).GroupBy(row => row.RunId)
            .Select(group => new RunFileChoice(group.Key, $"{group.Key} · {group.Count():N0} files · {string.Join(", ", group.Select(row => row.FunctionName).Distinct())}"))];
        if (runId is { } requested && !Runs.Any(item => item.Id == requested))
            Runs = [.. Runs, new(requested, $"{requested} · 0 files · Generate this run's parameters first")];
        selectedRun = Runs.FirstOrDefault(item => item.Id == runId) ?? Runs[0]; OnPropertyChanged(nameof(SelectedRun));
        foreach (var row in files) row.SelectionChanged += Notify;
        ApplyFilter();
        SelectRows(row => selected.Contains(row.Path));
        if (path.Length == 0) Status = "Select a saved run or browse to a runParameters folder.";
        else if (!Directory.Exists(path)) Status = "No runParameters folder yet. Generate run parameters locally first.";
    }
    public Task ExecuteAsync() => CanExecute ? WorkAsync(async token =>
    {
        var paths = SelectedPaths(); var runId = SelectedRun?.Id;
        var options = new resultsExecutionOptions(int.Parse(ParallelJobs));
        var selectedRows = Rows.Where(row => row.IsSelected).ToDictionary(row => row.Parameters!.Id);
        ExecutionProgress = new(paths.Length);
        var batch = ExecutionProgress;
        Status = $"Executing {paths.Length:N0} selected files…";
        var acceptingProgress = true;
        var progress = new Progress<runExecutionProgress>(value =>
        { if (acceptingProgress && !token.IsCancellationRequested) Status = $"{value.Message} ({value.Completed}/{value.Total})"; });
        var rowProgress = new RowProgress(SynchronizationContext.Current, update =>
        {
            if (cancellation?.Token != token || !selectedRows.TryGetValue(update.Id, out var row)) return;
            batch.Record(update.Id, update.State);
            if (cancellation?.Token != token) return;
            if (update.State is workItemExecutionState.Failed or workItemExecutionState.Cancelled) outcomes[update.Id] = update;
            else outcomes.Remove(update.Id);
            if (update.State == workItemExecutionState.Running) runningRows.Add(row);
            else runningRows.Remove(row);
            row.UpdateExecution(update);
            // A row notification can navigate to another folder. Do not restore old state there.
            if (cancellation?.Token != token) return;
            if (update.State != workItemExecutionState.Running && row.Elapsed is { } elapsed) elapsedTimes[update.Id] = elapsed;
            else elapsedTimes.Remove(update.Id);
        });
        runExecutionReport report;
        try { report = await Task.Run(() => RunParameterExecution.executeFilesWithOptions(paths, options, progress, rowProgress, token), token); }
        finally { acceptingProgress = false; }
        if (cancellation?.Token != token) return;
        LastReport = report;
        if (report.Status == runExecutionStatus.Cancelled || token.IsCancellationRequested)
        {
            Error = report.Error;
            Status = "Cancelled. Completed outputs are retained; refresh to check any partial outputs.";
            return;
        }
        await LoadCoreAsync(runId, paths, token);
        if (report.Status != runExecutionStatus.Completed)
        { Error = report.Error; Status = $"{report.Status}: {report.Results.Length:N0} selected files processed. {Status}"; }
    }) : Task.CompletedTask;
    internal void RefreshElapsedTimes()
    {
        foreach (var row in runningRows.ToArray()) row.RefreshElapsed();
    }
    private sealed class RowProgress(SynchronizationContext? context, Action<workItemExecutionProgress> receive) : IProgress<workItemExecutionProgress>
    {
        private readonly object sync = new();
        // Apply each transition on the UI thread before the worker proceeds. In particular,
        // terminal events must not be lost behind the final folder refresh or cancellation.
        public void Report(workItemExecutionProgress value)
        {
            if (context is null) { lock (sync) receive(value); }
            else if (ReferenceEquals(context, SynchronizationContext.Current)) receive(value);
            else context.Send(_ => receive(value), null);
        }
    }
    private async Task WorkAsync(Func<CancellationToken, Task> action)
    {
        cancellation?.Cancel();
        using var request = new CancellationTokenSource();
        cancellation = request; Error = ""; Notify();
        try { await action(request.Token); }
        catch (OperationCanceledException) { if (ReferenceEquals(cancellation, request)) Status = "Cancelled. Completed outputs are retained; refresh to update file statuses."; }
        catch (Exception e) { if (ReferenceEquals(cancellation, request)) { Error = e.GetBaseException().Message; Status = "The operation could not complete. Refresh after correcting the reported problem."; } }
        finally { if (ReferenceEquals(cancellation, request)) { cancellation = null; Notify(); } }
    }
    private void Notify()
    {
        if (changingSelection) return;
        foreach (var property in new[] { nameof(IsBusy), nameof(CanChooseRun), nameof(CanExecute), nameof(FileCount), nameof(SelectedCount), nameof(SelectionSummary), nameof(CommandLine) }) OnPropertyChanged(property);
        foreach (var command in new[] { BrowseCommand, RefreshCommand, ExecuteCommand, SelectAllCommand, SelectPendingCommand, ClearSelectionCommand, CancelCommand }) command?.RaiseCanExecuteChanged();
    }
    public void Cancel() => cancellation?.Cancel();
    public void Dispose() { Cancel(); foreach (var row in files) row.SelectionChanged -= Notify; }
}
