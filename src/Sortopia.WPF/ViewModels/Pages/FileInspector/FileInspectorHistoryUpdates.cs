using System.IO;
using Sortopia.WPF.ViewModels.Shared.Controls;

namespace Sortopia.WPF.ViewModels.Pages.FileInspector;

internal sealed record SummaryHistorySource(string ProjectFolder, IReadOnlyList<string> Paths);

public sealed partial class FileInspectorPageViewModel
{
    private Action<Action>? postHistoryUpdate;
    private FileSystemWatcher? historyWatcher;
    private InspectorFile? historyFile;
    private SummaryHistorySource? historySource;
    private HashSet<string> historyPaths = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? historyRefreshCancellation;
    private int historyWatchVersion;
    private bool historyRefreshPending, historyRefreshQueued;

    // The view owns activation and supplies its dispatcher; file-system callbacks never touch bound state.
    public void StartHistoryMonitoring(Action<Action> post)
    {
        if (postHistoryUpdate is not null) return;
        postHistoryUpdate = post;
        RebuildHistoryWatcher();
    }

    public void StopHistoryMonitoring()
    {
        ResetHistoryWatcher();
        postHistoryUpdate = null;
        BinMovie?.Pause();
    }

    private void ResetHistoryWatcher()
    {
        historyWatchVersion++;
        historyWatcher?.Dispose();
        historyWatcher = null;
        historyRefreshCancellation?.Cancel();
        historyRefreshCancellation = null;
        historyRefreshPending = historyRefreshQueued = false;
    }

    private void ClearHistorySource()
    {
        ResetHistoryWatcher();
        historySource = null;
        historyFile = null;
        historyPaths.Clear();
    }

    private void SetHistorySource(InspectorFile file, SummaryHistorySource? source)
    {
        historyFile = source is null ? null : file;
        historySource = source;
        historyPaths = new(source?.Paths ?? [], StringComparer.OrdinalIgnoreCase);
        RebuildHistoryWatcher();
    }

    private void RebuildHistoryWatcher()
    {
        ResetHistoryWatcher();
        var post = postHistoryUpdate;
        if (post is null || historySource is null) return;
        try
        {
            var version = historyWatchVersion;
            void Published(string? path) => post(() =>
            {
                if (version != historyWatchVersion || path is not null && !historyPaths.Contains(path)) return;
                historyRefreshPending = true;
                ScheduleHistoryRefresh();
            });
            // The project already exists even when no summary folder or summary file has been published yet.
            historyWatcher = new FileSystemWatcher(historySource.ProjectFolder, "*.msgpack")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                InternalBufferSize = 32 * 1024
            };
            historyWatcher.Created += (_, e) => Published(e.FullPath);
            historyWatcher.Changed += (_, e) => Published(e.FullPath);
            historyWatcher.Deleted += (_, e) => Published(e.FullPath);
            historyWatcher.Renamed += (_, e) => { Published(e.OldFullPath); Published(e.FullPath); };
            historyWatcher.Error += (_, _) => Published(null); // One catch-up read after dropped notifications.
            historyWatcher.EnableRaisingEvents = true;
            Published(null); // Close the gap between the initial read and subscribing, or returning to the page.
        }
        catch (Exception e)
        {
            ResetHistoryWatcher();
            Error = $"{Error} Automatic history updates unavailable. Use Refresh. {e.GetBaseException().Message}".Trim();
        }
    }

    private void ScheduleHistoryRefresh()
    {
        if (!historyRefreshPending || historyRefreshQueued || historyRefreshCancellation is not null
            || IsBrowsing || IsInspecting || historyWatcher is null || postHistoryUpdate is null || historyFile is null) return;
        historyRefreshQueued = true;
        var version = historyWatchVersion;
        postHistoryUpdate(async () =>
        {
            if (version != historyWatchVersion) return;
            historyRefreshQueued = false;
            if (IsBrowsing || IsInspecting || historyRefreshCancellation is not null) return;
            historyRefreshPending = false;
            using var request = new CancellationTokenSource();
            historyRefreshCancellation = request;
            var file = historyFile!;
            var root = RootFolder;
            try
            {
                var report = await Task.Run(() => InspectionReport.Read(root, file, request.Token), request.Token);
                if (request.IsCancellationRequested || version != historyWatchVersion) return;
                if (report.HistorySource is { } source)
                {
                    // Run extensions can add planned summary paths. Re-read once after updating the filter
                    // so publications arriving during the previous read cannot fall through the gap.
                    if (!historyPaths.SetEquals(source.Paths)) historyRefreshPending = true;
                    historySource = source;
                    historyPaths = new(source.Paths, StringComparer.OrdinalIgnoreCase);
                }
                if (report.Error.Length > 0)
                {
                    Error = report.Error + (BinMovie is null ? "" : " Showing the previously loaded history.");
                    return;
                }
                if (report.PoolSummary is null) BinMovie = null;
                else if (BinMovie is { } movie) movie.UpdateSummary(report.PoolSummary);
                else BinMovie = new SorterBinMovieViewModel(report.PoolSummary);
                if (request.IsCancellationRequested || version != historyWatchVersion) return;
                Kind = report.Kind; Description = report.Description; Details = report.Details; References = report.References;
                Error = "";
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (!request.IsCancellationRequested && version == historyWatchVersion)
                    Error = $"Cannot refresh evolution history: {e.GetBaseException().Message}" + (BinMovie is null ? "" : " Showing the previously loaded history.");
            }
            finally
            {
                if (ReferenceEquals(historyRefreshCancellation, request))
                {
                    historyRefreshCancellation = null;
                    ScheduleHistoryRefresh();
                }
            }
        });
    }
}
