using System.Collections.ObjectModel;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceSourceProject(Guid Id, string Name, bool IsSealed, projectSettingsDto Settings, WorkspaceConfigurationViewModel Editor,
    bool HasPendingRuns = false, string Location = "")
{
    public string Label => $"{Name}{(Location.Length == 0 ? "" : " · " + Location)} · {(IsSealed ? "sealed" : HasPendingRuns ? "seals on save" : "draft")}";
}

/// <summary>One setup output, its matching source project, and its own query keys.</summary>
public sealed class WorkspaceSetupDataTypeEntry : WorkspaceFormItem
{
    private readonly Func<IEnumerable<WorkspaceSourceProject>> sources;
    private readonly Func<IEnumerable<string>> suggestedQueryKeys;
    private Guid sourceProjectId;
    public WorkspaceSetupDataTypeEntry(setupDataTypeConfigDto value, Func<IEnumerable<WorkspaceSourceProject>> sources, Action<WorkspaceSetupDataTypeEntry> remove,
        Func<IEnumerable<string>>? suggestedQueryKeys = null)
    {
        this.sources = sources;
        this.suggestedQueryKeys = suggestedQueryKeys ?? (() => []);
        sourceProjectId = value.sourceProjectId;
        DataType = new(value.dataType.kind, value.dataType.value, _ => remove(this));
        DataType.Changed += Edited;
        Watch(QueryKeys, Edited);
        foreach (var key in value.queryParameterKeys) AddKey(key);
        AddQueryKeyCommand = new RelayCommand(() => AddKey(""));
        RemoveCommand = new RelayCommand(() => remove(this));
    }
    public WorkspaceDataTypeEntry DataType { get; }
    public Guid SourceProjectId { get => sourceProjectId; set { if (SetProperty(ref sourceProjectId, value)) Edited(); } }
    public IEnumerable<WorkspaceSourceProject> SourceProjects => sources().Where(Matches);
    public WorkspaceSourceProject? Source => sources().FirstOrDefault(p => p.Id == SourceProjectId);
    public WorkspaceConfigurationViewModel? SourceEditor => Source?.Editor;
    public string SourceLabel => Source?.Label ?? $"Missing source: {SourceProjectId}";
    public string QueryKeysSummary => QueryKeys.Count == 0 ? "No setup query keys" : string.Join(", ", QueryKeys.Select(k => k.Text));
    public string SourceQueryHint => Source is { } p ? $"Source results query keys: {(p.Settings.resultsQueryParameterKeys.Length == 0 ? "none" : string.Join(", ", p.Settings.resultsQueryParameterKeys))}" : "Choose a project providing this exact data type and text.";
    public ObservableCollection<WorkspaceTextEntry> QueryKeys { get; } = [];
    public RelayCommand AddQueryKeyCommand { get; }
    public RelayCommand RemoveCommand { get; }
    private bool Matches(WorkspaceSourceProject p) => p.Settings.resultsSortopiaDataTypes.Any(t => t.kind == DataType.Kind && t.value == DataType.Text);
    public string Error => DataType.Error.Length > 0 ? DataType.Error
        : SourceProjectId == Guid.Empty ? "Choose a source project."
        : Source is not { } p ? "The source project is missing from this workspace."
        : !Matches(p) ? "The source must provide this exact data type, including its free-form text."
        : QueryKeys.Any(k => string.IsNullOrWhiteSpace(k.Text)) ? "Enter each setup query key or remove its row."
        : QueryKeys.Select(k => k.Text).Distinct(StringComparer.Ordinal).Count() != QueryKeys.Count ? "Setup query keys must be unique within this entry." : "";
    private void AddKey(string key) => QueryKeys.Add(new(key, row => QueryKeys.Remove(row), suggestedQueryKeys));
    private void Edited() { RefreshSources(); NotifyChanged(); }
    internal void RefreshSources()
    {
        foreach (var key in QueryKeys) key.RefreshKeyChoices();
        OnPropertyChanged("");
    }
    internal setupDataTypeConfigDto ToDto() => new() { dataType = DataType.ToDto(), sourceProjectId = SourceProjectId, queryParameterKeys = QueryKeys.Select(k => k.Text).ToArray() };
}
