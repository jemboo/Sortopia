using System.IO;
using Sortopia.FileDb;
using Sortopia.Functions;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceRunParameterFile(string Path, Guid? RunId, string Parameters, string FileStatus, string ResultsStatus, string Details)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string SuffixStages { get; init; } = "—";
    public string SuffixStagesDetails { get; init; } = "No model-generation suffix length is recorded for this file.";
}

/// Reads saved files independently of the current draft and the last generation attempt.
public sealed class WorkspaceRunParameterFiles : ViewModelBase
{
    private CancellationTokenSource? refresh;
    private (string Folder, Guid Project, Guid Run)? context;
    public IReadOnlyList<WorkspaceRunParameterFile> Rows { get; private set; } = [];
    public bool IsRefreshing => refresh is not null;
    public bool HasFiles => Rows.Count > 0;
    public bool HasLoaded { get; private set; }
    public string Error { get; private set; } = "";
    internal void ForgetRemovedFiles() { Rows = []; HasLoaded = false; OnPropertyChanged(""); }
    public string Summary => IsRefreshing ? "Reading parameter files and checking results…"
        : !HasLoaded ? "Refresh to read this run's saved parameter files."
        : !HasFiles ? "No run parameter files found. Preview the combinations below, then generate files."
        : $"{Rows.Count:N0} files · {Rows.Count(row => row.FileStatus == "Ready"):N0} ready · {Rows.Count(row => row.ResultsStatus == "Complete"):N0} with complete results"
            + (Rows.Any(row => row.RunId is null) ? ". Unreadable files from the shared project folder are included; their run cannot be identified." : "");

    public async Task RefreshAsync(string folder, Guid projectId, Guid runId, IEnumerable<Guid>? generatedIds = null)
    {
        refresh?.Cancel();
        using var source = new CancellationTokenSource();
        refresh = source;
        var next = (folder, projectId, runId);
        if (context != next) { context = next; Rows = []; HasLoaded = false; }
        Error = "";
        var known = Rows.Where(row => row.RunId == runId).ToDictionary(row => row.Path, StringComparer.OrdinalIgnoreCase);
        if (folder.Length > 0)
            foreach (var id in generatedIds ?? [])
            {
                var path = System.IO.Path.Combine(folder, id + ".msgpack");
                known.TryAdd(path, new(path, runId, "Not read", "Missing", "Unknown", ""));
            }
        OnPropertyChanged("");
        try
        {
            var rows = await Task.Run(() => Read(folder, projectId, runId, known, source.Token), source.Token);
            source.Token.ThrowIfCancellationRequested();
            Rows = rows; HasLoaded = true;
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (ReferenceEquals(refresh, source))
                Error = $"Could not refresh run parameter files. {error.GetBaseException().Message} Displayed statuses may be out of date.";
        }
        finally
        {
            if (ReferenceEquals(refresh, source)) { refresh = null; OnPropertyChanged(""); }
        }
    }

    private static WorkspaceRunParameterFile[] Read(string folder, Guid projectId, Guid runId,
        Dictionary<string, WorkspaceRunParameterFile> known, CancellationToken token)
    {
        if (folder.Length == 0) return [];
        string[] files = [];
        try
        {
            File.GetAttributes(folder);
            RunParameterFiles.projectFolder(folder);
            files = Directory.GetFiles(folder, "*.msgpack");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        var paths = files.Concat(known.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var rows = new List<WorkspaceRunParameterFile>();
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            known.TryGetValue(path, out var previous);
            runParameters? parameters;
            try
            {
                // The decoder wraps IO errors. Check existence first so missing files
                // remain distinct from damaged files and inaccessible storage.
                File.GetAttributes(path);
                parameters = RunParameterFiles.loadFile(path);
            }
            catch (Exception error)
            {
                var missing = error is FileNotFoundException or DirectoryNotFoundException;
                rows.Add(new(path, previous?.RunId, previous?.Parameters ?? "Unreadable · run unknown",
                    missing ? "Missing" : "Invalid", "Unknown",
                    (previous is null ? "The run could not be identified. "
                        : previous.Parameters == "Not read" ? "Generation reported this file, but it could not be read. "
                        : "Parameters shown are from the last successful read. ")
                    + error.GetBaseException().Message)
                {
                    SuffixStages = previous?.SuffixStages ?? "—",
                    SuffixStagesDetails = previous is null ? "The file could not be read."
                        : "From the last successful read. " + previous.SuffixStagesDetails
                });
                continue;
            }
            if (parameters.RunId != runId) continue;
            var values = string.Join("; ", parameters.Parameters.ParamMap.Values.Select(binding => $"{binding.KeyName} = {binding.FormattedValue}"));
            if (values.Length == 0) values = "No parameter values";
            if (parameters.SetupSetNumber is { } setup) values = $"Setup set {setup.Value} · Results: {values}";
            if (parameters.ProjectId != projectId)
            {
                rows.Add(new(path, runId, values, "Invalid", "Unknown", "The parameter file belongs to a different project."));
                continue;
            }
            var (suffixStages, suffixDetails) = StageLengths(parameters);
            try
            {
                var outputs = RunParameterExecution.inspectFile(path, token).Outputs;
                rows.Add(new(path, runId, values, "Ready", outputs.State == outputState.Pending ? "Not generated" : outputs.State.ToString(),
                    outputs.Error.Length > 0 ? outputs.Error : $"{outputs.Available.Length}/{outputs.ExpectedCount} result outputs available.")
                    { SuffixStages = suffixStages, SuffixStagesDetails = suffixDetails });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                rows.Add(new(path, runId, values, "Ready", "Unknown", error.GetBaseException().Message)
                    { SuffixStages = suffixStages, SuffixStagesDetails = suffixDetails });
            }
        }
        return rows.ToArray();
    }

    private static (string Stages, string Details) StageLengths(runParameters item)
    {
        int? setup = item.SetupSuffixStages?.Value;
        int? results = item.ResultsSuffixStages?.Value;
        var basis = "Saved at parameter generation.";
        // Files written before sizing metadata was added still contain complete results inputs.
        if (results is null && ResultsFunctionCatalog.functions.TryGetValue(item.Results.FunctionName, out var definition)
            && definition.Version == item.Results.FunctionVersion)
        {
            try
            {
                results = ModelGenerationStageLengths.tryForDefinition(definition, item.Parameters)?.Value;
                if (results is not null) basis = "Results length calculated from this file's saved parameters.";
            }
            catch (ArgumentException error) { return ("Unavailable", $"Could not calculate the suffix length: {error.Message}"); }
        }
        var lengths = new List<string>();
        if (setup is not null) lengths.Add($"Setup: {setup:N0}");
        if (results is not null) lengths.Add($"Results: {results:N0}");
        var details = lengths.Count == 0 ? "No model-generation suffix length is recorded for this file."
            : $"Suggested suffix stages — {string.Join("; ", lengths)}. Fixed prefix stages are additional. {basis} These are provisional success targets.";
        if (item.SetupSetNumber is not null && setup is null)
            details += " No setup suffix length is recorded; it is not inferred from the current draft.";
        return (lengths.Count == 0 ? "—" : string.Join("\n", lengths), details);
    }
}
