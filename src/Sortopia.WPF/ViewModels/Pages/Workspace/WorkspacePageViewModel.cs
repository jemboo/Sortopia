using System.IO;
using System.Collections.ObjectModel;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;
using Sortopia.WPF.Settings;
using Sortopia.Workspaces;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public enum WorkspaceUnsavedChoice { Save, Discard, Cancel }
public sealed record WorkspaceFolderRequest(WorkspaceEntityKind Kind, string InitialDirectory, string ContainerFolder);

public sealed class WorkspacePageViewModel : ViewModelBase
{
    private readonly Action returnHome;
    private readonly Func<string?, string?> pickFolder;
    private readonly Func<WorkspaceUnsavedChoice> confirmUnsavedChanges;
    private readonly Func<WorkspaceFolderRequest, string?> pickExistingFolder;
    private readonly WorkspaceFolderSettings settings;
    private string workspaceFolder = "";
    private string status = "No Workspace folder selected.";
    private string error = "";
    private WorkspaceTreeNode? selected;
    private IReadOnlyList<string> retainedFolders = [];
    private workspace? savedDefinition;
    private bool hasUnsavedChanges;
    private bool resolvingNavigation;
    private CancellationTokenSource? cancellation;
    private enum WorkspaceFileState { NoFolder, Unavailable, Missing, Saved, Conflict }
    private WorkspaceFileState workspaceFileState;
    private string workspaceFileIssue = "";
    private int missingComponents;

