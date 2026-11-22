using System.IO;
using Sortopia.FileDb;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public enum WorkspaceExecutionFill { Empty, Half, Full }

internal sealed record WorkspaceExecutionCounts(int Files = 0, int Complete = 0, bool Unavailable = false)
{
    public static WorkspaceExecutionCounts operator +(WorkspaceExecutionCounts left, WorkspaceExecutionCounts right) =>
        new(left.Files + right.Files, left.Complete + right.Complete, left.Unavailable || right.Unavailable);
}

public sealed partial class WorkspaceTreeNode
{
    private WorkspaceExecutionCounts ownExecutionCounts = new(), executionCounts = new();
    public int RunParameterFileCount => executionCounts.Files;
    public int CompletedRunParameterFileCount => executionCounts.Complete;
    public WorkspaceExecutionFill ExecutionFill => executionCounts.Complete == 0 ? WorkspaceExecutionFill.Empty
        : executionCounts.Complete == executionCounts.Files && !executionCounts.Unavailable ? WorkspaceExecutionFill.Full : WorkspaceExecutionFill.Half;
    public string ExecutionSummary => executionCounts.Unavailable
        ? $"{executionCounts.Complete:N0} of {executionCounts.Files:N0} known run-parameter files completed; some files could not be checked."
        : executionCounts.Files == 0 ? "No run-parameter files."
        : $"{executionCounts.Complete:N0} of {executionCounts.Files:N0} run-parameter files completed ({(double)executionCounts.Complete / executionCounts.Files:P0}).";
    internal void SetExecutionCounts(WorkspaceExecutionCounts counts)
    {
        ownExecutionCounts = counts;
        RefreshExecutionCounts();
    }
    private void RefreshExecutionCounts()
    {
        var next = Children.Aggregate(ownExecutionCounts, (counts, child) => counts + child.executionCounts);
        if (next == executionCounts) return;
        executionCounts = next;
        OnPropertyChanged(nameof(RunParameterFileCount)); OnPropertyChanged(nameof(CompletedRunParameterFileCount));
        OnPropertyChanged(nameof(ExecutionFill)); OnPropertyChanged(nameof(ExecutionSummary)); OnPropertyChanged(nameof(IconDescription));
        Parent?.RefreshExecutionCounts();
    }
}

internal sealed record WorkspaceExecutionProject(Guid Id, string? Folder, Guid[] Runs);
internal sealed record WorkspaceExecutionProjectResult(WorkspaceExecutionCounts Unassigned, Dictionary<Guid, WorkspaceExecutionCounts> Runs);

/// <summary>Background display cache; execution still performs its own full validation.</summary>
internal sealed class WorkspaceExecutionProgressReader
{
    private sealed record Stamp(string Path, long Length, long Written, long Created);
    private sealed record Receipt(Guid RunId, Stamp[] Files, WorkspaceExecutionCounts Counts);
    private readonly Dictionary<string, Receipt> receipts = new(StringComparer.OrdinalIgnoreCase);

    private static Stamp[] Stamps(IEnumerable<string> paths) => paths.Select(path =>
    {
        var file = new FileInfo(path);
        return file.Exists ? new Stamp(path, file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks) : new(path, -1, 0, 0);
    }).ToArray();

    public Dictionary<Guid, WorkspaceExecutionProjectResult> Read(WorkspaceExecutionProject[] projects, CancellationToken token)
    {
        var results = new Dictionary<Guid, WorkspaceExecutionProjectResult>();
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            token.ThrowIfCancellationRequested();
            var runs = project.Runs.ToDictionary(id => id, _ => new WorkspaceExecutionCounts());
            var unassigned = new WorkspaceExecutionCounts();
            string[] files = [];
            try
            {
                if (project.Folder is { } folder)
                {
                    var parametersFolder = Path.Combine(folder, "runParameters");
                    try { if (Directory.Exists(parametersFolder)) files = Directory.GetFiles(parametersFolder, "*.msgpack"); }
                    catch (DirectoryNotFoundException) { }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                unassigned = new(Unavailable: true);
                foreach (var id in project.Runs) runs[id] = new(Unavailable: true);
            }
            foreach (var path in files)
            {
                token.ThrowIfCancellationRequested(); active.Add(path);
                receipts.TryGetValue(path, out var receipt);
                Guid? runId = receipt?.RunId;
                WorkspaceExecutionCounts counts;
                try
                {
                    var item = RunParameterFiles.loadFile(path);
                    if (item.ProjectId != project.Id) { runId = null; throw new InvalidDataException("Parameter file belongs to another project."); }
                    runId = item.RunId;
                    var store = new SortopiaFileDb(project.Folder!, project.Id);
                    var paths = RunOutputFiles.keys(item).Select(key => store.GetPath(key.Reference)).ToArray();
                    var before = Stamps(paths);
                    if (receipt is not null && receipt.RunId == item.RunId && before.SequenceEqual(receipt.Files)) counts = receipt.Counts;
                    else
                    {
                        // Stream/check envelopes and checksums without decoding sorter populations.
                        var inspection = RunParameterExecution.inspectFileIntegrity(path, token).Outputs;
                        counts = new(1, inspection.State == outputState.Complete ? 1 : 0, inspection.State == outputState.Invalid);
                        if (before.SequenceEqual(Stamps(paths))) receipts[path] = new(item.RunId, before, counts);
                        else receipts.Remove(path);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { counts = new(1, 0, true); }
                if (runId is { } id && runs.ContainsKey(id)) runs[id] += counts;
                else unassigned += counts;
            }
            results[project.Id] = new(unassigned, runs);
        }
        foreach (var path in receipts.Keys.Where(path => !active.Contains(path)).ToArray()) receipts.Remove(path);
        return results;
    }
}

public sealed partial class WorkspacePageViewModel
{
    private readonly WorkspaceExecutionProgressReader executionProgressReader = new();
    private Task? executionProgressRefresh;

    public Task RefreshExecutionProgressAsync(CancellationToken token = default)
    {
        if (IsBusy) return Task.CompletedTask;
        if (executionProgressRefresh is { IsCompleted: false }) return executionProgressRefresh;
        return executionProgressRefresh = ReadExecutionProgressAsync(token);
    }

    private async Task ReadExecutionProgressAsync(CancellationToken token)
    {
        var root = Roots.FirstOrDefault();
        if (root is null) return;
        var projects = root.DescendantsAndSelf().Where(node => node.IsProject).ToArray();
        var request = projects.Select(node => new WorkspaceExecutionProject(node.Id, node.SavedFolderPath ?? node.ImportSourcePath,
            node.Children.Where(child => child.IsRun).Select(child => child.Id).ToArray())).ToArray();
        try
        {
            var results = await Task.Run(() => executionProgressReader.Read(request, token), token);
            token.ThrowIfCancellationRequested();
            if (IsBusy || !ReferenceEquals(root, Roots.FirstOrDefault())) return;
            var current = root.DescendantsAndSelf().ToHashSet();
            foreach (var project in projects.Where(current.Contains))
            {
                var result = results[project.Id];
                project.SetExecutionCounts(result.Unassigned);
                foreach (var run in project.Children.Where(node => node.IsRun))
                    run.SetExecutionCounts(result.Runs.GetValueOrDefault(run.Id) ?? new());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
