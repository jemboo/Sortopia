using System.IO;
using Sortopia.WPF.Commands;
using Sortopia.Workspaces.Persistence;

namespace Sortopia.WPF.ViewModels.Pages.FileInspector;

public sealed class FileInspectorPageViewModel : ViewModelBase, IDisposable
{
    private Action goBack;
    private CancellationTokenSource? browseCancellation, inspectionCancellation;
    private string rootFolder = "", currentFolder = "", filter = "", error = "", status = "Choose a workspace, module, or project folder to inspect.";
    private string kind = "No file selected", description = "Select a file to see its saved contents. Unsaved editor changes are not shown.";
    private IReadOnlyList<InspectorFile> entries = [];
    private IReadOnlyList<InspectionNode> details = [], metadata = [];
    private IReadOnlyList<InspectorReference> references = [];
    private InspectorFile? selected;
    private int offset;
    private string appliedFilter = "";
    private bool hasMore;

    public FileInspectorPageViewModel(Action goBack, Func<string?, string?> pickFolder)
    {
        this.goBack = goBack;
        ReturnCommand = new RelayCommand(() => { Cancel(); this.goBack(); });
        BrowseCommand = new RelayCommand(async () =>
        {
            try { if (pickFolder(RootFolder) is { } folder) await OpenRootAsync(folder); }
            catch (Exception e) { Error = $"Could not choose a folder. {e.GetBaseException().Message}"; }
        });
        RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => CurrentFolder.Length > 0 && !IsBrowsing);
        UpCommand = new RelayCommand(async () => await OpenFolderAsync(Path.GetDirectoryName(CurrentFolder)!), () => CanGoUp && !IsBrowsing);
        RootCommand = new RelayCommand(async () => await OpenFolderAsync(RootFolder), () => CanGoUp && !IsBrowsing);
        OpenFolderCommand = new RelayCommand(async () => { if (Selected is { IsDirectory: true } item) await OpenFolderAsync(item.FullPath); }, () => Selected is { IsDirectory: true, Entry.IsLink: false } && !IsBrowsing);
        NextCommand = new RelayCommand(async () => await LoadFolderAsync(CurrentFolder, Filter == appliedFilter ? offset + WorkspaceFileInspection.pageSize : 0), () => HasMore && !IsBrowsing);
        PreviousCommand = new RelayCommand(async () => await LoadFolderAsync(CurrentFolder, Filter == appliedFilter ? Math.Max(0, offset - WorkspaceFileInspection.pageSize) : 0), () => offset > 0 && !IsBrowsing);
        FilterCommand = new RelayCommand(async () => await LoadFolderAsync(CurrentFolder, 0), () => CurrentFolder.Length > 0 && !IsBrowsing);
        OpenReferenceCommand = new RelayCommand(async value => { if (value is InspectorReference reference) await OpenReferenceAsync(reference); });
    }

    public string RootFolder { get => rootFolder; private set => SetProperty(ref rootFolder, value); }
    public string CurrentFolder { get => currentFolder; private set { SetProperty(ref currentFolder, value); OnPropertyChanged(nameof(CanGoUp)); } }
    public string Filter { get => filter; set => SetProperty(ref filter, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Kind { get => kind; private set => SetProperty(ref kind, value); }
    public string Description { get => description; private set => SetProperty(ref description, value); }
    public IReadOnlyList<InspectorFile> Entries { get => entries; private set => SetProperty(ref entries, value); }
    public IReadOnlyList<InspectionNode> Details { get => details; private set => SetProperty(ref details, value); }
    public IReadOnlyList<InspectionNode> Metadata { get => metadata; private set => SetProperty(ref metadata, value); }
    public IReadOnlyList<InspectorReference> References { get => references; private set { SetProperty(ref references, value); OnPropertyChanged(nameof(HasReferences)); } }
    public bool HasReferences => References.Count > 0;
    public bool IsBrowsing => browseCancellation is not null;
    public bool IsInspecting => inspectionCancellation is not null;
    public bool HasMore => hasMore;
    public bool CanGoUp => CurrentFolder.Length > 0 && !CurrentFolder.Equals(RootFolder, StringComparison.OrdinalIgnoreCase);
    public InspectorFile? Selected
    {
        get => selected;
        set { if (SetProperty(ref selected, value)) { NotifyCommands(); _ = InspectAsync(value); } }
    }
    public RelayCommand ReturnCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand UpCommand { get; }
    public RelayCommand RootCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand FilterCommand { get; }
    public RelayCommand OpenReferenceCommand { get; }

    public void SetReturnAction(Action action) => goBack = action;

    public async Task OpenRootAsync(string folder)
    {
        try
        {
            folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            WorkspaceFileInspection.checkedPath(folder, folder);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose a folder, not a file.");
            RootFolder = folder;
            Filter = "";
            await OpenFolderAsync(folder);
        }
        catch (Exception e) { Error = $"Cannot inspect '{folder}'. {e.GetBaseException().Message}"; }
    }

    public Task OpenFolderAsync(string folder) { Filter = ""; return LoadFolderAsync(folder, 0); }
    public Task RefreshAsync() => LoadFolderAsync(CurrentFolder, Filter == appliedFilter ? offset : 0, Selected?.FullPath);

    private async Task LoadFolderAsync(string folder, int start, string? selectPath = null)
    {
        browseCancellation?.Cancel();
        using var request = new CancellationTokenSource();
        browseCancellation = request;
        selected = null;
        OnPropertyChanged(nameof(Selected));
        await InspectAsync(null);
        CurrentFolder = folder;
        Entries = [];
        Error = "";
        Status = "Reading folder…";
        NotifyCommands();
        try
        {
            var root = RootFolder;
            var nameFilter = Filter;
            var listing = await Task.Run(() => WorkspaceFileInspection.listFolder(root, folder, start, nameFilter, request.Token), request.Token);
            if (request.IsCancellationRequested) return;
            CurrentFolder = folder;
            offset = start;
            appliedFilter = nameFilter;
            hasMore = listing.HasMore;
            Entries = listing.Entries.Select(entry => new InspectorFile(entry)).ToArray();
            Status = Entries.Count == 0 ? "No matching entries in this folder."
                : $"Entries {offset + 1:N0}–{offset + Entries.Count:N0}{(HasMore ? " · More available" : "")} · Saved files only";
            if (selectPath is not null)
            {
                var entry = Entries.FirstOrDefault(e => e.FullPath.Equals(selectPath, StringComparison.OrdinalIgnoreCase))
                    ?? new InspectorFile(WorkspaceFileInspection.entry(WorkspaceFileInspection.checkedPath(root, selectPath)));
                selected = entry;
                OnPropertyChanged(nameof(Selected));
                await InspectAsync(entry);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!request.IsCancellationRequested) { Entries = []; Status = "Folder could not be read."; Error = $"Cannot read '{folder}'. {e.GetBaseException().Message}"; }
        }
        finally
        {
            if (ReferenceEquals(browseCancellation, request)) { browseCancellation = null; NotifyCommands(); }
        }
    }

    public async Task InspectAsync(InspectorFile? file)
    {
        inspectionCancellation?.Cancel();
        using var request = new CancellationTokenSource();
        inspectionCancellation = request;
        OnPropertyChanged(nameof(IsInspecting));
        Details = []; References = []; Metadata = []; Error = "";
        Kind = file is null ? "No file selected" : "Reading file…";
        Description = file is null ? "Select a file to see its saved contents. Unsaved editor changes are not shown." : "Reading saved file metadata and configuration.";
        try
        {
            if (file is null) return;
            var root = RootFolder;
            var report = await Task.Run(() => InspectionReport.Read(root, file, request.Token), request.Token);
            if (request.IsCancellationRequested) return;
            var current = WorkspaceFileInspection.entry(file.FullPath);
            Metadata = [InspectionNode.Leaf("Name", current.Name), InspectionNode.Leaf("Path", current.FullPath),
                InspectionNode.Leaf("Size", current.IsDirectory ? "Folder" : $"{InspectorFile.FormatSize(current.Length)} ({current.Length:N0} bytes)"),
                InspectionNode.Leaf("Modified", current.ModifiedUtc.ToLocalTime().ToString("F"))];
            Kind = report.Kind; Description = report.Description; Details = report.Details; References = report.References;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!request.IsCancellationRequested)
            {
                Kind = "Could not inspect file";
                Description = "The file may be missing, locked, damaged, or use an unsupported format. No changes were made.";
                Error = $"Cannot inspect '{file?.FullPath}'. {e.GetBaseException().Message}";
                if (file is not null) Metadata = [InspectionNode.Leaf("Path", file.FullPath), InspectionNode.Leaf("Listed size", file.Size)];
            }
        }
        finally { if (ReferenceEquals(inspectionCancellation, request)) { inspectionCancellation = null; OnPropertyChanged(nameof(IsInspecting)); } }
    }

    public async Task OpenReferenceAsync(InspectorReference reference)
    {
        try
        {
            WorkspaceFileInspection.checkedPath(RootFolder, reference.FullPath);
            Filter = "";
            await LoadFolderAsync(Path.GetDirectoryName(reference.FullPath)!, 0, reference.FullPath);
        }
        catch (Exception e) { Error = $"Cannot open referenced file '{reference.FullPath}'. {e.GetBaseException().Message}"; }
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(IsBrowsing)); OnPropertyChanged(nameof(HasMore));
        foreach (var command in new[] { RefreshCommand, UpCommand, RootCommand, OpenFolderCommand, NextCommand, PreviousCommand, FilterCommand })
            command?.RaiseCanExecuteChanged();
    }
    public void Cancel() { browseCancellation?.Cancel(); inspectionCancellation?.Cancel(); }
    public void Dispose() => Cancel();
}