    public WorkspacePageViewModel(Action returnHome, Func<string?, string?> pickFolder, WorkspaceFolderSettings settings,
        Func<WorkspaceUnsavedChoice>? confirmUnsavedChanges = null, Func<WorkspaceFolderRequest, string?>? pickExistingFolder = null)
    {
        this.returnHome = returnHome;
        this.pickFolder = pickFolder;
        this.confirmUnsavedChanges = confirmUnsavedChanges ?? (() => WorkspaceUnsavedChoice.Cancel);
        this.pickExistingFolder = pickExistingFolder ?? (_ => null);
        this.settings = settings;
        ReturnHomeCommand = new RelayCommand(async () => await ReturnHomeAsync(), () => !IsBusy && !resolvingNavigation);
        BrowseWorkspaceFolderCommand = new RelayCommand(async () => await BrowseWorkspaceFolderAsync(), () => !IsBusy && !resolvingNavigation);
        AddModuleCommand = new RelayCommand(() => Add(WorkspaceEntityKind.Module), () => CanAdd(WorkspaceEntityKind.Module));
        AddProjectCommand = new RelayCommand(() => Add(WorkspaceEntityKind.Project), () => CanAdd(WorkspaceEntityKind.Project));
        AddRunCommand = new RelayCommand(() => Add(WorkspaceEntityKind.Run), () => CanAdd(WorkspaceEntityKind.Run));
        AddExistingModuleCommand = new RelayCommand(async () => await AddExistingAsync(WorkspaceEntityKind.Module), () => CanAddExisting(WorkspaceEntityKind.Module));
        AddExistingProjectCommand = new RelayCommand(async () => await AddExistingAsync(WorkspaceEntityKind.Project), () => CanAddExisting(WorkspaceEntityKind.Project));
        RemoveCommand = new RelayCommand(Remove, () => CanEdit && Selected?.Parent is not null);
        SaveWorkspaceCommand = new RelayCommand(async () => await SaveWorkspaceAsync(), () => CanSaveWorkspace);
        DiscardChangesCommand = new RelayCommand(DiscardEdits, () => CanEdit && (HasUnsavedChanges || HasEditErrors));
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        try
        {
            var saved = settings.Load();
            if (!string.IsNullOrWhiteSpace(saved))
            {
                WorkspaceFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saved));
                Status = "Workspace root folder selected.";
                if (!Directory.Exists(WorkspaceFolder))
                    Error = "The saved Workspace folder is unavailable. Browse to choose another folder.";
            }
        }
        catch (Exception exception) when (IsFolderError(exception))
        {
            Error = $"Could not load the Workspace folder. {exception.Message}";
        }
        if (WorkspaceFolder.Length > 0) OpenSelectedFolder();
        RefreshDiskStatus();
    }

    public string WorkspaceFolder { get => workspaceFolder; private set => SetProperty(ref workspaceFolder, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    private string DraftEditError => Roots.Select(root => root.EditError).FirstOrDefault(message => message.Length > 0) ?? "";
    public bool HasEditErrors => DraftEditError.Length > 0;
    public string Error { get => HasEditErrors ? DraftEditError : error; private set => SetProperty(ref error, value); }
    public RelayCommand ReturnHomeCommand { get; }
    public RelayCommand BrowseWorkspaceFolderCommand { get; }
    public RelayCommand AddModuleCommand { get; }
    public RelayCommand AddProjectCommand { get; }
    public RelayCommand AddRunCommand { get; }
    public RelayCommand AddExistingModuleCommand { get; }
    public RelayCommand AddExistingProjectCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand DiscardChangesCommand { get; }
    public RelayCommand SaveWorkspaceCommand { get; }
    public RelayCommand CancelCommand { get; }
    public ObservableCollection<WorkspaceTreeNode> Roots { get; } = [];
    public WorkspaceTreeNode? Selected
    {
        get => selected;
        set
        {
            if (ReferenceEquals(selected, value)) return;
            if (selected is not null) selected.IsSelected = false;
            SetProperty(ref selected, value);
            if (selected is not null) selected.IsSelected = true;
            NotifyCommands();
        }
    }
    public bool IsBusy => cancellation is not null;
    public bool CanEdit => !IsBusy && WorkspaceFolder.Length > 0;
    public bool HasSavedWorkspace => workspaceFileState == WorkspaceFileState.Saved;
    public bool ShowNewWorkspace => workspaceFileState == WorkspaceFileState.Missing && savedDefinition is null;
    public bool ShowWorkspaceEditor => savedDefinition is not null || workspaceFileState is WorkspaceFileState.Missing or WorkspaceFileState.Saved;
    public bool HasMissingComponents => missingComponents > 0;
    public string MissingComponentsMessage => missingComponents == 1 ? "1 saved component is missing from disk."
        : $"{missingComponents} saved components are missing from disk.";
    public bool HasUnsavedChanges
    {
        get => hasUnsavedChanges;
        private set
        {
            SetProperty(ref hasUnsavedChanges, value);
            OnPropertyChanged(nameof(WorkspaceFileStatus));
            SaveWorkspaceCommand.RaiseCanExecuteChanged();
            DiscardChangesCommand.RaiseCanExecuteChanged();
        }
    }
    public string WorkspaceFileStatus => workspaceFileState switch
    {
        WorkspaceFileState.Saved => HasUnsavedChanges ? "Saved workspace file found at this location. Unsaved changes." : "Saved workspace file found at this location.",
        WorkspaceFileState.Missing => savedDefinition is null ? "No workspace file at this location. Enter a name and save a workspace."
            : "The saved workspace definition is missing from disk. Save workspace to restore it.",
        WorkspaceFileState.Unavailable => "The selected root folder is unavailable.",
        WorkspaceFileState.Conflict => workspaceFileIssue,
        _ => "Choose a root folder to begin."
    };
    public string WorkspaceName
    {
        get => Roots.FirstOrDefault()?.Name ?? "";
        set { if (Roots.Count > 0) Roots[0].Name = value; }
    }
    public string WorkspaceNameError
    {
        get
        {
            if (!ShowWorkspaceEditor) return "";
            try { EntityName.validate(WorkspaceName); return ""; }
            catch (ArgumentException) { return "Enter a nonblank workspace name without leading/trailing whitespace or control characters."; }
        }
    }
    private bool CanSaveWorkspace => CanEdit && Directory.Exists(WorkspaceFolder) && workspaceFileState != WorkspaceFileState.Conflict
        && (ShowNewWorkspace || savedDefinition is not null && (HasUnsavedChanges || HasMissingComponents))
        && !HasEditErrors && WorkspaceNameError.Length == 0;
    public IReadOnlyList<string> RetainedFolders
    {
        get => retainedFolders;
        private set { SetProperty(ref retainedFolders, value); OnPropertyChanged(nameof(HasRetainedFolders)); }
    }
    public bool HasRetainedFolders => RetainedFolders.Count > 0;

    private void ReplaceDraft(workspace value)
    {
        if (Roots.Count > 0)
        {
            Roots[0].Changed -= DraftChanged;
            Roots[0].ValidationChanged -= EditValidationChanged;
        }
        Roots.Clear();
        var root = WorkspaceTreeNode.FromWorkspace(value);
        Roots.Add(root);
        if (savedDefinition is not null) root.RememberSavedLocations(WorkspaceFolder);
        root.Changed += DraftChanged;
        root.ValidationChanged += EditValidationChanged;
        Selected = root;
        HasUnsavedChanges = false;
        RetainedFolders = [];
        NotifyDraftProperties();
    }
    private void ResetDraft()
    {
        savedDefinition = null;
        var name = WorkspaceFileStore.GetFolderName(WorkspaceFolder);
        ReplaceDraft(workspace.create(name, name, []));
    }
    private void AcceptSavedDefinition(workspace value)
    {
        savedDefinition = value;
        ReplaceDraft(value);
    }
    private void OpenSelectedFolder()
    {
        ResetDraft();
        try
        {
            if (WorkspaceDefinitionFiles.Find(WorkspaceFolder, "workspace") is null) return;
            AcceptSavedDefinition(WorkspaceFileStore.LoadDefinition(WorkspaceFolder));
            Status = "Loaded the saved workspace definition.";
        }
        catch (Exception exception)
        {
            Error = $"Could not load the saved workspace. {exception.GetBaseException().Message}";
        }
    }
    private void Add(WorkspaceEntityKind kind)
    {
        var parent = Selected!;
        var stem = "New " + kind.ToString().ToLowerInvariant();
        var name = stem;
        for (var suffix = 2; parent.HasChildIdentity(kind, name); suffix++) name = stem + " " + suffix;
        var child = new WorkspaceTreeNode(kind, name);
        parent.Children.Add(child);
        Selected = child;
        Status = $"Added {kind.ToString().ToLowerInvariant()} to the definition. Choose Save workspace to save it.";
    }
    private bool CanAdd(WorkspaceEntityKind kind) => CanEdit && !HasEditErrors && Selected?.CanContain(kind) == true;
    private bool CanAddExisting(WorkspaceEntityKind kind)
    {
        if (!CanAdd(kind) || !Directory.Exists(WorkspaceFolder)) return false;
        try { Selected!.GetFolderPath(WorkspaceFolder); return true; }
        catch (ArgumentException) { return false; }
    }
    public async Task AddExistingAsync(WorkspaceEntityKind kind)
    {
        if (kind is not (WorkspaceEntityKind.Module or WorkspaceEntityKind.Project) || !CanAddExisting(kind)) return;
        var parent = Selected!;
        try
        {
            var parentFolder = parent.GetFolderPath(WorkspaceFolder);
            var container = Path.Combine(parentFolder, kind.ToString().ToLowerInvariant() + "s");
            var initial = container;
            while (!Directory.Exists(initial)) initial = Path.GetDirectoryName(initial) ?? WorkspaceFolder;
            var selectedFolder = pickExistingFolder(new(kind, initial, container));
            if (selectedFolder is null) return;
            await RunAsync(async token =>
            {
                if (!Path.IsPathFullyQualified(selectedFolder)) throw new ArgumentException("Choose an absolute source folder.");
                var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedFolder));
                var folderName = Sortopia.Runs.FolderName.validate(Path.GetFileName(folder));
                var destination = Path.Combine(container, folderName);
                var copy = await Task.Run(() => WorkspaceFolderCopy.Prepare(WorkspaceFolder, folder, destination, token), token);
                WorkspaceTreeNode child;
                if (kind == WorkspaceEntityKind.Module)
                {
                    var value = await Task.Run(() => WorkspaceFileStore.LoadModuleFolder(folder, token), token);
                    child = WorkspaceTreeNode.FromModule(value);
                }
                else
                {
                    var value = await Task.Run(() => WorkspaceFileStore.LoadProjectFolder(folder, token), token);
                    child = WorkspaceTreeNode.FromProject(value);
                }
                if (copy is not null) await Task.Run(() => copy.ValidateSource(token), token);
                token.ThrowIfCancellationRequested();
                child.PendingCopy = copy;
                parent.AddExistingChild(child);
                Selected = child;
                Status = copy is null ? $"Added existing {kind.ToString().ToLowerInvariant()} '{child.Name}'. Save workspace to keep it in the definition."
                    : $"Added '{child.Name}' to the draft. Save workspace to copy it and all contents into {destination}.";
            });
        }
        catch (Exception exception) when (IsFolderError(exception)) { Error = exception.GetBaseException().Message; }
    }
    private void Remove()
    {
        var parent = Selected!.Parent!;
        parent.Children.Remove(Selected);
        Selected = parent;
        Status = "Removed from the workspace. Save to keep this change. Existing folders and files will be kept.";
    }
    private void DraftChanged()
    {
        HasUnsavedChanges = true;
        EditValidationChanged();
    }
    private void NotifyDraftProperties()
    {
        OnPropertyChanged(nameof(WorkspaceName));
        OnPropertyChanged(nameof(WorkspaceNameError));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasEditErrors));
        NotifyCommands();
    }
    private void EditValidationChanged()
    {
        RetainedFolders = [];
        NotifyDraftProperties();
    }
    public void RefreshDiskStatus()
    {
        if (IsBusy) return;
        workspaceFileIssue = "";
        try
        {
            workspaceFileState = WorkspaceFolder.Length == 0 ? WorkspaceFileState.NoFolder
                : !Directory.Exists(WorkspaceFolder) ? WorkspaceFileState.Unavailable
                : WorkspaceDefinitionFiles.Find(WorkspaceFolder, "workspace") is not null ? WorkspaceFileState.Saved : WorkspaceFileState.Missing;
        }
        catch (Exception exception) when (IsFolderError(exception))
        { workspaceFileState = WorkspaceFileState.Conflict; workspaceFileIssue = exception.Message; }
        missingComponents = Roots.Sum(root => root.RefreshDiskStatus(WorkspaceFolder));
        OnPropertyChanged(nameof(HasMissingComponents));
        OnPropertyChanged(nameof(MissingComponentsMessage));
        OnPropertyChanged(nameof(HasSavedWorkspace));
        OnPropertyChanged(nameof(ShowNewWorkspace));
        OnPropertyChanged(nameof(ShowWorkspaceEditor));
        OnPropertyChanged(nameof(WorkspaceFileStatus));
        OnPropertyChanged(nameof(WorkspaceNameError));
        NotifyCommands();
    }
    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanEdit));
        foreach (var command in new[] { ReturnHomeCommand, BrowseWorkspaceFolderCommand, AddModuleCommand, AddProjectCommand, AddRunCommand, AddExistingModuleCommand, AddExistingProjectCommand,
            RemoveCommand, SaveWorkspaceCommand, DiscardChangesCommand, CancelCommand })
            command.RaiseCanExecuteChanged();
    }
    public void Cancel() => cancellation?.Cancel();
    public Task SaveWorkspaceAsync() => !CanSaveWorkspace ? Task.CompletedTask : RunAsync(async token =>
    {
        var definition = Roots.Single().ToWorkspace();
        var original = savedDefinition;
        var copies = Roots.Single().GetPendingCopies(WorkspaceFolder).ToArray();
        var result = await Task.Run(() => original is null ? WorkspaceFileStore.CreateWorkspace(WorkspaceFolder, definition, token, copies)
            : WorkspaceFileStore.SaveWorkspace(WorkspaceFolder, definition, original, token, copies), token);
        AcceptSavedDefinition(result.Definition);
        RetainedFolders = result.RetainedFolders;
        Status = $"Workspace '{definition.WorkspaceName}' saved." + (result.CopiedFolders > 0
            ? $" Copied {result.CopiedFolders} imported {(result.CopiedFolders == 1 ? "folder" : "folders")}; sources are unchanged." : "");
    });
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (!CanEdit) return;
        using var source = new CancellationTokenSource();
        cancellation = source;
        NotifyCommands();
        Error = "";
        try { await action(source.Token); }
        catch (OperationCanceledException) { Status = "Workspace operation cancelled."; }
        catch (Exception exception) { Error = exception.GetBaseException().Message; }
        finally { cancellation = null; RefreshDiskStatus(); }
    }

    public Task<bool> ReturnHomeAsync() => TryNavigateAsync(discard =>
    {
        if (discard) DiscardEdits();
        returnHome();
    });

    public Task<bool> TryLeaveAsync() => TryNavigateAsync(discard => { if (discard) DiscardEdits(); });

    private void DiscardEdits()
    {
        if (savedDefinition is not null) AcceptSavedDefinition(savedDefinition);
        else ResetDraft();
        Error = "";
        Status = "Unsaved workspace changes discarded.";
        RefreshDiskStatus();
    }

    private async Task<bool> TryNavigateAsync(Action<bool> navigate)
    {
        if (resolvingNavigation) return false;
        if (IsBusy)
        {
            Error = "Wait for the workspace operation to finish, or cancel it before leaving.";
            return false;
        }
        resolvingNavigation = true;
        NotifyCommands();
        try
        {
            var discard = false;
            if (HasUnsavedChanges || HasEditErrors)
            {
                switch (confirmUnsavedChanges())
                {
                    case WorkspaceUnsavedChoice.Cancel: return false;
                    case WorkspaceUnsavedChoice.Discard: discard = true; break;
                    case WorkspaceUnsavedChoice.Save:
                        if (!CanSaveWorkspace)
                        {
                            Error = WorkspaceNameError.Length > 0 ? WorkspaceNameError : "Correct the workspace errors before saving, or choose Discard or Cancel.";
                            return false;
                        }
                        await SaveWorkspaceAsync();
                        if (HasUnsavedChanges || HasEditErrors || Error.Length > 0) return false;
                        break;
                    default: return false;
                }
            }
            navigate(discard);
            return true;
        }
        finally { resolvingNavigation = false; NotifyCommands(); }
    }

    public async Task BrowseWorkspaceFolderAsync()
    {
        if (IsBusy || resolvingNavigation) return;
        try
        {
            var selected = pickFolder(string.IsNullOrEmpty(WorkspaceFolder) ? null : WorkspaceFolder);
            if (selected is null) return;
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Folder does not exist: {folder}");
            if (string.Equals(folder, WorkspaceFolder, StringComparison.OrdinalIgnoreCase)) return;
            await TryNavigateAsync(_ =>
            {
                settings.Save(folder);
                WorkspaceFolder = folder;
                Status = "Workspace root folder selected.";
                Error = "";
                OpenSelectedFolder();
                RefreshDiskStatus();
            });
        }
        catch (Exception exception) when (IsFolderError(exception))
        {
            Error = $"Could not set the Workspace folder. {exception.Message}";
        }
    }

    private static bool IsFolderError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
