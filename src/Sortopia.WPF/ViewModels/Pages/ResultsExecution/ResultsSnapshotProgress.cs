using System.IO;
using Sortopia.FileDb;

namespace Sortopia.WPF.ViewModels.Pages.ResultsExecution;

public sealed partial class ResultsExecutionPageViewModel
{
    private FileSystemWatcher? snapshotWatcher;
    private Action<Action>? postSnapshotUpdate;
    private readonly Dictionary<string, List<WorkItemStatusRow>> snapshotRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<WorkItemStatusRow> pendingSnapshotRows = [];
    private int snapshotWatchVersion;
    private bool snapshotRefreshQueued;

    // The view supplies its dispatcher. File-system callbacks never touch bound state.
    public void StartSnapshotMonitoring(Action<Action> post)
    {
        if (postSnapshotUpdate is not null) return;
        postSnapshotUpdate = post;
        RebuildSnapshotWatcher();
    }

    public void StopSnapshotMonitoring()
    {
        ResetSnapshotWatcher();
        postSnapshotUpdate = null;
    }

    private void ResetSnapshotWatcher()
    {
        snapshotWatchVersion++;
        snapshotWatcher?.Dispose();
        snapshotWatcher = null;
        snapshotRows.Clear();
        pendingSnapshotRows.Clear();
        snapshotRefreshQueued = false;
    }

    private void RebuildSnapshotWatcher()
    {
        ResetSnapshotWatcher();
        var post = postSnapshotUpdate;
        if (post is null || Folder.Length == 0) return;
        try
        {
            var project = RunParameterFiles.projectFolder(Folder);
            if (!Directory.Exists(project)) return;
            foreach (var row in files.Where(row => row.Parameters?.Sgd is not null))
            {
                var item = row.Parameters!;
                var store = new SortopiaFileDb(project, item.ProjectId);
                var paths = RunOutputFiles.keys(item).Select(key => store.GetPath(key.Reference))
                    .Append(RunOutputFiles.temporarySnapshotPath(project, item));
                foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!snapshotRows.TryGetValue(path, out var owners)) snapshotRows.Add(path, owners = []);
                    owners.Add(row);
                }
            }
            var version = snapshotWatchVersion;
            void Published(string? path) => post(() =>
            {
                if (version != snapshotWatchVersion) return;
                if (path is null) pendingSnapshotRows.UnionWith(snapshotRows.Values.SelectMany(rows => rows));
                else if (snapshotRows.TryGetValue(path, out var owners)) pendingSnapshotRows.UnionWith(owners);
                ScheduleSnapshotRefresh();
            });
            // Watch the existing project root, including result folders created by a new run.
            snapshotWatcher = new FileSystemWatcher(project, "*.msgpack")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                InternalBufferSize = 32 * 1024
            };
            snapshotWatcher.Created += (_, e) => Published(e.FullPath);
            snapshotWatcher.Changed += (_, e) => Published(e.FullPath);
            snapshotWatcher.Deleted += (_, e) => Published(e.FullPath);
            snapshotWatcher.Renamed += (_, e) => { Published(e.OldFullPath); Published(e.FullPath); };
            // A dropped notification needs one catch-up read, never a polling loop.
            snapshotWatcher.Error += (_, _) => Published(null);
            snapshotWatcher.EnableRaisingEvents = true;
            // Close the gap between the initial disk inspection and subscribing.
            Published(null);
        }
        catch (Exception e)
        {
            ResetSnapshotWatcher();
            Error = $"Automatic snapshot updates unavailable. Use Update progress. {e.GetBaseException().Message}";
        }
    }

    private void ScheduleSnapshotRefresh()
    {
        if (IsBusy || snapshotRefreshQueued || pendingSnapshotRows.Count == 0 || postSnapshotUpdate is null) return;
        snapshotRefreshQueued = true;
        var version = snapshotWatchVersion;
        postSnapshotUpdate(async () =>
        {
            if (version != snapshotWatchVersion) return;
            snapshotRefreshQueued = false;
            if (IsBusy) return; // WorkAsync drains notifications after the current operation.
            var changed = pendingSnapshotRows.ToArray();
            pendingSnapshotRows.Clear();
            await WorkAsync(token => ReadProgressAsync(changed, token));
        });
    }
}
