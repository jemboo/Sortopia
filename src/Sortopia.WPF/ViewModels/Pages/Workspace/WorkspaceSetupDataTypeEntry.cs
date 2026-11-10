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
    public bool CanEditContract { get; private set; } = true;
    public bool IsContractFixed => !CanEditContract;
    public bool RequiresSource { get; private set; } = true;
    public bool GeneratesData => !RequiresSource;
    private bool allowUnassignedSource;
    private string[]? requiredParameters;
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
    public IEnumerable<WorkspaceSourceProject> SourceProjects => RequiresSource ? sources().Where(p => Matches(p) && SupportsQuery(p)) : [];
    public WorkspaceSourceProject? Source => sources().FirstOrDefault(p => p.Id == SourceProjectId);
    public WorkspaceConfigurationViewModel? SourceEditor => Source?.Editor;
    public string SourceLabel => !RequiresSource ? "Generated from parameters; no source project needed" : Source?.Label ?? "Choose a source project";
    public string QueryKeysSummary => QueryKeys.Count == 0 ? "No setup query keys" : string.Join(", ", QueryKeys.Select(k => k.Text));
    public string SourceQueryHint => !RequiresSource ? "Generated from parameters; no source project needed."
        : Source is { } p ? $"Source results query keys: {string.Join(", ", p.Settings.resultsDataTypes?.FirstOrDefault(output => output.dataType.kind == DataType.Kind && output.dataType.value == DataType.Text)?.queryParameterKeys ?? p.Settings.resultsQueryParameterKeys)}"
        : !SourceProjects.Any() ? "No compatible source projects. A source must provide this exact data type and text, with results query keys accepted by the setup function."
        : allowUnassignedSource ? "Choose a shared source here, or let each descendant project choose its source. Only compatible projects are listed." : "Choose a project providing this exact data type and text. Only projects with compatible results query keys are listed.";
    public ObservableCollection<WorkspaceTextEntry> QueryKeys { get; } = [];
    public RelayCommand AddQueryKeyCommand { get; }
    public RelayCommand RemoveCommand { get; }
    private bool Matches(WorkspaceSourceProject p) => p.Settings.resultsSortopiaDataTypes.Any(t => t.kind == DataType.Kind && t.value == DataType.Text);
    private bool SupportsQuery(WorkspaceSourceProject p) => requiredParameters is null ||
        (p.Settings.resultsDataTypes?.FirstOrDefault(output => output.dataType.kind == DataType.Kind && output.dataType.value == DataType.Text)?.queryParameterKeys
            ?? p.Settings.resultsQueryParameterKeys ?? []).All(key => requiredParameters.Contains(key, StringComparer.Ordinal));
    public string Error => DataType.Error.Length > 0 ? DataType.Error
        : !RequiresSource || allowUnassignedSource && SourceProjectId == Guid.Empty ? ""
        : SourceProjectId == Guid.Empty ? "Choose a source project."
        : Source is not { } p ? "The source project is missing from this workspace."
        : !Matches(p) ? "The source must provide this exact data type, including its free-form text."
        : !SupportsQuery(p) ? "The source requires query parameters that this setup function does not accept. Choose a compatible source project."
        : QueryKeys.Any(k => string.IsNullOrWhiteSpace(k.Text)) ? "Enter each setup query key or remove its row."
        : QueryKeys.Select(k => k.Text).Distinct(StringComparer.Ordinal).Count() != QueryKeys.Count ? "Setup query keys must be unique within this entry." : "";
    private void AddKey(string key) => QueryKeys.Add(new(key, row => QueryKeys.Remove(row), suggestedQueryKeys));
    private void Edited() { RefreshSources(); NotifyChanged(); }
    internal void RefreshSources()
    {
        foreach (var key in QueryKeys) key.RefreshKeyChoices();
        OnPropertyChanged("");
    }
    internal void SetContract(bool fixedContract, bool requiresSource, bool allowUnassigned, string[] keys, string[]? requiredParameters = null)
    {
        CanEditContract = !fixedContract; RequiresSource = requiresSource; allowUnassignedSource = allowUnassigned;
        this.requiredParameters = requiredParameters;
        if (!QueryKeys.Select(key => key.Text).SequenceEqual(keys))
        {
            QueryKeys.Clear(); foreach (var key in keys) AddKey(key);
        }
        RefreshSources();
    }
    internal setupDataTypeConfigDto ToDto() => new() { dataType = DataType.ToDto(), sourceProjectId = SourceProjectId, queryParameterKeys = QueryKeys.Select(k => k.Text).ToArray() };
}
