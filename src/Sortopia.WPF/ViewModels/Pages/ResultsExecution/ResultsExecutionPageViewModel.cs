using System.IO;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.Commands;
using Sortopia.WPF.ViewModels.Shared;

namespace Sortopia.WPF.ViewModels.Pages.ResultsExecution;

public sealed record RunFileChoice(Guid? Id, string Label);

public sealed class WorkItemStatusRow(string path, runParameters? parameters, workItemOutputStatus? initialInspection, string error = "", TimeProvider? timeProvider = null, bool checking = false) : ViewModelBase, ISetupQueryRow
{
    private bool selected;
    private bool selectionLocked;
    private workItemExecutionProgress? execution;
    private workItemOutputStatus? inspection = initialInspection;
    private string inspectionError = error;
    private string inspectionStatus = checking ? "Checking" : "";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private long? started;
    private TimeSpan? elapsed;
    private int? generation;
    private int pendingGeneration = -1;
    private int attemptStartGeneration = parameters?.Sgd is null ? 0 : -1;
    private bool IsComplete => execution?.State == workItemExecutionState.Complete
        || execution is null && inspection?.Outputs.State == outputState.Complete;
    public int? TotalGenerations { get; } = GenerationTotal(parameters);
    public int? CompletedGenerations => IsComplete ? TotalGenerations : generation;
    public string GenerationsText => TotalGenerations is { } total ? $"{(CompletedGenerations is { } completed ? completed.ToString("N0") : "—")} / {total:N0}" : "—";
    private static int? GenerationTotal(runParameters? item) => item?.Sgd is { } controls ? Sortopia.Runs.SgdRun.target(controls.Value) : null;
    public string Path => path;
    public string FileName => System.IO.Path.GetFileName(path);
    public runParameters? Parameters => parameters;
    public Guid? RunId => parameters?.RunId;
    public string FunctionName => parameters?.Results.FunctionName ?? "Unreadable";
    public outputState State => execution?.State == workItemExecutionState.Complete ? outputState.Complete : inspection?.Outputs.State ?? outputState.Invalid;
    public string Status => execution?.State.ToString() ?? (inspectionStatus.Length > 0 ? inspectionStatus : State.ToString());
    public bool IsChecking => inspectionStatus == "Checking";
    public TimeSpan? Elapsed => started is { } timestamp ? clock.GetElapsedTime(timestamp) : elapsed;
    public string ElapsedText => Elapsed is { } value ? $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds / 100}" : "—";
    public TimeSpan? EstimatedRemaining
    {
        get
        {
            if (TotalGenerations is not { } total) return null;
            if (IsComplete) return TimeSpan.Zero;
            if (execution?.State != workItemExecutionState.Running || CompletedGenerations is not { } completed) return null;
            if (completed >= total) return TimeSpan.Zero;
            var baseline = Volatile.Read(ref attemptStartGeneration);
            if (baseline < 0 || completed <= baseline || Elapsed is not { Ticks: > 0 } duration) return null;
            var ticks = (decimal)duration.Ticks * (total - completed) / (completed - baseline);
            return ticks <= TimeSpan.MaxValue.Ticks ? TimeSpan.FromTicks((long)ticks) : null;
        }
    }
    public string EstimatedRemainingText => EstimatedRemaining is { } value ? $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}" : "—";
    public string OutputSummary => inspectionStatus.Length > 0 ? (IsChecking ? "Checking outputs…" : "Outputs not checked") : inspection is null ? "" : $"{(State == outputState.Complete ? inspection.Outputs.ExpectedCount : inspection.Outputs.Available.Length)}/{inspection.Outputs.ExpectedCount} outputs"
        + (parameters?.Reporting is null ? "" : State == outputState.Complete ? " · evolution reports complete" : " · evolution reports required");
    public string Error => execution is { Error.Length: > 0 } ? execution.Error : inspectionError.Length > 0 ? inspectionError : inspection?.Outputs.Error ?? "";
    public bool HasError => Error.Length > 0;
    public bool CanExecute => inspectionStatus.Length == 0 && parameters is not null && State != outputState.Invalid;
    public string[] Cells { get; private set; } = [];
    public bool HasSetupInputs => parameters?.SetupInputs.IsEmpty == false;
    public SetupQueryData SetupQueries { get; init; } = new() { Unavailable = parameters is null };
    public string InheritedSetupQueryValues { get; init; } = parameters is null ? "Unavailable" : "—";
    public string InheritedSetupQueryDetails { get; init; } = parameters is null ? "The parameter file could not be read." : "These results do not consume setup files.";
    public bool CanChangeSelection => !selectionLocked;
    public bool IsSelected { get => selected; set { if (CanChangeSelection && SetProperty(ref selected, value)) SelectionChanged?.Invoke(); } }
    internal void LockSelection(bool value)
    {
        if (selectionLocked == value) return;
        selectionLocked = value;
        OnPropertyChanged(nameof(CanChangeSelection));
    }
    internal event Action? SelectionChanged;
    internal void UpdateInspection(workItemOutputStatus? value, string error = "")
    {
        inspection = value; inspectionError = error; inspectionStatus = "";
        NotifyInspection();
    }
    internal void CancelInspection()
    {
        if (!IsChecking) return;
        inspectionStatus = "Not checked";
        NotifyInspection();
    }
    private void NotifyInspection()
    {
        foreach (var property in new[] { nameof(Status), nameof(State), nameof(IsChecking), nameof(OutputSummary), nameof(Error), nameof(HasError), nameof(CanExecute) }) OnPropertyChanged(property);
        NotifyGenerations();
    }
    internal void UpdateExecution(workItemExecutionProgress update)
    {
        if (update.State == workItemExecutionState.Running)
        {
            if (execution?.State != workItemExecutionState.Running)
            {
                generation = null;
                Interlocked.Exchange(ref attemptStartGeneration, parameters?.Sgd is null ? 0 : -1);
                Interlocked.Exchange(ref pendingGeneration, -1);
            }
            if (started is null) started = clock.GetTimestamp();
            elapsed = null;
        }
        else
        {
            RefreshGenerationProgress();
            // Validation failures have no running interval. Do not invent a duration.
            elapsed = started is { } timestamp ? clock.GetElapsedTime(timestamp) : null;
            started = null;
        }
        execution = update;
        foreach (var property in new[] { nameof(Status), nameof(State), nameof(OutputSummary), nameof(Error), nameof(HasError), nameof(CanExecute) }) OnPropertyChanged(property);
        RefreshElapsed();
        NotifyGenerations();
    }
    // Worker threads publish only a number; the UI timer coalesces notifications.
    internal void QueueGeneration(int value)
    {
        if (value < 0) return;
        Interlocked.CompareExchange(ref attemptStartGeneration, value, -1);
        Interlocked.Exchange(ref pendingGeneration, value);
    }
    internal void RefreshGenerationProgress()
    {
        var value = Interlocked.Exchange(ref pendingGeneration, -1);
        if (TotalGenerations is not { } total || value < 0) return;
        value = Math.Min(value, total);
        if (generation == value) return;
        generation = value;
        NotifyGenerations();
    }
    private void NotifyGenerations() { OnPropertyChanged(nameof(CompletedGenerations)); OnPropertyChanged(nameof(GenerationsText)); NotifyEstimatedRemaining(); }
    internal void RestoreElapsed(TimeSpan value) { elapsed = value; RefreshElapsed(); }
    internal void RefreshElapsed() { OnPropertyChanged(nameof(Elapsed)); OnPropertyChanged(nameof(ElapsedText)); NotifyEstimatedRemaining(); }
    private void NotifyEstimatedRemaining() { OnPropertyChanged(nameof(EstimatedRemaining)); OnPropertyChanged(nameof(EstimatedRemainingText)); }
    internal void SetColumns(IEnumerable<string> keys)
    {
        Cells = keys.Select(key => parameters?.Parameters.TryFind(key)?.Value.FormattedValue ?? "—").ToArray();
        OnPropertyChanged(nameof(Cells));
    }
}

