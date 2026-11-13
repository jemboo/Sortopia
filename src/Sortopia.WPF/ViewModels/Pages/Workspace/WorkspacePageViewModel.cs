using System.IO;
using System.Collections.ObjectModel;
using Sortopia.WPF.Commands;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public enum WorkspaceUnsavedChoice { Save, Discard, Cancel }
public sealed record WorkspaceFolderRequest(WorkspaceEntityKind Kind, string InitialDirectory, string ContainerFolder);

public sealed partial class WorkspacePageViewModel : ViewModelBase
{
    private readonly Action returnHome;
    private readonly Func<string?, string?> pickFolder;
    private readonly Func<WorkspaceUnsavedChoice> confirmUnsavedChanges;
    private readonly Func<WorkspaceFolderRequest, string?> pickExistingFolder;
    private readonly Action<string>? openFolder;
    private readonly WorkspaceFolderSettings settings;
    private string workspaceFolder = "";
    private string status = "No Workspace folder selected.";
    private string error = "";
    private WorkspaceTreeNode? selected;
    private WorkspaceTreeNode? cutNode;
    private IReadOnlyList<string> retainedFolders = [];
    private workspace? savedDefinition;
    private bool hasUnsavedChanges;
    private bool resolvingNavigation;
    private CancellationTokenSource? cancellation;
    private enum WorkspaceFileState { NoFolder, Unavailable, Missing, Saved, Conflict }
    private WorkspaceFileState workspaceFileState;
    private string workspaceFileIssue = "";
    private int missingComponents;
    private bool needsLayoutConversion;

