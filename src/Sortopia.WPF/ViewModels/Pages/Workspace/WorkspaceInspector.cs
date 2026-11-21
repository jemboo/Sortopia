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
    public RelayCommand EditProjectQueryProjectionsCommand { get; private set; } = null!;
    public RelayCommand OpenFunctionParameterCommand { get; private set; } = null!;
    public string ActiveItemKey => activeItemKey;
    public string ActiveItemTitle => InspectorItems.FirstOrDefault(item => item.Key == activeItemKey) is { } item
        ? item.Group is "Results" or "Setup" && !item.Title.StartsWith(item.Group, StringComparison.OrdinalIgnoreCase) ? $"{item.Group} · {item.Title}" : item.Title
        : "Configuration item";
    public bool ShowNameEditor => activeItemKey == "name";
    public bool ShowLocationEditor => activeItemKey == "location";
    public bool ShowExecutionEditor => activeItemKey == "execution" && Selected?.IsRun == true;
    public bool ShowConfigurationItemEditor => Selected?.HasConfiguration == true && !ShowNameEditor && !ShowLocationEditor && !ShowExecutionEditor;
    public bool ShowParentDefaultsAction => ShowConfigurationItemEditor && activeItemKey != "evolutionReporting" && Selected?.HasParentDefaults == true;
    public bool HasSelectedEntity => Selected is not null;
    private void InitializeInspector()
    {
        OpenFunctionParameterCommand = new RelayCommand(value =>
        {
            if (value is not WorkspaceFunctionParameter { CanNavigate: true } parameter) return;
            var node = Roots.SelectMany(root => root.DescendantsAndSelf())
                .FirstOrDefault(node => ReferenceEquals(node.Editor, parameter.TargetEditor));
            if (node is null) return;
            if (parameter.IsSourceInput)
            {
                if (CanModifyWorkspace && node.Editor!.IsEditable)
                    node.Editor.PrepareSetupSource(parameter.SourceKind!, parameter.SourceText);
            }
            else if (parameter.Supply == WorkspaceParameterSupply.None
                && (!CanModifyWorkspace || !node.Editor!.PrepareMissingFunctionParameter(parameter.Name, parameter.IsSetup))) return;
            for (var parent = node.Parent; parent is not null; parent = parent.Parent) parent.IsExpanded = true;
            Selected = node;
            SelectConfigurationItem(parameter.ItemKey);
        }, () => CanEdit);
        EditProjectQueryProjectionsCommand = new RelayCommand(value =>
        {
            if (value is not string key || key is not ("resultsTypes" or "setupOutputs")) return;
            if (Selected!.IsRun) Selected = Selected.Parent;
            SelectConfigurationItem(key);
        }, () => CanEdit && (Selected?.IsProject == true || Selected is { IsRun: true, Parent.IsProject: true }));
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
        if (ShowExecutionEditor) _ = RefreshRunParameterFilesAsync();
    }
    private void InspectorSelectionChanged()
    {
        activeItemKey = Selected is not null && rememberedItems.TryGetValue(Selected.Id, out var key) ? key : DefaultInspectorItem;
        RefreshInspector();
        if (ShowExecutionEditor) _ = RefreshRunParameterFilesAsync();
    }
    private string DefaultInspectorItem => Selected?.HasConfiguration == true ? Selected.IsBranch ? "setupFunction" : "resultsFunction" : "name";
    private IEnumerable<WorkspaceItemSnapshot> InspectorSnapshots()
    {
        if (Selected is not { } node) yield break;
        var configuration = node.Editor?.InspectorSnapshots().ToArray() ?? [];
        string[] firstGroups = node.IsBranch ? ["Setup", "Results", "Reporting"] : ["Functions", "Reporting"];
        foreach (var group in firstGroups)
            foreach (var item in configuration.Where(item => item.Group == group)) yield return item;
        yield return new("name", "Identity", "Name", node.NameInput, "Local", node.NameError);
        yield return new("location", "Identity", "Location", SelectedFolderPath, node.DiskStatus.Length > 0 ? node.DiskStatus : "Saved", node.NameError);
        foreach (var item in configuration.Where(item => !firstGroups.Contains(item.Group))) yield return item;
        if (node.IsRun)
            yield return new("execution", "Files", "Run parameter files", SelectedExecution!.Files.HasLoaded ? SelectedExecution.Files.Summary : RunParameterPreviewSummary,
                SelectedExecution.IsRunning ? "Generating" : SelectedExecution.Files.HasFiles ? "Saved files" : "Preview and generation", SelectedExecution.Files.Error);
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
            "Results parameter spans" => "resultsSpans", "Setup parameter spans" => "setupSpans",
            "Evolution reporting" => "evolutionReporting", _ => "name"
        };
        SelectConfigurationItem(InspectorItems.Any(item => item.Key == key) ? key : DefaultInspectorItem);
    }
}
