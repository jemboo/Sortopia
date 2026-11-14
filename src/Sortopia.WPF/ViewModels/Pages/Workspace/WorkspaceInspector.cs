using System.Collections.ObjectModel;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceItemSnapshot(string Key, string Group, string Title, string Value, string Status, string Error = "",
    WorkspaceConfigurationViewModel? Source = null);

public sealed class WorkspaceInspectorItem : ViewModelBase
{
    private WorkspaceItemSnapshot item;
    public WorkspaceInspectorItem(WorkspaceItemSnapshot item) => this.item = item;
    public string Key => item.Key;
    public string Group => item.Group;
    public string Title => item.Title;
    public string Value => string.IsNullOrWhiteSpace(item.Value) ? "Not set" : item.Value;
    public string Status => HasError ? "Needs attention" : HasSource ? "Inherited" : item.Status;
    public string Error => item.Error;
    public bool HasError => Error.Length > 0;
    public WorkspaceConfigurationViewModel? Source => item.Source;
    public bool HasSource => Source is not null;
    public string SourceName => Source?.Name ?? "";
    public bool IsActive { get; private set; }
    public bool StartsGroup { get; private set; }
    internal void Update(WorkspaceItemSnapshot value, bool active, bool startsGroup)
    { item = value; IsActive = active; StartsGroup = startsGroup; OnPropertyChanged(""); }
}

public sealed partial class WorkspacePageViewModel
{
    private string activeItemKey = "name";
    private readonly Dictionary<Guid, string> rememberedItems = [];
    public ObservableCollection<WorkspaceInspectorItem> InspectorItems { get; } = [];
    public RelayCommand SelectConfigurationItemCommand { get; private set; } = null!;
    public RelayCommand SelectInspectorSourceCommand { get; private set; } = null!;
    public string ActiveItemKey => activeItemKey;
    public string ActiveItemTitle => InspectorItems.FirstOrDefault(item => item.Key == activeItemKey) is { } item
        ? item.Group == "Functions" ? Selected?.Editor?.FunctionsTitle ?? "Functions"
            : item.Group is "Results" or "Setup" && !item.Title.StartsWith(item.Group, StringComparison.OrdinalIgnoreCase) ? $"{item.Group} · {item.Title}" : item.Title
        : "Configuration item";
    public bool ShowNameEditor => activeItemKey == "name";
    public bool ShowLocationEditor => activeItemKey == "location";
    public bool ShowExecutionEditor => activeItemKey == "execution" && Selected?.IsRun == true;
    public bool ShowConfigurationItemEditor => Selected?.HasConfiguration == true && !ShowNameEditor && !ShowLocationEditor && !ShowExecutionEditor;
    public bool ShowParentDefaultsAction => ShowConfigurationItemEditor && Selected?.HasParentDefaults == true;
    public bool HasSelectedEntity => Selected is not null;
    private void InitializeInspector()
    {
        SelectConfigurationItemCommand = new RelayCommand(value => SelectConfigurationItem(value is WorkspaceInspectorItem item ? item.Key : value as string ?? "name"));
        SelectInspectorSourceCommand = new RelayCommand(value =>
        {
            if (value is not WorkspaceInspectorItem { Source: not null } item) return;
            SelectValueSourceCommand.Execute(item.Source);
            SelectConfigurationItem(item.Key);
        });
    }
    public void SelectConfigurationItem(string key)
    {
        if (!InspectorItems.Any(item => item.Key == key)) return;
        activeItemKey = key;
        if (Selected is not null) rememberedItems[Selected.Id] = key;
        RefreshInspector();
    }
    private void InspectorSelectionChanged()
    {
        activeItemKey = Selected is not null && rememberedItems.TryGetValue(Selected.Id, out var key) ? key : DefaultInspectorItem;
        RefreshInspector();
    }
    private string DefaultInspectorItem => Selected?.HasConfiguration == true ? Selected.IsBranch ? "setupFunction" : "resultsFunction" : "name";
    private IEnumerable<WorkspaceItemSnapshot> InspectorSnapshots()
    {
        if (Selected is not { } node) yield break;
        if (node.Editor is { } module)
            foreach (var item in module.InspectorSnapshots().Where(item => item.Group == "Functions")) yield return item;
        yield return new("name", "Identity", "Name", node.NameInput, "Local", node.NameError);
        yield return new("location", "Identity", "Location", SelectedFolderPath, node.DiskStatus.Length > 0 ? node.DiskStatus : "Saved", node.FolderNameError);
        if (node.Editor is { } editor)
            foreach (var item in editor.InspectorSnapshots().Where(item => item.Group != "Functions")) yield return item;
        if (node.IsRun)
            yield return new("execution", "Run parameters", "Generate run parameters", SelectedExecution?.Summary ?? "Not generated yet", SelectedExecution?.IsRunning == true ? "Generating" : GenerationHint.Length > 0 ? "Not ready" : "Ready", SelectedExecution?.Error ?? "");
    }
    private void RefreshInspector()
    {
        var snapshots = InspectorSnapshots().ToArray();
        if (!snapshots.Any(item => item.Key == activeItemKey)) activeItemKey = DefaultInspectorItem;
        if (!InspectorItems.Select(item => item.Key).SequenceEqual(snapshots.Select(item => item.Key)))
        {
            var existing = InspectorItems.ToDictionary(item => item.Key);
            InspectorItems.Clear();
            foreach (var item in snapshots) InspectorItems.Add(existing.GetValueOrDefault(item.Key) ?? new(item));
        }
        for (var index = 0; index < snapshots.Length; index++)
            InspectorItems[index].Update(snapshots[index], snapshots[index].Key == activeItemKey, index == 0 || snapshots[index - 1].Group != snapshots[index].Group);
        OnPropertyChanged(nameof(ActiveItemKey)); OnPropertyChanged(nameof(ActiveItemTitle));
        OnPropertyChanged(nameof(ShowNameEditor)); OnPropertyChanged(nameof(ShowLocationEditor)); OnPropertyChanged(nameof(ShowExecutionEditor));
        OnPropertyChanged(nameof(ShowConfigurationItemEditor)); OnPropertyChanged(nameof(HasSelectedEntity));
        OnPropertyChanged(nameof(ShowParentDefaultsAction));
    }
    private void InspectSaveIssue(WorkspaceSaveIssue issue)
    {
        var key = InspectorItems.FirstOrDefault(item => item.Error == issue.Message)?.Key ?? issue.Section switch
        {
            "Name" => "name", "Location" => "location", "Description" => "description",
            "Results settings" => "resultsFunction", "Setup settings" => "setupFunction",
            "Results parameter defaults" => "resultsDefaults", "Setup parameter defaults" => "setupDefaults",
            "Results parameter spans" => "resultsSpans", "Setup parameter spans" => "setupSpans", _ => "name"
        };
        SelectConfigurationItem(InspectorItems.Any(item => item.Key == key) ? key : DefaultInspectorItem);
    }
}
