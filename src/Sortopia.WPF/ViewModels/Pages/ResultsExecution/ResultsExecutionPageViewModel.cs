using System.IO;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Shared;

namespace Sortopia.WPF.ViewModels.Pages.ResultsExecution;

public sealed record RunFileChoice(Guid? Id, string Label);

public sealed class WorkItemStatusRow(string path, runParameters? parameters, workItemOutputStatus? initialInspection, string error = "", bool checking = false) : ViewModelBase, ISetupQueryRow
{
    private bool selected;
    private workItemOutputStatus? inspection = initialInspection;
    private string inspectionError = error;
    private string inspectionStatus = checking ? "Checking" : "";
    private int? generation;
    public int? TotalGenerations => GenerationTotal(parameters);
    public int? CompletedGenerations => generation;
    public string GenerationsText => TotalGenerations is { } total ? $"{(CompletedGenerations is { } completed ? completed.ToString("N0") : "—")} / {total:N0}" : "—";
    private static int? GenerationTotal(runParameters? item) => item?.Sgd is { } controls ? Sortopia.Runs.SgdRun.target(controls.Value) : null;
    public string Path => path;
    public string FileName => System.IO.Path.GetFileName(path);
    public runParameters? Parameters => parameters;
    public Guid? RunId => parameters?.RunId;
    public string FunctionName => parameters?.Results.FunctionName ?? "Unreadable";
    public outputState State => generation is not null && inspection?.Outputs.State == outputState.Pending ? outputState.Partial : inspection?.Outputs.State ?? outputState.Invalid;
    public string Status => inspectionStatus.Length > 0 ? inspectionStatus : State.ToString();
    public bool IsChecking => inspectionStatus == "Checking";
    public string OutputSummary => inspectionStatus.Length > 0 ? (IsChecking ? "Checking outputs…" : "Outputs not checked") : inspection is null ? "" : $"{(State == outputState.Complete ? inspection.Outputs.ExpectedCount : inspection.Outputs.Available.Length)}/{inspection.Outputs.ExpectedCount} outputs"
        + (parameters?.Reporting is null ? "" : State == outputState.Complete ? " · evolution reports complete" : " · evolution reports required");
    public string Error => inspectionError.Length > 0 ? inspectionError : inspection?.Outputs.Error ?? "";
    public bool HasError => Error.Length > 0;
    public bool CanExecute => inspectionStatus.Length == 0 && parameters is not null && State != outputState.Invalid;
    public string[] Cells { get; private set; } = [];
    public bool HasSetupInputs => parameters?.SetupInputs.IsEmpty == false;
    public SetupQueryData SetupQueries { get; init; } = new() { Unavailable = parameters is null };
    public string InheritedSetupQueryValues { get; init; } = parameters is null ? "Unavailable" : "—";
    public string InheritedSetupQueryDetails { get; init; } = parameters is null ? "The parameter file could not be read." : "These results do not consume setup files.";
    public bool IsSelected { get => selected; set { if (SetProperty(ref selected, value)) SelectionChanged?.Invoke(); } }
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
        foreach (var property in new[] { nameof(Status), nameof(State), nameof(IsChecking), nameof(OutputSummary), nameof(Error), nameof(HasError), nameof(CanExecute), nameof(CompletedGenerations), nameof(GenerationsText) }) OnPropertyChanged(property);
    }
    internal void SetSnapshotGeneration(int? value)
    {
        if (generation == value) return;
        generation = value;
        NotifyInspection();
    }
    internal void SetSnapshotControls(runParameters saved)
    {
        parameters = saved;
        OnPropertyChanged(nameof(Parameters)); OnPropertyChanged(nameof(TotalGenerations)); OnPropertyChanged(nameof(GenerationsText));
    }
    internal void SetColumns(IEnumerable<string> keys)
    {
        Cells = keys.Select(key => parameters?.Parameters.TryFind(key)?.Value.FormattedValue ?? "—").ToArray();
        OnPropertyChanged(nameof(Cells));
    }
}