    public WorkspacePageViewModel(Action returnHome, Func<string?, string?> pickFolder, WorkspaceFolderSettings settings,
        Func<WorkspaceUnsavedChoice>? confirmUnsavedChanges = null, Func<WorkspaceFolderRequest, string?>? pickExistingFolder = null,
        Action<string>? openFolder = null, Action? inspectFiles = null, Func<string, Guid?, Task>? openResultsExecution = null)
    {
        this.returnHome = returnHome;
        this.pickFolder = pickFolder;
        this.confirmUnsavedChanges = confirmUnsavedChanges ?? (() => WorkspaceUnsavedChoice.Cancel);
        this.pickExistingFolder = pickExistingFolder ?? (_ => null);
        this.openFolder = openFolder;
        this.settings = settings;
        InitializeSaveValidation();
        InitializeInspector();
        ReturnHomeCommand = new RelayCommand(async () => await ReturnHomeAsync(), () => !IsBusy && !resolvingNavigation);
        InspectFilesCommand = new RelayCommand(inspectFiles ?? (() => { }), () => inspectFiles is not null && !IsBusy);
        BrowseWorkspaceFolderCommand = new RelayCommand(async () => await BrowseWorkspaceFolderAsync(), () => !IsBusy && !resolvingNavigation);
        OpenFolderCommand = new RelayCommand(OpenSelectedFolderInExplorer, () => CanEdit && this.openFolder is not null && Directory.Exists(SelectedExplorerFolder));
        AddModuleCommand = new RelayCommand(() => Add(WorkspaceEntityKind.Module, hierarchyKind.Root), () => CanAddModule(hierarchyKind.Root));
        AddBranchModuleCommand = new RelayCommand(() => Add(WorkspaceEntityKind.Module, hierarchyKind.Branch), () => CanAddModule(hierarchyKind.Branch));
        AddProjectCommand = new RelayCommand(() => Add(WorkspaceEntityKind.Project), () => CanAdd(WorkspaceEntityKind.Project));
        AddRunCommand = new RelayCommand(() => Add(WorkspaceEntityKind.Run), () => CanAdd(WorkspaceEntityKind.Run));
        CreateProjectVersionCommand = new RelayCommand(CreateProjectVersion, () => CanEdit && Selected is { IsProject: true, IsSealed: true, Parent: not null });
        EditProjectDefaultsCommand = new RelayCommand(() =>
        { var key = ActiveItemKey; Selected = Selected!.Parent; SelectConfigurationItem(key); }, () => CanEdit && Selected?.HasParentDefaults == true);
        SelectValueSourceCommand = new RelayCommand(source =>
        {
            if (source is WorkspaceConfigurationViewModel editor
                && Roots.SelectMany(root => root.DescendantsAndSelf()).FirstOrDefault(item => ReferenceEquals(item.Editor, editor)) is { } node)
            { var key = ActiveItemKey; Selected = node; SelectConfigurationItem(key); }
        }, () => CanEdit);
        AddExistingModuleCommand = new RelayCommand(async () => await AddExistingAsync(WorkspaceEntityKind.Module), () => CanAddExisting(WorkspaceEntityKind.Module));
        AddExistingProjectCommand = new RelayCommand(async () => await AddExistingAsync(WorkspaceEntityKind.Project), () => CanAddExisting(WorkspaceEntityKind.Project));
        RemoveCommand = new RelayCommand(Remove, () => CanEdit && Selected?.Parent is not null);
        CutCommand = new RelayCommand(Cut, () => CanEdit && !HasEditErrors && Selected?.Parent is not null);
        PasteCommand = new RelayCommand(async () => await PasteAsync(), () => CanPaste);
        CancelCutCommand = new RelayCommand(CancelCut, () => CanEdit && HasCutItem);
        SaveWorkspaceCommand = new RelayCommand(async () => await SaveWorkspaceAsync(), () => CanSaveWorkspace);
        ConvertFolderLayoutCommand = new RelayCommand(async () => await ConvertFolderLayoutAsync(), () => !IsBusy && NeedsLayoutConversion);
        InitializeRunParameterCommands(openResultsExecution);
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
    public string Error
    {
        get => HasEditErrors ? DraftEditError : error;
        private set { if (SetProperty(ref error, value)) OnPropertyChanged(nameof(OperationError)); }
    }
    // Draft errors are already shown beside Save, with links to their fields.
    public string OperationError => HasEditErrors ? "" : error;
    public RelayCommand ReturnHomeCommand { get; }
    public RelayCommand InspectFilesCommand { get; }
    public RelayCommand BrowseWorkspaceFolderCommand { get; }
    public RelayCommand ConvertFolderLayoutCommand { get; }
    public bool NeedsLayoutConversion => needsLayoutConversion;
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand AddModuleCommand { get; }
    public RelayCommand AddBranchModuleCommand { get; }
    public string AddProjectLabel => $"New {(Selected?.IsBranch == true ? "branch" : "root")} project";
    public string AddRunLabel => $"New {(Selected?.IsBranch == true ? "branch" : "root")} run";
    public bool ShowRootModuleAction => Selected?.IsWorkspace == true || Selected is { Kind: WorkspaceEntityKind.Module, IsBranch: false };
    public bool ShowBranchModuleAction => Selected?.IsWorkspace == true || Selected is { Kind: WorkspaceEntityKind.Module, IsBranch: true };
    public bool ShowProjectAction => Selected?.Kind == WorkspaceEntityKind.Module;
    public bool ShowRunAction => Selected?.Kind == WorkspaceEntityKind.Project;
    public string SelectionHint => Selected?.ContainmentHint ?? "Select an item in the tree.";
    public string PasteHint => cutNode is not null && Selected is not null && !Selected.CanContain(cutNode)
        ? $"A {cutNode.KindLabel.ToLowerInvariant()} cannot be placed in a {Selected.KindLabel.ToLowerInvariant()}. Choose a matching parent."
        : "Paste under the selected parent (Ctrl+V in the tree).";
    public RelayCommand AddProjectCommand { get; }
    public RelayCommand AddRunCommand { get; }
    public RelayCommand CreateProjectVersionCommand { get; }
    public RelayCommand EditProjectDefaultsCommand { get; }
    public RelayCommand SelectValueSourceCommand { get; }
    public RelayCommand AddExistingModuleCommand { get; }
    public RelayCommand AddExistingProjectCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand CutCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand CancelCutCommand { get; }
    public bool HasCutItem => cutNode is not null;
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
            InspectorSelectionChanged();
            OnPropertyChanged(nameof(SelectedFolderPath));
            NotifyCommands();
        }
    }
    private string SelectedExplorerFolder => Selected?.IsRun == true ? Path.GetDirectoryName(SelectedFolderPath) ?? "" : SelectedFolderPath;
    public string SelectedFolderPath
    {
        get
        {
            if (Selected is null || WorkspaceFolder.Length == 0) return "";
            try { return Path.GetFullPath(Selected.GetFolderPath(WorkspaceFolder)); }
            catch (Exception exception) when (IsFolderError(exception)) { return ""; }
        }
    }
    private void OpenSelectedFolderInExplorer()
    {
        var folder = SelectedExplorerFolder;
        try
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("The folder no longer exists.");
            openFolder!(folder);
            Error = "";
            Status = "Opened folder in File Explorer.";
        }
        catch (Exception exception) when (IsFolderError(exception) || exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Error = $"Could not open folder '{folder}'. {exception.Message}";
        }
    }
    public bool IsBusy => cancellation is not null;
    public bool CanEdit => !IsBusy && WorkspaceFolder.Length > 0 && !NeedsLayoutConversion;
    public bool HasSavedWorkspace => workspaceFileState == WorkspaceFileState.Saved;
    public bool ShowNewWorkspace => workspaceFileState == WorkspaceFileState.Missing && savedDefinition is null;
    public bool ShowWorkspaceEditor => !NeedsLayoutConversion && (savedDefinition is not null || workspaceFileState is WorkspaceFileState.Missing or WorkspaceFileState.Saved);
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
        get => Roots.FirstOrDefault()?.NameInput ?? "";
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
        ClearCut();
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
        ReplaceDraft(workspace.create(Guid.NewGuid(), name, name, []));
    }
    private void AcceptSavedDefinition(workspace value)
    {
        savedDefinition = value;
        ReplaceDraft(value);
    }
    private void OpenSelectedFolder()
    {
        executions.Clear();
        needsLayoutConversion = false;
        ResetDraft();
        try
        {
            if (WorkspaceDefinitionFiles.Find(WorkspaceFolder, "workspace") is null) return;
            if (WorkspaceDefinitionFiles.Version(WorkspaceFolder, "workspace") == 15)
            {
                needsLayoutConversion = true;
                Status = "This workspace uses the earlier modules/projects folder layout. Convert it to open it with shorter paths.";
                Error = "";
                return;
            }
            AcceptSavedDefinition(WorkspaceFileStore.LoadDefinition(WorkspaceFolder));
            Status = "Loaded the saved workspace definition.";
        }
        catch (Exception exception)
        {
            Error = $"Could not load the saved workspace. {exception.GetBaseException().Message}";
        }
    }
    private void Add(WorkspaceEntityKind kind, hierarchyKind? family = null)
    {
        var parent = Selected!;
        var childFamily = family ?? parent.Family;
        var stem = "New " + kind.ToString().ToLowerInvariant();
        var name = stem;
        for (var suffix = 2; parent.HasChildIdentity(kind, name); suffix++) name = stem + " " + suffix;
        var child = new WorkspaceTreeNode(kind, name, family: childFamily);
        parent.Children.Add(child);
        Selected = child;
        Status = $"Added {kind.ToString().ToLowerInvariant()} to the definition. Choose Save workspace to save it.";
    }
    private void CreateProjectVersion()
    {
        var source = Selected!;
        var parent = source.Parent!;
        var stem = source.Name + " v2";
        var name = stem;
        for (var suffix = 2; parent.HasChildIdentity(WorkspaceEntityKind.Project, name); suffix++) name = stem + " " + suffix;
        var version = new WorkspaceTreeNode(WorkspaceEntityKind.Project, name, family: source.Family);
        version.Editor!.LoadProject(source.Editor!.ToProjectSettings());
        parent.Children.Add(version);
        Selected = version;
        Status = "Created an editable project version with a new ID and no runs.";
    }
    private bool CanAddModule(hierarchyKind family) => CanAdd(WorkspaceEntityKind.Module) && (Selected!.IsWorkspace || Selected.Family == family);
    private bool CanAdd(WorkspaceEntityKind kind) => CanEdit && (kind != WorkspaceEntityKind.Run || !HasEditErrors) && Selected?.CanContain(kind) == true;
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
            var container = parentFolder;
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
                WorkspaceFolderCopy? copy;
                WorkspaceTreeNode child;
                if (kind == WorkspaceEntityKind.Module)
                {
                    var import = await Task.Run(() => WorkspaceImport.PrepareModule(WorkspaceFolder, folder, destination, token), token);
                    child = WorkspaceTreeNode.FromModule(import.Definition);
                    copy = import.Copy;
                }
                else
                {
                    var import = await Task.Run(() => WorkspaceImport.PrepareProject(WorkspaceFolder, folder, destination, token), token);
                    child = WorkspaceTreeNode.FromProject(import.Definition);
                    copy = import.Copy;
                }
                token.ThrowIfCancellationRequested();
                child.PendingCopy = copy;
                if (copy is not null) child.RememberImportLocations(copy.SourceFolder, copy.Id, copy.OriginalIds);
                parent.AddExistingChild(child);
                if (copy is null) child.RememberSavedLocations(WorkspaceFolder);
                Selected = child;
                Status = copy is null ? $"Added existing {kind.ToString().ToLowerInvariant()} '{child.Name}'. Save workspace to keep it in the definition."
                    : $"Added '{child.Name}' to the draft. Save workspace to copy it and all contents into {destination}.";
            });
        }
        catch (Exception exception) when (IsFolderError(exception)) { Error = exception.GetBaseException().Message; }
    }
    private void Remove()
    {
        if (Selected!.DescendantsAndSelf().Contains(cutNode)) ClearCut();
        var parent = Selected!.Parent!;
        parent.Children.Remove(Selected);
        Selected = parent;
        Status = "Removed from the workspace. Save to keep this change. Existing folders and files will be kept.";
    }
    private bool CanPaste => CanEdit && !HasEditErrors && cutNode is not null && Selected?.CanContain(cutNode) == true
        && !cutNode.DescendantsAndSelf().Contains(Selected);
    private void Cut()
    {
        ClearCut();
        cutNode = Selected!;
        cutNode.IsCut = true;
        OnPropertyChanged(nameof(HasCutItem));
        NotifyCommands();
        Status = $"Cut '{cutNode.Name}'. Select a parent and choose Paste. Press Escape in the tree to cancel.";
    }
    private void ClearCut()
    {
        if (cutNode is not null) cutNode.IsCut = false;
        cutNode = null;
        OnPropertyChanged(nameof(HasCutItem));
        NotifyCommands();
    }
    private void CancelCut()
    {
        ClearCut();
        Status = "Cut cancelled.";
    }
    public Task PasteAsync() => !CanPaste ? Task.CompletedTask : RunAsync(async token =>
    {
        var node = cutNode!;
        var parent = Selected!;
        parent.ValidateChildPlacement(node);
        var destination = node.IsRun ? Path.Combine(parent.GetFolderPath(WorkspaceFolder), "runs", node.PathName)
            : Path.Combine(parent.GetFolderPath(WorkspaceFolder), node.PathName);
        if (!ReferenceEquals(parent, node.Parent) && !string.Equals(destination, node.SavedFolderPath, StringComparison.OrdinalIgnoreCase)
            && (Directory.Exists(destination) || File.Exists(destination)))
            throw new IOException($"The destination already exists. Choose another parent or folder name: {destination}");
        await PrepareImportedChildCopyAsync(node, destination, token);
        var oldParent = node.Parent!;
        oldParent.Children.Remove(node);
        // Append within the entity's typed collection, keeping modules/projects/runs grouped.
        var lastOfKind = parent.Children.LastOrDefault(child => child.Kind == node.Kind);
        var index = lastOfKind is null ? parent.Children.TakeWhile(child => child.Kind < node.Kind).Count() : parent.Children.IndexOf(lastOfKind) + 1;
        parent.Children.Insert(index, node);
        node.MarkMoved();
        ClearCut();
        Selected = node;
        Status = $"Moved '{node.Name}' under '{parent.Name}' in the draft. Save workspace to move it on disk.";
    });
    private async Task PrepareImportedChildCopyAsync(WorkspaceTreeNode node, string destination, CancellationToken token)
    {
        // A relocated child of an unsaved import becomes its own copy; exclude it from the parent copy.
        if (node.PendingCopy is null && node.ImportOriginId is { } originId && node.ImportSourcePath is { } source)
        {
            var origin = Roots.Single().DescendantsAndSelf().Select(item => item.PendingCopy).FirstOrDefault(copy => copy?.Id == originId)
                ?? throw new IOException("Save or discard the pending import before moving this child.");
            var copy = await Task.Run(() => origin.PrepareChild(WorkspaceFolder, source, destination, token), token);
            token.ThrowIfCancellationRequested();
            node.PendingCopy = copy;
            node.CopyOriginId = originId;
            node.CopyOriginRelativePath = Path.GetRelativePath(origin.SourceFolder, source);
            node.RememberImportLocations(source, copy.Id, copy.OriginalIds);
        }
    }
    private async Task PrepareModuleLocationCopiesAsync(CancellationToken token)
    {
        var nodes = Roots.Single().DescendantsAndSelf().ToArray();
        // Visit parents before children so nested location edits use their nearest pending copy.
        foreach (var node in nodes)
        {
            if (node.Kind != WorkspaceEntityKind.Module || node.PendingCopy is not null
                || node.ImportOriginId is not { } originId || node.ImportSourcePath is not { } source) continue;
            var origin = nodes.Single(item => item.PendingCopy?.Id == originId);
            var inheritedLocation = Path.Combine(origin.GetFolderPath(WorkspaceFolder), Path.GetRelativePath(origin.PendingCopy!.SourceFolder, source));
            var destination = node.GetFolderPath(WorkspaceFolder);
            if (!string.Equals(inheritedLocation, destination, StringComparison.Ordinal))
                await PrepareImportedChildCopyAsync(node, destination, token);
        }
    }
    private void DraftChanged()
    {
        HasUnsavedChanges = true;
        EditValidationChanged();
    }
    private void NotifyDraftProperties()
    {
        OnPropertyChanged(nameof(SelectedFolderPath));
        OnPropertyChanged(nameof(WorkspaceName));
        OnPropertyChanged(nameof(WorkspaceNameError));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(OperationError));
        OnPropertyChanged(nameof(HasEditErrors));
        NotifyCommands();
    }
    private void EditValidationChanged()
    {
        if (HasEditErrors) HasUnsavedChanges = true;
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
        OnPropertyChanged(nameof(NeedsLayoutConversion));
        OnPropertyChanged(nameof(WorkspaceFileStatus));
        OnPropertyChanged(nameof(WorkspaceNameError));
        NotifyCommands();
    }
    private void NotifyCommands()
    {
        RefreshInspector();
        NotifySaveValidation();
        OnPropertyChanged(nameof(SelectedExecution));
        OnPropertyChanged(nameof(GenerationHint));
        OnPropertyChanged(nameof(GenerationInstructions));
        NotifyRunParameterCommands();
        foreach (var property in new[] { nameof(AddProjectLabel), nameof(AddRunLabel), nameof(ShowRootModuleAction), nameof(ShowBranchModuleAction), nameof(ShowProjectAction), nameof(ShowRunAction), nameof(SelectionHint), nameof(PasteHint) }) OnPropertyChanged(property);
        OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanEdit));
        foreach (var command in new[] { ReturnHomeCommand, InspectFilesCommand, BrowseWorkspaceFolderCommand, OpenFolderCommand, AddModuleCommand, AddBranchModuleCommand, AddProjectCommand, AddRunCommand, CreateProjectVersionCommand, EditProjectDefaultsCommand, SelectValueSourceCommand, AddExistingModuleCommand, AddExistingProjectCommand,
            RemoveCommand, CutCommand, PasteCommand, CancelCutCommand, SaveWorkspaceCommand, ConvertFolderLayoutCommand, DiscardChangesCommand, CancelCommand })
            command.RaiseCanExecuteChanged();
    }
    public void Cancel() => cancellation?.Cancel();
    public Task ConvertFolderLayoutAsync() => !ConvertFolderLayoutCommand.CanExecute(null) ? Task.CompletedTask : RunAsync(async token =>
    {
        var result = await Task.Run(() => WorkspaceFileStore.ConvertFolderLayout(WorkspaceFolder, token), token);
        needsLayoutConversion = false;
        AcceptSavedDefinition(result.Definition);
        RetainedFolders = result.RetainedFolders;
        Status = "Converted to shorter folder paths. Project IDs, runs, run parameters, and results were preserved.";
    }, convertingLayout: true);
    public Task SaveWorkspaceAsync() => !CanSaveWorkspace ? Task.CompletedTask : RunAsync(async token =>
    {
        try
        {
            var definition = Roots.Single().ToWorkspace();
            var original = savedDefinition;
            await PrepareModuleLocationCopiesAsync(token);
            var copies = Roots.Single().GetPendingCopies(WorkspaceFolder).ToArray();
            var moves = Roots.Single().GetPendingMoves(WorkspaceFolder);
            var result = await Task.Run(() => original is null ? WorkspaceFileStore.CreateWorkspace(WorkspaceFolder, definition, token, copies, moves)
                : WorkspaceFileStore.SaveWorkspace(WorkspaceFolder, definition, original, token, copies, moves), token);
            AcceptSavedDefinition(result.Definition);
            RetainedFolders = result.RetainedFolders;
            Status = $"Workspace '{definition.WorkspaceName}' saved." + (result.CopiedFolders > 0
                ? $" Copied {result.CopiedFolders} imported {(result.CopiedFolders == 1 ? "component" : "components")}; sources are unchanged." : "")
                + (result.MovedFolders > 0 ? $" Moved {result.MovedFolders} {(result.MovedFolders == 1 ? "component" : "components")} with all contents." : "");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            Error = $"Could not save workspace ‘{WorkspaceName}’. {exception.GetBaseException().Message} Your edits are still open; correct the problem and save again.";
            Status = "Workspace changes have not been saved.";
        }
    });
    private async Task RunAsync(Func<CancellationToken, Task> action, bool convertingLayout = false)
    {
        if (IsBusy || (!CanEdit && !(convertingLayout && NeedsLayoutConversion))) return;
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
                            Error = SaveHint;
                            return false;
                        }
                        await SaveWorkspaceAsync();
                        if (HasUnsavedChanges || HasEditErrors || Error.Length > 0) return false;
                        break;
                    default: return false;
                }
            }
            navigate(discard);
            ClearCut();
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
        exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
