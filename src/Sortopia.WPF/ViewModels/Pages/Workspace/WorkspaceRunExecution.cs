using System.Diagnostics;
using System.IO;
using Sortopia.Functions;
using Sortopia.FileDb;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
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

    private (Guid Project, Guid Run)? savedScopeKey;
    private runWorkspace? savedRunScope;
    private string savedScopeError = "";
    private void ClearRunScope() { savedScopeKey = null; savedRunScope = null; savedScopeError = ""; }
    private WorkspaceTreeNode? SelectedRunNode => Selected?.IsRun == true
        ? Roots.SelectMany(root => root.DescendantsAndSelf()).FirstOrDefault(node => node.IsRun && node.Id == Selected.Id) : null;

    private runWorkspace? ResolveRunScope(WorkspaceTreeNode node)
    {
        var key = (node.Parent!.Id, node.Id);
        if (savedScopeKey == key) return savedRunScope;
        ClearRunScope(); savedScopeKey = key;
        try { savedRunScope = WorkspaceRunDocument.resolve(savedDocument!, key.Item1, key.Item2); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { savedScopeError = error.GetBaseException().Message; }
        return savedRunScope;
    }

    // A sealed project's settings are independent of later edits to its ancestors.
    // Only relevant locations and the selected run's own unsaved inputs matter here.
    private string ScopeLocationError(runWorkspace scope, WorkspaceTreeNode runNode)
    {
        var nodes = Roots.SelectMany(root => root.DescendantsAndSelf()).ToArray();
        foreach (var location in scope.ProjectLocations)
        {
            var project = nodes.FirstOrDefault(node => node.IsProject && node.Id == location.Snapshot.ProjectId);
            if (project is null) return $"Required project '{location.Snapshot.ProjectName}' has been removed. Restore it before generating this run.";
            for (var ancestor = project; ancestor is { IsWorkspace: false }; ancestor = ancestor.Parent)
                if (ancestor.FolderNameError.Length > 0) return $"Location of '{ancestor.Name}': {ancestor.FolderNameError}";
            var savedFolder = Path.Combine(WorkspaceFolder, location.RelativeFolder);
            if (!string.Equals(project.GetFolderPath(WorkspaceFolder), savedFolder, StringComparison.OrdinalIgnoreCase))
                return $"Save the location change for required project '{project.Name}' before generating this run.";
            var definition = Path.Combine(savedFolder, WorkspaceDefinitionFiles.Name(project.Id));
            if (!File.Exists(definition)) return $"Required project '{project.Name}' is missing its saved definition: {definition}. Restore it or save the workspace.";
        }
        var runFile = Path.Combine(WorkspaceData.scopedProjectFolder(scope, WorkspaceFolder, scope.Parent.Id), "runs", WorkspaceDefinitionFiles.Name(runNode.Id));
        return File.Exists(runFile) ? "" : $"This run's saved definition is missing: {runFile}. Restore it or save the workspace.";
    }

    public string GenerationHint
    {
        get
        {
            if (Selected?.IsRun != true) return "Select a run to generate its parameter files.";
            if (IsBusy) return "Wait for the current operation, or cancel it below.";
            if (savedDocument is null || !HasSavedWorkspace || SelectedRunNode is not { Parent: not null } node)
                return "Save this run and its project before generating parameter files.";
            if (node.NameError.Length > 0) return $"Run name: {node.NameError}";
            if (node.Editor!.ValidationError is { Length: > 0 } issue) return $"Run '{node.Name}': {issue}";
            if (ResolveRunScope(node) is not { } scope)
                return $"Saved run '{node.Name}': {savedScopeError} Save this run and its project after correcting these settings.";
            try
            {
                if (node.Parent.Name != (string)scope.Parent.ProjectName ||
                    !SettingsDto.toDomain(node.Parent.Family, node.Parent.Editor!.ToProjectSettings()).Equals(scope.Parent.Configuration))
                    return $"Save the changes to project '{node.Parent.Name}' and its inherited settings before generating run parameters.";
                var current = RunDto.toDomain(scope.Parent, node.Editor.ToRunInputs(node.Id, node.Name, node.Parent.Id));
                if (!WorkspaceCodec.encodeRun(current).SequenceEqual(WorkspaceCodec.encodeRun(scope.Run)))
                    return $"Save the changes to run '{node.Name}' before generating its parameter files.";
                return ScopeLocationError(scope, node);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
            { return $"Run '{node.Name}': {error.GetBaseException().Message}"; }
        }
    }
    private bool CanGenerateRunParameters => CanEdit && GenerationHint.Length == 0;
    public string GenerationInstructions => (Selected?.IsBranch == true
        ? "Generate run parameters runs setup locally and creates one file for each setup × results pairing. Open the Execute page to select saved files for results execution. "
        : "Generate run parameters creates one file for every results parameter set. Open the Execute page to select saved files for results execution. ")
        + "Generation fixes the parent project's settings before writing setup data or parameter files. Cancel operation stops at database operations and between function calls.";

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
