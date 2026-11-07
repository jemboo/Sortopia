using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using WorkspaceModule = Sortopia.Workspaces.@module;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public enum WorkspaceEntityKind { Workspace, Module, Project, Run }

public sealed class WorkspaceTreeNode : ViewModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private string name;
    private string folderName;
    private string nameInput;
    private string folderNameInput;
    private string nameError = "";
    private string folderNameError = "";
    private bool folderFollowsName;
    private sealed record SavedChild(WorkspaceTreeNode Node, string Name, string FolderName);
    private SavedChild[] savedChildren = [];
    private string runConfiguration = "";
    private bool isSelected;
    private string? savedFolderPath;
    private string diskStatus = "Not saved";
    private string diskStatusDetail = "This component has not been saved yet.";
    private bool isMissingOnDisk;
    private bool hasDiskWarning;
    public WorkspaceTreeNode(WorkspaceEntityKind kind, string name, string? folderName = null)
    {
        Kind = kind; this.name = name; this.folderName = folderName ?? name;
        nameInput = name; folderNameInput = this.folderName;
        folderFollowsName = kind != WorkspaceEntityKind.Workspace && folderName is null;
        Children.CollectionChanged += (_, args) =>
        {
            if (args.OldItems is not null) foreach (WorkspaceTreeNode child in args.OldItems) { child.Parent = null; child.Changed -= ChildChanged; child.ValidationChanged -= ChildValidationChanged; }
            if (args.NewItems is not null) foreach (WorkspaceTreeNode child in args.NewItems) { child.Parent = this; child.Changed += ChildChanged; child.ValidationChanged += ChildValidationChanged; }
            Changed?.Invoke();
            ValidationChanged?.Invoke();
        };
        if (kind == WorkspaceEntityKind.Run)
            runConfiguration = JsonSerializer.Serialize(new
            {
                runKind = "root", description = "", resultsDatabaseName = "Results", executeFunctionName = "",
                outputParameterSpans = Array.Empty<object>(), outputDataTypes = Array.Empty<int>(), outputQueryParameterKeys = Array.Empty<string>(),
                setupDatabaseName = "", setupFunctionName = "", setupParameterSpans = Array.Empty<object>(),
                setupOutputDataTypes = Array.Empty<int>(), setupQueryParameterKeys = Array.Empty<object>()
            }, JsonOptions);
    }
    internal event Action? Changed;
    internal event Action? ValidationChanged;
    private void ChildChanged() => Changed?.Invoke();
    private void ChildValidationChanged() => ValidationChanged?.Invoke();
    public WorkspaceEntityKind Kind { get; }
    public string KindLabel => Kind.ToString();
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
    internal int RefreshDiskStatus(string rootFolder, bool pendingCopy = false)
    {
        pendingCopy |= PendingCopy is not null;
        var label = "";
        var detail = "";
        var missing = false;
        var warning = false;
        try
        {
            var path = GetFolderPath(rootFolder);
            var previouslySavedHere = string.Equals(savedFolderPath, path, StringComparison.OrdinalIgnoreCase);
            if (pendingCopy) { label = "Pending copy"; detail = $"Saving will copy this component into {path}."; }
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { label = "Disk error"; detail = exception.Message; warning = true; }
        SetProperty(ref diskStatus, label, nameof(DiskStatus));
        SetProperty(ref diskStatusDetail, detail, nameof(DiskStatusDetail));
        SetProperty(ref isMissingOnDisk, missing, nameof(IsMissingOnDisk));
        SetProperty(ref hasDiskWarning, warning || missing, nameof(HasDiskWarning));
        return (missing ? 1 : 0) + Children.Sum(child => child.RefreshDiskStatus(rootFolder, pendingCopy));
    }
    internal WorkspaceFolderCopy? PendingCopy { get; set; }
    public string PendingCopyDescription => PendingCopy is null ? "" : $"Will copy this folder and all contents when saved: {PendingCopy.SourceFolder}";
    internal IEnumerable<WorkspaceFolderCopy> GetPendingCopies(string rootFolder)
    {
        if (PendingCopy is not null) yield return PendingCopy.WithDestination(rootFolder, GetFolderPath(rootFolder));
        foreach (var child in Children)
            foreach (var copy in child.GetPendingCopies(rootFolder)) yield return copy;
    }
    public bool CanContain(WorkspaceEntityKind child) => Kind switch
    {
        WorkspaceEntityKind.Workspace => child == WorkspaceEntityKind.Module,
        WorkspaceEntityKind.Module => child is WorkspaceEntityKind.Module or WorkspaceEntityKind.Project,
        WorkspaceEntityKind.Project => child is WorkspaceEntityKind.Project or WorkspaceEntityKind.Run,
        _ => false
    };
    internal string GetFolderPath(string rootFolder) => Parent is null ? rootFolder
        : Path.Combine(Parent.GetFolderPath(rootFolder), Kind.ToString().ToLowerInvariant() + "s", Sortopia.Runs.FolderName.validate(FolderName));
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
            if (conflict.Length == 0 && folderFollowsName)
                conflict = Collision(value, folder: true);
            SetNameError(conflict);
            if (conflict.Length > 0) return;
            if (!SetProperty(ref name, value, nameof(Name))) return;
            if (folderFollowsName)
            {
                SetProperty(ref folderName, value, nameof(FolderName));
                SetProperty(ref folderNameInput, value, nameof(FolderNameInput));
                SetFolderNameError("");
            }
            Changed?.Invoke();
        }
    }
    public string RunConfiguration
    {
        get => runConfiguration;
        set { if (SetProperty(ref runConfiguration, value)) Changed?.Invoke(); }
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
            SetProperty(ref folderNameInput, value);
            var conflict = Collision(value, folder: true);
            SetFolderNameError(conflict);
            if (conflict.Length > 0) return;
            folderFollowsName = false;
            OnPropertyChanged(nameof(FolderFollowsName));
            OnPropertyChanged(nameof(FolderNameHint));
            if (SetProperty(ref folderName, value, nameof(FolderName))) Changed?.Invoke();
            // A manual folder can resolve a name edit that conflicted only through automatic tracking.
            if (nameError.Length > 0) NameInput = nameInput;
        }
    }
    public bool FolderFollowsName => folderFollowsName;
    public string FolderNameHint => IsWorkspace ? "The workspace folder name follows the selected root location."
        : folderFollowsName ? "Follows the name until saved or edited manually." : "Changing the name keeps this folder location.";
    public string NameError => nameError;
    public string FolderNameError => folderNameError;
    internal string EditError => nameError.Length > 0 ? nameError : folderNameError.Length > 0 ? folderNameError
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
        return Parent?.ChildCollision(Kind, value, folder, this) ?? "";
    }
    private string ChildCollision(WorkspaceEntityKind kind, string value, bool folder, WorkspaceTreeNode? excluded = null)
    {
        var saved = savedChildren.FirstOrDefault(child => child.Node != excluded && child.Node.Kind == kind
            && string.Equals(folder ? child.FolderName : child.Name, value, StringComparison.OrdinalIgnoreCase));
        var current = Children.FirstOrDefault(child => child != excluded && child.Kind == kind
            && string.Equals(folder ? child.FolderName : child.Name, value, StringComparison.OrdinalIgnoreCase));
        if (saved is null && current is null) return "";
        return $"Duplicate {(folder ? "folder name" : "name")} in {kind.ToString().ToLowerInvariant()}s: '{value}' is already used by '{saved?.Name ?? current!.Name}'"
            + (saved is not null ? " in the saved workspace. Choose another value." : " in this collection. Choose another value.");
    }
    internal void AddExistingChild(WorkspaceTreeNode child)
    {
        if (!CanContain(child.Kind)) throw new ArgumentException($"A {KindLabel.ToLowerInvariant()} cannot contain a {child.KindLabel.ToLowerInvariant()}.");
        var depth = 1;
        for (var ancestor = this; ancestor.Parent is not null; ancestor = ancestor.Parent) depth++;
        void CheckSubtree(WorkspaceTreeNode node, int level)
        {
            CheckDepth(level);
            foreach (var descendant in node.Children) CheckSubtree(descendant, level + 1);
        }
        CheckSubtree(child, depth);
        var conflict = ChildCollision(child.Kind, child.Name, folder: false);
        if (conflict.Length == 0) conflict = ChildCollision(child.Kind, child.FolderName, folder: true);
        if (conflict.Length > 0) throw new ArgumentException(conflict);
        Children.Add(child);
    }
    internal bool HasChildIdentity(WorkspaceEntityKind kind, string value) =>
        Children.Any(child => child.Kind == kind && (string.Equals(child.Name, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(child.FolderName, value, StringComparison.OrdinalIgnoreCase)))
        || savedChildren.Any(child => child.Node.Kind == kind && (string.Equals(child.Name, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(child.FolderName, value, StringComparison.OrdinalIgnoreCase)));
    private void RememberSavedChildren() =>
        savedChildren = Children.Select(child => new SavedChild(child, child.Name, child.FolderName)).ToArray();

    internal static WorkspaceTreeNode FromWorkspace(workspace value)
    {
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Workspace, value.WorkspaceName, value.FolderName);
        foreach (var child in value.Modules) node.Children.Add(FromModule(child));
        node.RememberSavedChildren();
        return node;
    }
    internal static WorkspaceTreeNode FromModule(WorkspaceModule value)
    {
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Module, value.ModuleName, value.FolderName);
        foreach (var child in value.Modules) node.Children.Add(FromModule(child));
        foreach (var child in value.Projects) node.Children.Add(FromProject(child));
        node.RememberSavedChildren();
        return node;
    }
    internal static WorkspaceTreeNode FromProject(project value)
    {
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Project, value.ProjectName, value.FolderName);
        foreach (var child in value.Projects) node.Children.Add(FromProject(child));
        foreach (var child in value.Runs)
        {
            var dto = JsonSerializer.SerializeToNode(RunDto.fromDomain(child), JsonOptions)!.AsObject();
            dto.Remove("schemaVersion"); dto.Remove("projectName"); dto.Remove("runName"); dto.Remove("folderName");
            node.Children.Add(new(WorkspaceEntityKind.Run, child.RunName, child.FolderName) { RunConfiguration = dto.ToJsonString(JsonOptions) });
        }
        node.RememberSavedChildren();
        return node;
    }
    internal workspace ToWorkspace()
    {
        ValidateChildren();
        return workspace.create(Name, FolderName, Children.Select(child => child.ToModule(1)).ToArray());
    }
    private WorkspaceModule ToModule(int depth)
    {
        CheckDepth(depth);
        ValidateChildren();
        return WorkspaceModule.create(Name, FolderName, Children.Where(child => child.Kind == WorkspaceEntityKind.Module).Select(child => child.ToModule(depth + 1)).ToArray(),
            Children.Where(child => child.Kind == WorkspaceEntityKind.Project).Select(child => child.ToProject(depth + 1)).ToArray());
    }
    private project ToProject(int depth)
    {
        CheckDepth(depth);
        ValidateChildren();
        return project.create(Name, FolderName, Children.Where(child => child.IsRun).Select(child => child.ToRun(Name)).ToArray(),
            Children.Where(child => child.Kind == WorkspaceEntityKind.Project).Select(child => child.ToProject(depth + 1)).ToArray());
    }
    private run ToRun(string projectName)
    {
        ValidateChildren();
        var json = JsonNode.Parse(RunConfiguration)?.AsObject() ?? throw new ArgumentException("Enter a run configuration object.");
        json["schemaVersion"] = 2; json["projectName"] = projectName; json["runName"] = Name; json["folderName"] = FolderName;
        return RunDto.toDomain(json.Deserialize<runDto>(JsonOptions)!);
    }
    private void ValidateChildren()
    {
        if (nameError.Length > 0 || folderNameError.Length > 0) throw new ArgumentException(nameError.Length > 0 ? nameError : folderNameError);
        EntityName.validate(Name);
        if (Children.Any(child => !CanContain(child.Kind))) throw new ArgumentException($"Invalid child type inside {KindLabel} '{Name}'.");
    }
    private static void CheckDepth(int depth)
    { if (depth > 64) throw new ArgumentException("Workspace nesting must not exceed 64 levels."); }
}