/// Executes checked parameter files with a live pending queue; never runs setup.
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
    private ResultsOutputValidationCache validationCache = new();
    // Keep failed/cancelled attempts inspectable during this folder session; no tracking files.
    private readonly Dictionary<Guid, workItemExecutionProgress> outcomes = [];
    private readonly Dictionary<Guid, TimeSpan> elapsedTimes = [];
    private readonly Dictionary<Guid, int> generations = [];
    private readonly HashSet<WorkItemStatusRow> runningRows = [];
    private ResultsExecutionProgress executionProgress = new(0);
    private bool changingSelection;
    private HashSet<WorkItemStatusRow>? claimedRows;
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
        ValidateFullyCommand = new RelayCommand(async () => await ValidateFullyAsync(), () => !IsBusy && Folder.Length > 0);
        ExecuteCommand = new RelayCommand(async () => await ExecuteAsync(), () => CanExecute);
        ExportReportsCommand = new RelayCommand(async () => await ExportReportsAsync(), () => CanExportReports);
        SelectAllCommand = new RelayCommand(() => SelectRows(_ => true), () => CanEditChecks && Rows.Any(row => row.CanChangeSelection));
        SelectPendingCommand = new RelayCommand(() => SelectRows(row => row.State is outputState.Pending or outputState.Partial), () => CanEditChecks && Rows.Any(row => row.CanChangeSelection));
        ClearSelectionCommand = new RelayCommand(() => SelectRows(_ => false), () => CanEditChecks && Rows.Any(row => row.IsSelected && row.CanChangeSelection));
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
    public string SelectionSummary => $"{SelectedCount:N0} of {FileCount:N0} files checked."
        + (Rows.Any(row => row.IsSelected && !row.CanExecute) ? " Some checked files cannot run. Inspect Error details, then fix and refresh, or uncheck them." : "");
    public string CommandLine => string.Join(Environment.NewLine, Rows.Where(row => row.IsSelected && row.CanExecute)
        .Select(row => $"sortopia execute-results --parameters \"{row.Path}\""));
    public bool IsBusy => cancellation is not null;
    private bool CanEditChecks => !IsBusy || claimedRows is not null;
    public bool CanChooseRun => !IsBusy;
    public bool CanExecute => !IsBusy && ParallelJobsError.Length == 0 && SelectedCount > 0 && Rows.Where(row => row.IsSelected).All(row => row.CanExecute);
    public bool CanExportReports => !IsBusy && SelectedCount > 0 && Rows.Where(row => row.IsSelected)
        .All(row => row.State == outputState.Complete && row.Parameters is { } item && SorterEvalReportExport.isSupported(item));
    public runExecutionReport? LastReport { get; private set; }
    public ResultsExecutionProgress ExecutionProgress { get => executionProgress; private set => SetProperty(ref executionProgress, value); }
    public RelayCommand ReturnCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ValidateFullyCommand { get; }
    public RelayCommand ExecuteCommand { get; }
    public RelayCommand ExportReportsCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectPendingCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand CancelCommand { get; }

    public void SetReturnAction(Action action, bool toWorkspace)
    { goBack = action; ReturnLabel = toWorkspace ? "← Back to workspace" : "← Back to Sortopia"; }

    public async Task SetFolderAsync(string path, Guid? runId = null)
    {
        Cancel();
        if (!string.Equals(Folder, path, StringComparison.OrdinalIgnoreCase)) validationCache = new();
        outcomes.Clear();
        claimedRows = null;
        elapsedTimes.Clear(); generations.Clear(); runningRows.Clear();
        ExecutionProgress = new(0);
        foreach (var row in files) row.SelectionChanged -= Notify;
        files = []; Rows = []; Runs = []; ParameterColumns = [];
        selectedRun = null; OnPropertyChanged(nameof(SelectedRun));
        Folder = path; LastReport = null;
        await WorkAsync(token => LoadCoreAsync(runId, [], token));
    }
    public Task RefreshAsync() => WorkAsync(token => LoadCoreAsync(SelectedRun?.Id, SelectedPaths(), token));
    public Task ValidateFullyAsync() => WorkAsync(token => LoadCoreAsync(SelectedRun?.Id, SelectedPaths(), token, fullValidation: true));
    private string[] SelectedPaths() => Rows.Where(row => row.IsSelected).Select(row => row.Path).ToArray();
    private void SelectRows(Func<WorkItemStatusRow, bool> select)
    {
        changingSelection = true;
        try { foreach (var row in Rows) row.IsSelected = select(row) && (claimedRows is null || row.CanExecute); }
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
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        Status = string.Join(" · ", new[] { "Checking", "Not checked", "Pending", "Partial", "Running", "Complete", "Failed", "Cancelled", "Invalid" }
            .Select(state => (State: state, Count: Rows.Count(row => row.Status == state)))
            .Where(value => value.Count > 0 || value.State is "Pending" or "Complete")
            .Select(value => $"{value.Count:N0} {value.State.ToLowerInvariant()}"));
        if (SelectedRun?.Id is not null && Rows.Count == 0 && files.Length > 0)
            Status = $"No readable parameter files match this run. The folder contains {files.Length:N0} files; choose All runs to inspect them.";
        else if (SelectedRun?.Id is not null && files.Any(row => row.Parameters is null))
            Status += ". Unreadable files cannot be assigned to a run; choose All runs to inspect them.";
        Notify();
    }
    private async Task LoadCoreAsync(Guid? runId, string[] selectedPaths, CancellationToken token, bool fullValidation = false)
    {
        var path = Folder;
        var selected = selectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Status = "Reading parameter files…";
        var loaded = await Task.Run(() =>
        {
            if (path.Length == 0 || !Directory.Exists(path)) return [];
            var setupQueries = new SavedSetupQueries(RunParameterFiles.projectFolder(path));
            return Directory.GetFiles(path, "*.msgpack").Order(StringComparer.Ordinal).Select(file =>
            {
                token.ThrowIfCancellationRequested();
                runParameters? parameters = null;
                var error = "";
                var setup = (Values: "Unavailable", Details: "The parameter file could not be read.", Queries: new SetupQueryData { Unavailable = true });
                try
                {
                    parameters = RunParameterFiles.loadFile(file);
                    setup = setupQueries.Read(parameters, token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { error = e.GetBaseException().Message; }
                return new WorkItemStatusRow(file, parameters, null, error, clock, checking: parameters is not null && error.Length == 0)
                    { InheritedSetupQueryValues = setup.Values, InheritedSetupQueryDetails = setup.Details, SetupQueries = setup.Queries };
            }).ToArray();
        }, token);
        token.ThrowIfCancellationRequested();
        // A file removed after selection still needs a row for its execution error.
        var loadedPaths = loaded.Select(row => row.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        loaded = [.. loaded, .. files.Where(row => !loadedPaths.Contains(row.Path) && row.Parameters is not null
                && outcomes.TryGetValue(row.Parameters.Id, out var outcome) && outcome.State == workItemExecutionState.Failed)
            .Select(row => new WorkItemStatusRow(row.Path, row.Parameters, null, "The parameter file no longer exists.", clock)
                { InheritedSetupQueryValues = row.InheritedSetupQueryValues, SetupQueries = row.SetupQueries,
                  InheritedSetupQueryDetails = "From the last successful read. " + row.InheritedSetupQueryDetails })];
        foreach (var row in loaded)
            if (Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(row.Path), out var id))
            {
                if (outcomes.TryGetValue(id, out var outcome)) row.UpdateExecution(outcome);
                if (elapsedTimes.TryGetValue(id, out var elapsed)) row.RestoreElapsed(elapsed);
                if (generations.TryGetValue(id, out var generation)) { row.QueueGeneration(generation); row.RefreshGenerationProgress(); }
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
        else
        {
            // Publish the small parameter files before validating potentially large
            // result payloads. An unfinished check must never look like an empty folder.
            var toCheck = loaded.Where(row => row.IsChecking).ToArray();
            var cache = validationCache;
            try
            {
                // Decoding these object graphs concurrently increases GC pressure
                // in the desktop process. Reuse content-verified checks instead.
                for (var index = 0; index < toCheck.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    Status = $"{files.Length:N0} parameter files found. {(fullValidation ? "Fully validating outputs" : "Checking file integrity")}: {index:N0} of {toCheck.Length:N0} files…";
                    var row = toCheck[index];
                    workItemOutputStatus? inspection = null;
                    var error = "";
                    try { inspection = await Task.Run(() => fullValidation ? cache.Inspect(row.Path, token).Inspection
                        : cache.InspectIntegrity(row.Path, token), token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { error = e.GetBaseException().Message; }
                    token.ThrowIfCancellationRequested();
                    row.UpdateInspection(inspection, error);
                    // Property notifications can navigate away or cancel the load.
                    token.ThrowIfCancellationRequested();
                    Notify();
                }
                token.ThrowIfCancellationRequested();
                UpdateStatus();
                Status += fullValidation ? " · Full validation finished." : " · File integrity checked; payloads are validated before use.";
            }
            finally
            {
                foreach (var row in toCheck) row.CancelInspection();
            }
        }
    }
    public Task ExecuteAsync() => CanExecute ? WorkAsync(async token =>
    {
        var paths = SelectedPaths(); var runId = SelectedRun?.Id;
        var options = new resultsExecutionOptions(Math.Min(int.Parse(ParallelJobs), Rows.Count));
        var executionRows = Rows.ToArray();
        var selectedRows = executionRows.Where(row => row.Parameters is not null).ToDictionary(row => row.Parameters!.Id);
        var claimed = new HashSet<WorkItemStatusRow>();
        claimedRows = claimed;
        foreach (var row in executionRows) row.LockSelection(!row.CanExecute);
        ExecutionProgress = new(paths.Length);
        var batch = ExecutionProgress;
        Status = $"Executing {paths.Length:N0} checked files…";
        Notify();
        var context = SynchronizationContext.Current;
        var callbackGate = new object();
        var queue = new ExecutionQueue(context, callbackGate, () =>
        {
            if (cancellation?.Token != token || token.IsCancellationRequested) return null;
            var row = executionRows.FirstOrDefault(row => row.IsSelected && row.CanExecute && !claimed.Contains(row));
            if (row is null) return null;
            claimed.Add(row);
            row.LockSelection(true);
            Notify();
            return row.Path;
        });
        var acceptingProgress = true;
        var progress = new Progress<runExecutionProgress>(value =>
        { if (acceptingProgress && !token.IsCancellationRequested) Status = $"{value.Message} ({batch.Finished}/{batch.Total})"; });
        var rowProgress = new RowProgress(context, update =>
        {
            if (cancellation?.Token != token || !selectedRows.TryGetValue(update.Id, out var row)) return;
            claimed.Add(row);
            row.LockSelection(true);
            batch.SetTotal(executionRows.Count(row => row.IsSelected || claimed.Contains(row)));
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
            if (update.State != workItemExecutionState.Running && row.CompletedGenerations is { } generation) generations[update.Id] = generation;
            else generations.Remove(update.Id);
            Notify();
        }, callbackGate);
        runExecutionReport report;
        var generationProgress = new GenerationProgress(selectedRows);
        try { report = await Task.Run(() => RunParameterExecution.executeQueuedFilesWithProgress(paths, queue.TakeNext, options, progress, rowProgress, generationProgress, token), token); }
        finally
        {
            acceptingProgress = false;
            if (ReferenceEquals(claimedRows, claimed)) claimedRows = null;
            foreach (var row in executionRows) row.LockSelection(false);
        }
        if (cancellation?.Token != token) return;
        LastReport = report;
        if (report.Status == runExecutionStatus.Cancelled || token.IsCancellationRequested)
        {
            Error = report.Error;
            Status = "Cancelled. Completed outputs are retained; refresh to check any partial outputs.";
            return;
        }
        await LoadCoreAsync(runId, SelectedPaths(), token);
        if (report.Status != runExecutionStatus.Completed)
        { Error = report.Error; Status = $"{report.Status}: {report.Results.Length:N0} selected files processed. {Status}"; }
    }) : Task.CompletedTask;
    public Task ExportReportsAsync(string? outputFolder = null) => CanExportReports ? WorkAsync(async token =>
    {
        var paths = SelectedPaths();
        Status = $"Exporting reports for {paths.Length:N0} selected files…";
        var destination = outputFolder ?? SorterEvalReportExport.nextOutputFolder(Folder);
        var exported = await Task.Run(() => SorterEvalReportExport.exportFiles(paths, destination, token), token);
        if (cancellation?.Token == token)
        {
            var reports = new List<string>();
            if (exported.SummaryPath is not null)
                reports.Add($"{exported.SummaryRows:N0} summary rows and {exported.DetailRows:N0} detail rows");
            if (exported.EvolutionSummaryPath is not null)
                reports.Add($"{exported.EvolutionSummaryRows:N0} evolution summary rows");
            Status = $"Exported {string.Join("; ", reports)} to {exported.Folder}";
        }
    }) : Task.CompletedTask;
    internal void RefreshElapsedTimes()
    {
        foreach (var row in runningRows.ToArray()) { row.RefreshElapsed(); row.RefreshGenerationProgress(); }
    }
    private sealed class GenerationProgress(IReadOnlyDictionary<Guid, WorkItemStatusRow> rows) : IProgress<workItemGenerationProgress>
    {
        public void Report(workItemGenerationProgress value)
        {
            if (rows.TryGetValue(value.Id, out var row)) row.QueueGeneration(value.Generation);
        }
    }
    private sealed class ExecutionQueue(SynchronizationContext? context, object sync, Func<string?> takeNext)
    {
        public string? TakeNext()
        {
            if (context is null) { lock (sync) return takeNext(); }
            if (ReferenceEquals(context, SynchronizationContext.Current)) return takeNext();
            string? path = null;
            context.Send(_ => path = takeNext(), null);
            return path;
        }
    }
    private sealed class RowProgress(SynchronizationContext? context, Action<workItemExecutionProgress> receive, object sync) : IProgress<workItemExecutionProgress>
    {
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
        if (claimedRows is { } claimed) ExecutionProgress.SetTotal(Rows.Count(row => row.IsSelected || claimed.Contains(row)));
        foreach (var property in new[] { nameof(IsBusy), nameof(CanChooseRun), nameof(CanExecute), nameof(CanExportReports), nameof(FileCount), nameof(SelectedCount), nameof(SelectionSummary), nameof(CommandLine) }) OnPropertyChanged(property);
        foreach (var command in new[] { BrowseCommand, RefreshCommand, ValidateFullyCommand, ExecuteCommand, ExportReportsCommand, SelectAllCommand, SelectPendingCommand, ClearSelectionCommand, CancelCommand }) command?.RaiseCanExecuteChanged();
    }
    public void Cancel() => cancellation?.Cancel();
    public void Dispose() { Cancel(); foreach (var row in files) row.SelectionChanged -= Notify; }
}