/// Inspects saved parameter files and launches results execution in separate terminals.
public sealed partial class ResultsExecutionPageViewModel : ViewModelBase, IDisposable
{
    private readonly Func<string?, string?> pickFolder;
    private readonly Action<string, string> launchTerminal;
    private readonly ResultsLaunchSettings? launchSettings;
    private string cpuPercent = "50", memoryLimit = "50%", parallelFiles = "Auto";
    private readonly string commandExecutable = ResultsCommandTerminal.FindExecutable(AppContext.BaseDirectory);
    private Action goBack;
    private CancellationTokenSource? cancellation;
    private string folder = "", status = "Choose a project's runParameters folder.", error = "", returnLabel = "← Back to Sortopia";
    private RunFileChoice? selectedRun;
    private IReadOnlyList<RunFileChoice> runs = [];
    private IReadOnlyList<WorkItemStatusRow> rows = [];
    private string[] parameterColumns = [];
    private WorkItemStatusRow[] files = [];
    private ResultsOutputValidationCache validationCache = new();
    private bool changingSelection;
    private WorkItemStatusRow? selectedFile;
    private readonly Func<ProducingRun, Task>? openRun;
    private readonly Func<string, string, Task>? inspectHistory;

    public ResultsExecutionPageViewModel(Action goBack, Func<string?, string?> pickFolder, Action<string, string>? launchTerminal = null, ResultsLaunchSettings? launchSettings = null, Func<ProducingRun, Task>? openRun = null, Func<string, string, Task>? inspectHistory = null)
    {
        this.inspectHistory = inspectHistory;
        OpenEvolutionHistoryCommand = new RelayCommand(async () => await OpenEvolutionHistoryAsync(), () => CanOpenEvolutionHistory);
        this.openRun = openRun;
        OpenRunCommand = new RelayCommand(async value =>
        {
            if ((value as WorkItemStatusRow ?? SelectedFile)?.Parameters is not { } item || this.openRun is null) return;
            try
            {
                var target = ProducingRun.Find(RunParameterFiles.projectFolder(Folder), item.ProjectId, item.RunId)
                    ?? throw new InvalidOperationException("The producing run or its workspace definition is no longer available.");
                await this.openRun(target);
            }
            catch (Exception e) { Error = $"Cannot open the producing run. {e.GetBaseException().Message}"; }
        }, () => SelectedFile?.Parameters is not null && this.openRun is not null && !IsBusy);
        this.goBack = goBack; this.pickFolder = pickFolder;
        this.launchTerminal = launchTerminal ?? ResultsCommandTerminal.Launch;
        this.launchSettings = launchSettings;
        try
        {
            var preferences = launchSettings?.Load() ?? ResultsLaunchPreferences.Default;
            cpuPercent = preferences.CpuPercent; memoryLimit = preferences.MemoryLimit; parallelFiles = preferences.ParallelFiles;
        }
        catch (Exception e) { error = $"Could not read remembered launch limits; using defaults. {e.GetBaseException().Message}"; }
        ReturnCommand = new RelayCommand(() => { Cancel(); this.goBack(); });
        BrowseCommand = new RelayCommand(async () =>
        {
            try { if (this.pickFolder(Folder) is { } path) await SetFolderAsync(path); }
            catch (Exception e) { Error = e.GetBaseException().Message; }
        }, () => !IsBusy);
        RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy && Folder.Length > 0);
        ValidateFullyCommand = new RelayCommand(async () => await ValidateFullyAsync(), () => !IsBusy && Folder.Length > 0);
        ExecuteCommandLineCommand = new RelayCommand(ExecuteCommandLine, () => CanExecuteCommandLine);
        UpdateProgressCommand = new RelayCommand(async () => await UpdateProgressAsync(), () => !IsBusy && Folder.Length > 0);
        ExportReportsCommand = new RelayCommand(async () => await ExportReportsAsync(), () => CanExportReports);
        SelectAllCommand = new RelayCommand(() => SelectRows(_ => true), () => !IsBusy && Rows.Count > 0);
        SelectPendingCommand = new RelayCommand(() => SelectRows(row => row.State is outputState.Pending or outputState.Partial), () => !IsBusy && Rows.Count > 0);
        ClearSelectionCommand = new RelayCommand(() => SelectRows(_ => false), () => !IsBusy && SelectedCount > 0);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }
    public string Folder { get => folder; private set => SetProperty(ref folder, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string ReturnLabel { get => returnLabel; private set => SetProperty(ref returnLabel, value); }
    public string CpuPercent { get => cpuPercent; set { if (SetProperty(ref cpuPercent, value)) LaunchLimitsChanged(); } }
    public string MemoryLimit { get => memoryLimit; set { if (SetProperty(ref memoryLimit, value)) LaunchLimitsChanged(); } }
    public string ParallelFiles { get => parallelFiles; set { if (SetProperty(ref parallelFiles, value)) LaunchLimitsChanged(); } }
    private ResultsLaunchPreferences LaunchPreferences => new(CpuPercent, MemoryLimit, ParallelFiles);
    public string LaunchLimitsError
    {
        get
        {
            try { LaunchPreferences.Validate(); return ""; }
            catch (Exception e) { return e.Message.Split(Environment.NewLine)[0]; }
        }
    }
    private void LaunchLimitsChanged()
    {
        OnPropertyChanged(nameof(LaunchLimitsError));
        if (LaunchLimitsError.Length == 0)
        {
            try { launchSettings?.Save(LaunchPreferences); }
            catch (Exception e) { Error = $"Could not remember launch limits: {e.GetBaseException().Message}"; }
        }
        Notify();
    }
    public IReadOnlyList<RunFileChoice> Runs { get => runs; private set => SetProperty(ref runs, value); }
    public IReadOnlyList<WorkItemStatusRow> Rows { get => rows; private set => SetProperty(ref rows, value); }
    public WorkItemStatusRow? SelectedFile
    {
        get => selectedFile;
        set
        {
            if (SetProperty(ref selectedFile, value))
            {
                OpenRunCommand.RaiseCanExecuteChanged();
                OpenEvolutionHistoryCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public RelayCommand OpenRunCommand { get; }
    public RelayCommand OpenEvolutionHistoryCommand { get; }
    private bool CanOpenEvolutionHistory => !IsBusy && inspectHistory is not null
        && SelectedFile?.Parameters?.Sgd is not null && Rows.Contains(SelectedFile);
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
    public string CommandLine => LaunchLimitsError.Length == 0
        ? ResultsCommandTerminal.FormatBatchCommand(commandExecutable, Rows.Where(row => row.IsSelected && row.CanExecute).Select(row => row.Path), LaunchPreferences) : "";
    public bool IsBusy => cancellation is not null;
    public bool CanChooseRun => !IsBusy;
    public bool CanExecuteCommandLine => !IsBusy && LaunchLimitsError.Length == 0 && SelectedCount > 0 && Rows.Where(row => row.IsSelected).All(row => row.CanExecute);
    public bool CanExportReports => !IsBusy && SelectedCount > 0 && Rows.Where(row => row.IsSelected)
        .All(row => row.State == outputState.Complete && row.Parameters is { } item && SorterEvalReportExport.isSupported(item));
    public RelayCommand ReturnCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ValidateFullyCommand { get; }
    public RelayCommand ExecuteCommandLineCommand { get; }
    public RelayCommand UpdateProgressCommand { get; }
    public bool HasSnapshotProgress => Rows.Any(row => row.TotalGenerations is not null);
    public string SnapshotProgressSummary => $"Saved snapshot progress: {Rows.Sum(row => (long)(row.CompletedGenerations ?? 0)):N0} / {Rows.Sum(row => (long)(row.TotalGenerations ?? 0)):N0} generations · {Rows.Count(row => row.TotalGenerations is not null):N0} evolution files";
    public double SnapshotProgressPercent
    {
        get
        {
            var total = Rows.Sum(row => (long)(row.TotalGenerations ?? 0));
            return total > 0 ? 100.0 * Rows.Sum(row => (long)(row.CompletedGenerations ?? 0)) / total
                : HasSnapshotProgress && Rows.Where(row => row.TotalGenerations is not null).All(row => row.CompletedGenerations == 0) ? 100 : 0;
        }
    }
    public RelayCommand ExportReportsCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectPendingCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand CancelCommand { get; }

    private void ExecuteCommandLine()
    {
        try
        {
            Error = "";
            launchTerminal(CommandLine, Folder);
            Status = "Opened one batch in a separate terminal with the selected CPU and memory limits. Cancel execution there. Saved generations update as snapshots are published.";
        }
        catch (Exception e) { Error = $"Could not open the execution terminal: {e.GetBaseException().Message}"; }
    }

    public void SetReturnAction(Action action, bool toWorkspace)
    { goBack = action; ReturnLabel = toWorkspace ? "← Back to workspace" : "← Back to Sortopia"; }

    public async Task SetFolderAsync(string path, Guid? runId = null)
    {
        ResetSnapshotWatcher();
        Cancel();
        SelectedFile = null;
        if (!string.Equals(Folder, path, StringComparison.OrdinalIgnoreCase)) validationCache = new();
        foreach (var row in files) row.SelectionChanged -= Notify;
        files = []; Rows = []; Runs = []; ParameterColumns = [];
        selectedRun = null; OnPropertyChanged(nameof(SelectedRun));
        Folder = path;
        await WorkAsync(token => LoadCoreAsync(runId, [], token));
        RebuildSnapshotWatcher();
    }
    public async Task RefreshAsync()
    {
        await WorkAsync(token => LoadCoreAsync(SelectedRun?.Id, SelectedPaths(), token));
        RebuildSnapshotWatcher();
    }
    public Task OpenEvolutionHistoryAsync()
    {
        if (!CanOpenEvolutionHistory) return Task.CompletedTask;
        var path = SelectedFile!.Path;
        var project = RunParameterFiles.projectFolder(Folder);
        return WorkAsync(async token =>
        {
            Status = "Checking for saved evolution history…";
            var summary = await Task.Run(() => RunOutputFiles.latestSummaryPath(project, RunParameterFiles.loadFile(path), token), token);
            token.ThrowIfCancellationRequested();
            if (summary is null)
            {
                Status = "No SorterPoolSummarySet has been saved for the selected file yet.";
                return;
            }
            await inspectHistory!(project, path);
            Status = "Opened the saved evolution history in the File inspector.";
        });
    }
    public Task UpdateProgressAsync() => !IsBusy ? WorkAsync(token => ReadProgressAsync(Rows.ToArray(), token)) : Task.CompletedTask;
    private async Task ReadProgressAsync(IEnumerable<WorkItemStatusRow> selectedRows, CancellationToken token)
    {
        var project = RunParameterFiles.projectFolder(Folder);
        var cache = validationCache;
        foreach (var row in selectedRows.Where(row => row.Parameters is not null))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var previousTarget = row.TotalGenerations;
                var recheckOutputs = row.State is outputState.Complete or outputState.Invalid;
                var update = await Task.Run(() =>
                {
                    var item = RunParameterFiles.loadFile(row.Path);
                    var generation = RunOutputFiles.completedGeneration(project, item, token);
                    // Intermediate progress needs only snapshot headers. Verify outputs when a file may be complete.
                    var inspection = item.Sgd is null || recheckOutputs || previousTarget != Sortopia.Runs.SgdRun.target(item.Sgd.Value)
                        || generation?.Value == Sortopia.Runs.SgdRun.target(item.Sgd.Value)
                        ? cache.InspectIntegrity(row.Path, token) : null;
                    return (Item: item, Generation: generation is null ? (int?)null : generation.Value, Inspection: inspection);
                }, token);
                token.ThrowIfCancellationRequested();
                row.SetSnapshotControls(update.Item);
                if (update.Inspection is not null) row.UpdateInspection(update.Inspection);
                row.SetSnapshotGeneration(update.Generation);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { row.UpdateInspection(null, e.GetBaseException().Message); }
        }
        UpdateStatus();
        Status += " · Snapshot progress updated.";
    }
    public async Task ValidateFullyAsync()
    {
        await WorkAsync(token => LoadCoreAsync(SelectedRun?.Id, SelectedPaths(), token, fullValidation: true));
        RebuildSnapshotWatcher();
    }
    private string[] SelectedPaths() => Rows.Where(row => row.IsSelected).Select(row => row.Path).ToArray();
    private void SelectRows(Func<WorkItemStatusRow, bool> select)
    {
        changingSelection = true;
        try { foreach (var row in Rows) row.IsSelected = select(row); }
        finally { changingSelection = false; Notify(); }
    }
    private void ApplyFilter()
    {
        Rows = files.Where(row => SelectedRun?.Id is not { } id || row.RunId == id).ToArray();
        if (SelectedFile is not null && !Rows.Contains(SelectedFile)) SelectedFile = null;
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
        Status = string.Join(" · ", new[] { "Checking", "Not checked", "Pending", "Partial", "Complete", "Invalid" }
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
                return new WorkItemStatusRow(file, parameters, null, error, checking: parameters is not null && error.Length == 0)
                    { InheritedSetupQueryValues = setup.Values, InheritedSetupQueryDetails = setup.Details, SetupQueries = setup.Queries };
            }).ToArray();
        }, token);
        token.ThrowIfCancellationRequested();
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
                    int? savedGeneration = null;
                    var error = "";
                    try
                    {
                        (inspection, savedGeneration) = await Task.Run(() =>
                        {
                            var checkedOutputs = fullValidation ? cache.Inspect(row.Path, token).Inspection : cache.InspectIntegrity(row.Path, token);
                            var generation = RunOutputFiles.completedGeneration(RunParameterFiles.projectFolder(path), row.Parameters!, token);
                            return (checkedOutputs, generation is null ? (int?)null : generation.Value);
                        }, token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { error = e.GetBaseException().Message; }
                    token.ThrowIfCancellationRequested();
                    row.UpdateInspection(inspection, error);
                    row.SetSnapshotGeneration(savedGeneration);
                    // Property notifications can navigate away or cancel the load.
                    token.ThrowIfCancellationRequested();
                    Notify();
                }
                token.ThrowIfCancellationRequested();
                UpdateStatus();
                Status += fullValidation ? " · Full validation finished." : " · File integrity checked. Snapshot progress updated from disk.";
            }
            finally
            {
                foreach (var row in toCheck) row.CancelInspection();
            }
        }
    }
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
    private async Task WorkAsync(Func<CancellationToken, Task> action)
    {
        cancellation?.Cancel();
        using var request = new CancellationTokenSource();
        cancellation = request; Error = ""; Notify();
        try { await action(request.Token); }
        catch (OperationCanceledException) { if (ReferenceEquals(cancellation, request)) Status = "Inspection or export cancelled. Terminal execution is independent."; }
        catch (Exception e) { if (ReferenceEquals(cancellation, request)) { Error = e.GetBaseException().Message; Status = "The operation could not complete. Refresh after correcting the reported problem."; } }
        finally { if (ReferenceEquals(cancellation, request)) { cancellation = null; Notify(); ScheduleSnapshotRefresh(); } }
    }
    private void Notify()
    {
        if (changingSelection) return;
        OpenRunCommand?.RaiseCanExecuteChanged();
        OpenEvolutionHistoryCommand?.RaiseCanExecuteChanged();
        foreach (var property in new[] { nameof(IsBusy), nameof(CanChooseRun), nameof(CanExecuteCommandLine), nameof(CanExportReports), nameof(FileCount), nameof(SelectedCount), nameof(SelectionSummary), nameof(CommandLine), nameof(HasSnapshotProgress), nameof(SnapshotProgressSummary), nameof(SnapshotProgressPercent) }) OnPropertyChanged(property);
        foreach (var command in new[] { BrowseCommand, RefreshCommand, ValidateFullyCommand, ExecuteCommandLineCommand, UpdateProgressCommand, ExportReportsCommand, SelectAllCommand, SelectPendingCommand, ClearSelectionCommand, CancelCommand }) command?.RaiseCanExecuteChanged();
    }
    public void Cancel() => cancellation?.Cancel();
    public void Dispose() { StopSnapshotMonitoring(); Cancel(); foreach (var row in files) row.SelectionChanged -= Notify; }
}
