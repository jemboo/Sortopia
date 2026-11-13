using System.Diagnostics;
using Sortopia.Functions;
using Sortopia.FileDb;
using Sortopia.Runs;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspacePageViewModel
{
    private readonly Dictionary<Guid, WorkspaceRunExecution> executions = [];

    public WorkspaceRunExecution? SelectedExecution
    {
        get
        {
            if (Selected?.IsRun != true) return null;
            if (!executions.TryGetValue(Selected.Id, out var state))
            {
                var id = Selected.Id;
                executions[id] = state = new WorkspaceRunExecution();
                state.PropertyChanged += (_, _) => { if (Selected?.Id == id) RefreshInspector(); };
            }
            return state;
        }
    }

    private run? SelectedSavedRun => Selected is { IsRun: true, Parent: not null }
        && savedDefinition is not null && savedDefinition.ProjectIndex.TryGetValue(Selected.Parent.Id, out var project)
        ? project.Runs.FirstOrDefault(value => value.Id == Selected.Id) : null;

    public string GenerationHint
    {
        get
        {
            if (Selected?.IsRun != true) return "Select a run to generate its parameter files.";
            if (IsBusy) return "Wait for the current operation, or cancel it below.";
            if (HasEditErrors) return "Correct the input errors and save the workspace before generating run parameters.";
            if (HasUnsavedChanges || SelectedSavedRun is null || !HasSavedWorkspace || HasMissingComponents)
                return "Save the workspace before generating parameters for this run.";
            var value = SelectedSavedRun!;
            if (!ResultsFunctionCatalog.functions.ContainsKey(value.ResultsFunctionName))
                return $"Results function '{value.ResultsFunctionName}' is not in the catalog.";
            if (value.SetupFunctionName is { } setup && !SetupFunctionCatalog.functions.ContainsKey(setup.Value))
                return $"Setup function '{setup.Value}' is not in the catalog.";
            return "";
        }
    }
    private bool CanGenerateRunParameters => CanEdit && GenerationHint.Length == 0;
    public string GenerationInstructions => (Selected?.IsBranch == true
        ? "Generate run parameters runs setup locally and creates one file for each setup × results pairing. Open the Execute page to select saved files for results execution. "
        : "Generate run parameters creates one file for every results parameter set. Open the Execute page to select saved files for results execution. ")
        + "Cancel operation stops at database operations and between function calls.";

    private sealed class ThrottledExecutionProgress(IProgress<runExecutionProgress> target) : IProgress<runExecutionProgress>
    {
        private readonly Stopwatch timer = Stopwatch.StartNew();
        private bool first = true;
        public void Report(runExecutionProgress value)
        {
            if (!first && timer.ElapsedMilliseconds < 100) return;
            first = false;
            timer.Restart();
            target.Report(value);
        }
    }
}

public sealed record WorkspaceExecutionRow(string Heading, string Parameters, string Outputs);

/// The last attempt for one run, retained only while this workspace is open.
public sealed class WorkspaceRunExecution : ViewModelBase
{
    public bool IsRunning { get; private set; }
    public bool HasAttempt { get; private set; }
    public bool HasReport => Report is not null;
    public runExecutionReport? Report { get; private set; }
    public string Summary { get; private set; } = "Run parameters have not been generated yet.";
    public string Detail { get; private set; } = "";
    public string Error => Report?.Error ?? "";
    public double Percent { get; private set; }
    public bool IsIndeterminate => IsRunning && total == 0;
    private long total;
    public IReadOnlyList<WorkspaceExecutionRow> Rows { get; private set; } = [];
    public string RowsHint { get; private set; } = "";

    internal void Begin(string summary = "Generating run parameters…")
    {
        HasAttempt = IsRunning = true;
        Report = null; Rows = []; RowsHint = ""; Percent = 0; total = 0;
        Summary = summary; Detail = "Generating parameter sets…";
        OnPropertyChanged("");
    }
    internal void Update(runExecutionProgress progress)
    {
        total = progress.Total;
        Percent = total == 0 ? 0 : 100d * progress.Completed / total;
        Detail = $"{progress.Message} · {progress.Completed:N0} / {total:N0} sets ready";
        OnPropertyChanged("");
    }
    internal void Finish(runExecutionReport report)
    {
        IsRunning = false; Report = report;
        var stored = report.Setup.Concat(report.Results).SelectMany(call => call.StoredOutputs).ToArray();
        Summary = $"{report.Status} · {report.Setup.Length:N0} setup, {report.Results.Length:N0} results sets ready"
            + $" · {stored.Count(output => !output.Reused):N0} saved, {stored.Count(output => output.Reused):N0} reused";
        Detail = $"Last attempt: {DateTime.Now:g}";
        if (report.Status == runExecutionStatus.Completed) Percent = 100;
        var count = (long)report.Setup.Length + report.Results.Length;
        Rows = report.Setup.Concat(report.Results).Take(100).Select(call => new WorkspaceExecutionRow(
            $"{call.Phase} set {call.SetNumber}" + (call.SetupSetNumber is { } setup ? $" · setup set {setup.Value}" : "") + $" · {call.FunctionName}",
            string.Join("; ", call.Parameters.ParamMap.Values.Select(binding => $"{binding.KeyName} = {binding.FormattedValue}")),
            call.StoredOutputs.IsEmpty ? "No saved output data" : string.Join("\n", call.StoredOutputs.Select(output =>
                $"{(output.Reused ? "Reused" : "Saved")} {output.Reference.DataType}: {output.Reference.Id}")))).ToArray();
        RowsHint = count > 100 ? $"Showing the first 100 of {count:N0} processed parameter sets." : $"{count:N0} processed parameter sets.";
        OnPropertyChanged("");
    }

    internal void FinishGeneration(runParameterGenerationReport report)
    {
        Finish(report.Execution);
        if (report.Execution.Status == runExecutionStatus.Completed)
        {
            Summary = $"Generated · {report.WorkItemIds.Length:N0} results parameter files ready. Results have not been executed; existing outputs can be reused.";
            OnPropertyChanged("");
        }
    }
}
