using System.Collections.ObjectModel;
using System.IO;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using WorkspaceModule = Sortopia.Workspaces.workspaceModule;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public enum WorkspaceEntityKind { Workspace, Module, Project, Run }

public sealed partial class WorkspaceTreeNode : ViewModelBase
{
    private string name;
    private string folderName;
    private string nameInput;
    private string folderNameInput;
    private string nameError = "";
    private string folderNameError = "";
    private sealed record SavedChild(WorkspaceTreeNode Node, string Name, string FolderName);
    private SavedChild[] savedChildren = [];
    private bool isSelected;
    private string? savedFolderPath;
    private string diskStatus = "Not saved";
    private string diskStatusDetail = "This component has not been saved yet.";
    private bool isMissingOnDisk;
    private bool hasDiskWarning;
    private bool isCut;
    private bool moveWithContents;
    public bool IsCut { get => isCut; internal set => SetProperty(ref isCut, value); }
    internal string? SavedFolderPath => savedFolderPath;
    internal void MarkMoved() => moveWithContents = true;
    internal IEnumerable<WorkspaceTreeNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.DescendantsAndSelf()) yield return node;
    }
    internal IReadOnlyList<WorkspaceFolderMove> GetPendingMoves(string rootFolder)
    {
        var moves = new List<WorkspaceFolderMove>();
        void Visit(WorkspaceTreeNode node, bool moving, WorkspaceFolderMove? enclosing)
        {
            moving |= node.moveWithContents;
            if (moving && node.savedFolderPath is { } source && (Directory.Exists(source) || File.Exists(source)))
            {
                var destination = node.GetFolderPath(rootFolder);
                var inherited = enclosing is not null && enclosing.ContainsSourcePath(source)
                    ? Path.Combine(enclosing.DestinationFolder, Path.GetRelativePath(enclosing.SourceFolder, source)) : source;
                if (!string.Equals(destination, inherited, StringComparison.Ordinal))
                {
                    var move = new WorkspaceFolderMove(source, destination);
                    moves.Add(move);
                    enclosing = move;
                }
            }
            foreach (var child in node.Children) Visit(child, moving, enclosing);
        }
        Visit(this, false, null);
        return moves;
    }
    public WorkspaceTreeNode(WorkspaceEntityKind kind, string name, string? folderName = null, Guid? id = null, hierarchyKind family = hierarchyKind.Root)
    {
        if (!Enum.IsDefined(family)) throw new ArgumentException("Choose root or branch.", nameof(family));
        Family = family;
        Id = id ?? Guid.NewGuid();
        Kind = kind; this.name = name; this.folderName = kind == WorkspaceEntityKind.Run ? "" : folderName ?? name;
        nameInput = name; folderNameInput = this.folderName;
        Children.CollectionChanged += (_, args) =>
        {
            if (args.OldItems is not null) foreach (WorkspaceTreeNode child in args.OldItems) { child.Parent = null; child.RefreshInheritedSubtree(); child.Changed -= ChildChanged; child.ValidationChanged -= ChildValidationChanged; }
            if (args.NewItems is not null) foreach (WorkspaceTreeNode child in args.NewItems) { child.Parent = this; child.RefreshInheritedSubtree(); child.Changed += ChildChanged; child.ValidationChanged += ChildValidationChanged; }
            Changed?.Invoke();
            ValidationChanged?.Invoke();
        };
        if (kind != WorkspaceEntityKind.Workspace)
        {
            Editor = new(kind, IsBranch, () => Parent?.Editor, () => Name, SourceProjects, () => Children.Where(node => node.IsRun).Select(node => node.Editor!));
            Editor.Changed += ConfigurationChanged;
        }
    }
    public WorkspaceConfigurationViewModel? Editor { get; }
    private project? sealedProject;
    private Guid? savedParentProjectId;
    public bool IsSealed => sealedProject is not null;
    public string ProjectState => IsProject ? IsSealed ? "Sealed · settings fixed for generated work" : "Draft · editable until run parameters are generated" : "";
    internal void MarkSealed(project snapshot)
    {
        sealedProject = project.withRuns(snapshot, []);
        var settings = SettingsDto.fromDomain(snapshot.Configuration);
        Editor!.LoadProject(settings);
        Editor.Freeze(settings);
        RefreshProjectState();
    }
    internal void ResumeEditing(projectDefinitionDto definition)
    {
        sealedProject = null;
        Editor!.ResumeEditing(definition.localOverrides);
        RefreshProjectState();
    }
    private void RefreshProjectState()
    {
        RefreshInheritedSubtree();
        OnPropertyChanged(nameof(IsSealed)); OnPropertyChanged(nameof(ProjectState));
    }
    private IEnumerable<WorkspaceSourceProject> SourceProjects()
    {
        var root = this;
        while (root.Parent is not null) root = root.Parent;
        static string Location(WorkspaceTreeNode node)
        {
            var names = new Stack<string>();
            for (var ancestor = node.Parent; ancestor is { IsWorkspace: false }; ancestor = ancestor.Parent) names.Push(ancestor.Name);
            return string.Join(" / ", names);
        }
        return root.DescendantsAndSelf().Where(node => node.IsProject && node != this && node != Parent)
            .Select(node => new WorkspaceSourceProject(node.Id, node.Name, node.IsSealed, node.Editor!.ToProjectSettings(), node.Editor,
                node.Children.Count > 0, Location(node)));
    }
    private void ConfigurationChanged()
    {
        foreach (var child in Children) child.RefreshInheritedSubtree();
        Changed?.Invoke();
    }
    private void RefreshInheritedSubtree()
    {
        OnPropertyChanged(nameof(HasParentDefaults));
        Editor?.RefreshInherited();
        foreach (var child in Children) child.RefreshInheritedSubtree();
    }

    internal event Action? Changed;
    internal event Action? ValidationChanged;
    private void ChildChanged()
    {
        Editor?.RefreshQueryChoices();
        foreach (var child in Children) child.Editor?.RefreshQueryChoices();
        Changed?.Invoke();
    }
    private void ChildValidationChanged() => ValidationChanged?.Invoke();
    public hierarchyKind Family { get; }
    public bool IsBranch => Family == hierarchyKind.Branch;
    public string FamilyName => IsBranch ? "Branch" : "Root";
    public Guid Id { get; }
    public bool HasFolder => !IsRun;
    internal string PathName => IsRun ? WorkspaceDefinitionFiles.Name(Id) : Sortopia.Runs.FolderName.validate(FolderName);
    public WorkspaceEntityKind Kind { get; }
    public string KindLabel => IsWorkspace ? "Workspace" : $"{FamilyName} {Kind.ToString().ToLowerInvariant()}";
    public string ContainmentHint => IsWorkspace ? "Choose a root module for runs without setup, or a branch module for runs with setup."
        : Kind == WorkspaceEntityKind.Module ? $"Contains {FamilyName.ToLowerInvariant()} modules and {FamilyName.ToLowerInvariant()} projects. New children inherit this type."
        : Kind == WorkspaceEntityKind.Project ? $"Contains only {FamilyName.ToLowerInvariant()} runs. Projects do not contain other projects."
        : IsBranch ? "Branch run: supply the setup and results functions’ required parameters. Functions and database names are fixed by the project; description can be overridden."
        : "Root run: supply execution parameters and choose whether to inherit the project description.";
    public bool IsProject => Kind == WorkspaceEntityKind.Project;
    public bool HasConfiguration => !IsWorkspace;
    public bool HasParentDefaults => Parent?.HasConfiguration == true;
    public bool IsRun => Kind == WorkspaceEntityKind.Run;
    public bool IsWorkspace => Kind == WorkspaceEntityKind.Workspace;
    public string DiskStatus => diskStatus;
    public string DiskStatusDetail => diskStatusDetail;
    public bool IsMissingOnDisk => isMissingOnDisk;
    public bool HasDiskWarning => hasDiskWarning;
    internal void RememberSavedLocations(string rootFolder)
    {
        savedFolderPath = GetFolderPath(rootFolder);
        foreach (var child in Children) child.RememberSavedLocations(rootFolder);
    }
    internal int RefreshDiskStatus(string rootFolder, bool pendingCopy = false, bool pendingMove = false)
    {
        pendingCopy |= PendingCopy is not null;
        pendingMove |= moveWithContents;
        var label = "";
        var detail = "";
        var missing = false;
        var warning = false;
        try
        {
            var path = GetFolderPath(rootFolder);
            var previouslySavedHere = string.Equals(savedFolderPath, path, StringComparison.OrdinalIgnoreCase);
            if (pendingCopy && savedFolderPath is null) { label = "Pending copy"; detail = $"Saving will copy this component into {path}."; }
            else if (IsRun)
            {
                var source = pendingMove || previouslySavedHere ? savedFolderPath ?? path : path;
                missing = (pendingMove && savedFolderPath is not null || previouslySavedHere) && !File.Exists(source);
                label = missing ? "Missing definition" : pendingMove && !string.Equals(source, path, StringComparison.Ordinal) ? "Pending move" : File.Exists(path) ? "" : "Not saved";
                detail = missing ? $"The run definition is missing: {source}" : $"Run definition: {path}";
            }
            else if (pendingMove && savedFolderPath is { } source && !string.Equals(source, path, StringComparison.Ordinal))
            {
                var folderMissing = !Directory.Exists(source);
                missing = folderMissing || WorkspaceDefinitionFiles.Find(source, Kind.ToString().ToLowerInvariant()) is null;
                label = folderMissing ? "Missing folder" : missing ? "Missing definition" : "Pending move";
                detail = folderMissing ? $"The folder to move is missing: {source}"
                    : missing ? $"The definition for the component to move is missing in {source}."
                    : $"Saving will move this folder and all contents from {source} to {path}.";
            }
            else if (!Directory.Exists(path))
            {
                missing = previouslySavedHere;
                label = missing ? "Missing folder" : "Not saved";
                detail = missing ? $"The saved component's folder is missing: {path}" : $"Saving will create this folder: {path}";
            }
            else if (WorkspaceDefinitionFiles.Find(path, Kind.ToString().ToLowerInvariant()) is null)
            {
                missing = previouslySavedHere;
                label = missing ? "Missing definition" : "Not saved";
                detail = missing ? $"The saved component's MessagePack definition is missing in {path}." : $"Saving will write this component's definition in {path}.";
            }
            else detail = $"Saved in {path}";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { label = "Disk error"; detail = exception.Message; warning = true; }
        SetProperty(ref diskStatus, label, nameof(DiskStatus));
        SetProperty(ref diskStatusDetail, detail, nameof(DiskStatusDetail));
        SetProperty(ref isMissingOnDisk, missing, nameof(IsMissingOnDisk));
        SetProperty(ref hasDiskWarning, warning || missing, nameof(HasDiskWarning));
        return (missing ? 1 : 0) + Children.Sum(child => child.RefreshDiskStatus(rootFolder, pendingCopy, pendingMove));
    }
    internal WorkspaceFolderCopy? PendingCopy { get; set; }
    internal Guid? ImportOriginId { get; private set; }
    internal string? ImportSourcePath { get; private set; }
    internal Guid? CopyOriginId { get; set; }
    internal string? CopyOriginRelativePath { get; set; }
    internal void RememberImportLocations(string source, Guid originId, IReadOnlyDictionary<Guid, Guid>? originalIds = null)
    {
        ImportOriginId = originId;
        ImportSourcePath = source;
        foreach (var child in Children)
        {
            var pathName = child.IsRun && originalIds?.TryGetValue(child.Id, out var original) == true ? WorkspaceDefinitionFiles.Name(original) : child.PathName;
            var childSource = child.IsRun ? Path.Combine(source, "runs", pathName) : Path.Combine(source, pathName);
            child.RememberImportLocations(childSource, originId, originalIds);
        }
    }
    public string PendingCopyDescription => PendingCopy is null ? "" : $"Will copy this {(IsRun ? "run file" : "folder and all contents")} when saved: {PendingCopy.SourceFolder}";
    internal IEnumerable<WorkspaceFolderCopy> GetPendingCopies(string rootFolder)
    {
        var nodes = DescendantsAndSelf().ToArray();
        foreach (var node in nodes)
            if (node.PendingCopy is { } copy)
                yield return copy.WithDestination(rootFolder, node.GetFolderPath(rootFolder))
                    .WithExclusions(nodes.Where(other => other.CopyOriginId == copy.Id).Select(other => other.CopyOriginRelativePath!)
                        .Concat(node.DescendantsAndSelf().SelectMany(parent => parent.savedChildren).Select(child => child.Node)
                            .Where(child => child.ImportOriginId == copy.Id && child.ImportSourcePath is not null && !nodes.Any(current => current.Id == child.Id))
                            .Select(child => Path.GetRelativePath(copy.SourceFolder, child.ImportSourcePath!))));
    }
    public bool CanContain(WorkspaceEntityKind child) => Kind switch
    {
        WorkspaceEntityKind.Workspace => child == WorkspaceEntityKind.Module,
        WorkspaceEntityKind.Module => child is WorkspaceEntityKind.Module or WorkspaceEntityKind.Project,
        WorkspaceEntityKind.Project => child == WorkspaceEntityKind.Run,
        _ => false
    };
    public bool CanContain(WorkspaceTreeNode child) => CanContain(child.Kind) && (IsWorkspace || Family == child.Family);
    internal string GetFolderPath(string rootFolder) => Parent is null ? rootFolder
        : IsRun ? Path.Combine(Parent.GetFolderPath(rootFolder), "runs", PathName) : Path.Combine(Parent.GetFolderPath(rootFolder), PathName);
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
    public WorkspaceTreeNode? Parent { get; private set; }
    public ObservableCollection<WorkspaceTreeNode> Children { get; } = [];
    public string Name
    {
        get => name;
        set => NameInput = value;
    }
    // Keep rejected input editable while the tree and saved definition retain the last accepted value.
    public string NameInput
    {
        get => nameInput;
        set
        {
            SetProperty(ref nameInput, value);
            var conflict = Collision(value, folder: false);
            // Rebinding an existing display name must not relocate an imported
            // folder or an older definition with a different folder name.
            if (conflict.Length == 0 && FolderFollowsName && !string.Equals(name, value, StringComparison.Ordinal))
                conflict = Collision(value, folder: true);
            SetNameError(conflict);
            if (conflict.Length > 0) return;
            if (!SetProperty(ref name, value, nameof(Name))) return;
            if (FolderFollowsName)
            {
                SetProperty(ref folderName, value, nameof(FolderName));
                SetProperty(ref folderNameInput, value, nameof(FolderNameInput));
                SetFolderNameError("");
                MarkMoved();
            }
            foreach (var child in Children) child.RefreshInheritedSubtree();
            Changed?.Invoke();
        }
    }
    public string FolderName
    {
        get => folderName;
        set => FolderNameInput = value;
    }
    public string FolderNameInput
    {
        get => folderNameInput;
        set
        {
            if (IsRun) return;
            SetProperty(ref folderNameInput, value);
            var conflict = Collision(value, folder: true);
            SetFolderNameError(conflict);
            if (conflict.Length > 0) return;
            // Binding refreshes can write the current value back; they are not a manual location edit.
            if (string.Equals(folderName, value, StringComparison.Ordinal)) return;
            if (SetProperty(ref folderName, value, nameof(FolderName)))
            {
                if (FolderFollowsName) MarkMoved();
                Changed?.Invoke();
            }
        }
    }
    public bool FolderFollowsName => Kind is WorkspaceEntityKind.Module or WorkspaceEntityKind.Project;
    public string FolderNameHint => IsWorkspace ? "The workspace folder name follows the selected root location."
        : "The folder follows the name. Rename this module or project to change its path, then Save workspace to move the folder and all contents.";
    public string NameError => nameError;
    public string FolderNameError => folderNameError;
    internal string EditError => nameError.Length > 0 ? $"{KindLabel} ‘{Name}’ — Name: {nameError}" : folderNameError.Length > 0 ? $"{KindLabel} ‘{Name}’ — Folder name: {folderNameError}"
        : Editor?.ValidationError is { Length: > 0 } error ? $"{KindLabel} '{Name}': {error}"
        : Children.Select(child => child.EditError).FirstOrDefault(message => message.Length > 0) ?? "";
    private void SetNameError(string value)
    {
        if (SetProperty(ref nameError, value, nameof(NameError))) ValidationChanged?.Invoke();
    }
    private void SetFolderNameError(string value)
    {
        if (SetProperty(ref folderNameError, value, nameof(FolderNameError))) ValidationChanged?.Invoke();
    }
    private string Collision(string value, bool folder)
    {
        try { if (folder) Sortopia.Runs.FolderName.validate(value); else EntityName.validate(value); }
        catch (ArgumentException exception)
        {
            return folder ? "Choose a folder name without reserved characters, trailing dots/spaces, or a reserved device name. " + exception.Message.Split(" (Parameter", StringSplitOptions.None)[0]
                : "Enter a name without leading/trailing spaces or control characters; the name cannot be blank.";
        }
        return Parent?.ChildCollision(Kind, value, folder, this) ?? "";
    }
    private string ChildCollision(WorkspaceEntityKind kind, string value, bool folder, WorkspaceTreeNode? excluded = null)
    {
        if (folder && kind == WorkspaceEntityKind.Run) return "";
        var saved = savedChildren.FirstOrDefault(child => child.Node != excluded && (folder ? !child.Node.IsRun : child.Node.Kind == kind)
            && string.Equals(folder ? child.FolderName : child.Name, value, StringComparison.OrdinalIgnoreCase));
        var current = Children.FirstOrDefault(child => child != excluded && (folder ? !child.IsRun : child.Kind == kind)
            && string.Equals(folder ? child.FolderName : child.Name, value, StringComparison.OrdinalIgnoreCase));
        if (saved is null && current is null) return "";
        return $"Duplicate {(folder ? "folder name among module and project children" : "name in " + kind.ToString().ToLowerInvariant() + "s")}: '{value}' is already used by '{saved?.Name ?? current!.Name}'"
            + (saved is not null ? " in the saved workspace. Choose another value." : " in this collection. Choose another value.");
    }
    internal void AddExistingChild(WorkspaceTreeNode child)
    {
        ValidateChildPlacement(child);
        Children.Add(child);
    }
    internal void ValidateChildPlacement(WorkspaceTreeNode child)
    {
        if (child.IsRun && child.savedParentProjectId is { } parentId && parentId != Id)
            throw new ArgumentException("A saved run keeps its parent project. Create a new run in the target project instead.");
        for (var ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
            if (ReferenceEquals(ancestor, child)) throw new ArgumentException("An entity cannot be pasted into itself or one of its descendants.");
        if (!CanContain(child)) throw new ArgumentException($"A {KindLabel.ToLowerInvariant()} cannot contain a {child.KindLabel.ToLowerInvariant()}.");
        var depth = 1;
        for (var ancestor = this; ancestor.Parent is not null; ancestor = ancestor.Parent) depth++;
        void CheckSubtree(WorkspaceTreeNode node, int level)
        {
            CheckDepth(level);
            foreach (var descendant in node.Children) CheckSubtree(descendant, level + 1);
        }
        CheckSubtree(child, depth);
        var conflict = ChildCollision(child.Kind, child.Name, folder: false, excluded: child);
        if (conflict.Length == 0) conflict = ChildCollision(child.Kind, child.FolderName, folder: true, excluded: child);
        if (conflict.Length > 0) throw new ArgumentException(conflict);
    }
    internal bool HasChildIdentity(WorkspaceEntityKind kind, string value) =>
        Children.Any(child => child.Kind == kind && string.Equals(child.Name, value, StringComparison.OrdinalIgnoreCase)
            || kind != WorkspaceEntityKind.Run && !child.IsRun && string.Equals(child.FolderName, value, StringComparison.OrdinalIgnoreCase))
        || savedChildren.Any(child => child.Node.Kind == kind && string.Equals(child.Name, value, StringComparison.OrdinalIgnoreCase)
            || kind != WorkspaceEntityKind.Run && !child.Node.IsRun && string.Equals(child.FolderName, value, StringComparison.OrdinalIgnoreCase));
    private void RememberSavedChildren() =>
        savedChildren = Children.Select(child => new SavedChild(child, child.Name, child.FolderName)).ToArray();

    internal static WorkspaceTreeNode FromWorkspace(workspace value)
    {
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Workspace, value.WorkspaceName, value.FolderName, value.Id);
        foreach (var child in value.Modules) node.Children.Add(FromModule(child));
        node.RememberSavedChildren();
        return node;
    }
    internal static WorkspaceTreeNode FromModule(WorkspaceModule value)
    {
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Module, value.ModuleName, value.FolderName, value.Id, value.Kind);
        node.Editor!.LoadModuleOverrides(ModuleOverridesDto.fromDomain(value.LocalOverrides));
        foreach (var child in value.Modules) node.Children.Add(FromModule(child));
        foreach (var child in value.Projects) node.Children.Add(FromProject(child));
        node.RememberSavedChildren();
        return node;
    }
    internal static WorkspaceTreeNode FromProject(project value)
    {
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Project, value.ProjectName, value.FolderName, value.Id, value.Kind);
        node.Editor!.LoadOverrides(OverridesDto.fromDomain(value.LocalOverrides));
        foreach (var child in value.Runs)
        {
            var runNode = new WorkspaceTreeNode(WorkspaceEntityKind.Run, child.RunName, id: child.Id, family: value.Kind);
            runNode.Editor!.LoadRun(RunDto.fromDomain(child));
            runNode.savedParentProjectId = child.ParentProjectId;
            node.Children.Add(runNode);
        }
        if (value.IsSealed) { node.sealedProject = project.withRuns(value, []); node.Editor.Freeze(SettingsDto.fromDomain(value.Configuration)); }
        node.RememberSavedChildren();
        return node;
    }
    internal workspace ToWorkspace()
    {
        ValidateChildren();
        return workspace.create(Id, Name, FolderName, Children.Select(child => child.ToModule(1)).ToArray());
    }
    private WorkspaceModule ToModule(int depth)
    {
        CheckDepth(depth);
        ValidateChildren();
        return WorkspaceModule.createInherited(Id, Name, FolderName, ModuleDefaults.fromProject(ParentConfiguration()), ModuleOverridesDto.toDomain(Family, Editor!.ToModuleOverrides()), Children.Where(child => child.Kind == WorkspaceEntityKind.Module).Select(child => child.ToModule(depth + 1)).ToArray(),
            Children.Where(child => child.Kind == WorkspaceEntityKind.Project).Select(child => child.ToProject(depth + 1)).ToArray());
    }
    private project ReadProject()
    {
        if (sealedProject is not null) return project.restore(Id, Name, FolderName, sealedProject.Configuration, sealedProject.LocalOverrides, true, []);
        return project.createInherited(Id, Name, FolderName, ParentConfiguration(), OverridesDto.toDomain(Family, Editor!.ToLocalOverrides()), []);
    }
    private projectConfiguration ParentConfiguration() => Parent?.Editor is { } editor
        ? SettingsDto.toDomain(Family, editor.ToProjectSettings()) : Defaults.configuration(IsBranch);
    private project ToProject(int depth)
    {
        CheckDepth(depth);
        ValidateChildren();
        var parent = ReadProject();
        return project.withRuns(parent, Children.Select(child => child.ToRun(parent)).ToArray());
    }
    private run ToRun(project parent)
    {
        ValidateChildren();
        if (savedParentProjectId is { } parentId && parentId != parent.Id) throw new ArgumentException("A saved run cannot change its parent project.");
        return RunDto.toDomain(parent, Editor!.ToRunInputs(Id, Name, parent.Id));
    }
    private void ValidateChildren()
    {
        if (nameError.Length > 0 || folderNameError.Length > 0) throw new ArgumentException(nameError.Length > 0 ? nameError : folderNameError);
        EntityName.validate(Name);
        if (Children.Any(child => !CanContain(child))) throw new ArgumentException($"Invalid child type inside {KindLabel} '{Name}'.");
    }
    private static void CheckDepth(int depth)
    { if (depth > 64) throw new ArgumentException("Workspace nesting must not exceed 64 levels."); }
}
