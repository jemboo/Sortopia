using Sortopia.Workspaces.Serialization;
using Sortopia.Functions;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceSourceProject(Guid Id, string Name, bool IsSealed, projectSettingsDto Settings, WorkspaceConfigurationViewModel Editor,
    bool HasPendingRuns = false, string Location = "")
{
    public string Label => $"{Name}{(Location.Length == 0 ? "" : " · " + Location)} · {(IsSealed ? "sealed" : "draft · generate source results first")}";
}

public sealed record WorkspaceSourceProjectChoice(WorkspaceSourceProject Project, string IncompatibilityReason, string QuerySummary)
{
    public Guid Id => Project.Id;
    public string Label => Project.Label;
    public bool IsCompatible => IncompatibilityReason.Length == 0;
    public string CompatibilityText => IsCompatible ? QuerySummary : "Unavailable · " + IncompatibilityReason;
}

/// <summary>One setup output, its matching source project, and its own query keys.</summary>
public sealed class WorkspaceSetupDataTypeEntry : WorkspaceFormItem
{
    private readonly Func<IEnumerable<WorkspaceSourceProject>> sources;
    private Guid sourceProjectId;
    public bool RequiresSource { get; private set; } = true;
    public bool GeneratesData => !RequiresSource;
    private bool allowUnassignedSource;
    private string[]? requiredParameters;
    private bool parametersFromSources;
    public WorkspaceSetupDataTypeEntry(setupDataTypeConfigDto value, Func<IEnumerable<WorkspaceSourceProject>> sources,
        WorkspaceSetupSourceOverride? sourceOverride = null)
    {
        this.sources = sources;
        SourceOverride = sourceOverride;
        sourceProjectId = value.sourceProjectId;
        DataType = new(value.dataType.kind, value.dataType.value);
        QueryKeys = value.queryParameterKeys.ToArray();
    }
    public WorkspaceOutputType DataType { get; }
    public WorkspaceSetupSourceOverride? SourceOverride { get; }
    public WorkspaceConfigurationViewModel? SelectionOwner { get; init; }
    public string SelectionOrigin => SelectionOwner is { } owner ? $"Source selection defined on {owner.Name}" : "No source selection defined";
    public Guid SourceProjectId
    {
        get => SourceOverride?.Value ?? sourceProjectId;
        set { if (SourceOverride is { } state) state.Value = value; else if (SetProperty(ref sourceProjectId, value)) Edited(); }
    }
    public IEnumerable<WorkspaceSourceProjectChoice> SourceProjects => RequiresSource
        ? sources().Select(p => new WorkspaceSourceProjectChoice(p, Incompatibility(p), QuerySummary(p)))
            .OrderByDescending(p => p.IsCompatible).ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase) : [];
    public WorkspaceSourceProject? Source => sources().FirstOrDefault(p => p.Id == SourceProjectId);
    public WorkspaceConfigurationViewModel? SourceEditor => Source?.Editor;
    public string SourceLabel => !RequiresSource ? "Generated from parameters; no source project needed" : Source?.Label ?? "Choose a source project";
    public string QueryKeysSummary => QueryKeys.Length == 0 ? "No setup query keys" : string.Join(", ", QueryKeys);
    public string SourceQueryHint => !RequiresSource ? "Generated from parameters; no source project needed."
        : Source is { } p ? QuerySummary(p)
        : !SourceProjects.Any(p => p.IsCompatible) ? "No compatible source projects. Open the list to see why each project is unavailable."
        : allowUnassignedSource ? "Choose a shared source here, or let descendant projects choose. Unavailable projects are listed with the reason."
        : "Choose a compatible project. Unavailable projects are listed with the reason.";
    public string[] QueryKeys { get; private set; }
    private sourceQueryContract SourceContract(WorkspaceSourceProject p) => WorkspaceSourceContracts.Resolve(p, DataType.Kind, DataType.Text);
    private string QuerySummary(WorkspaceSourceProject p) => SourceContract(p).QueryKeys is { Length: > 0 } keys
        ? $"Source results query keys: {string.Join(", ", keys)}" : "Source has no results query keys.";
    private string Incompatibility(WorkspaceSourceProject p)
    {
        if (!WorkspaceSourceContracts.HasOutput(p, DataType.Kind, DataType.Text))
        {
            static string TypeName(sortopiaDataTypeDto type) => $"{type.kind} \"{type.value}\"";
            var outputs = string.Join(", ", p.Settings.resultsDataTypes.Select(output => TypeName(output.dataType)));
            return $"Requires the exact data type {DataType.Kind} \"{DataType.Text}\". Project produces {(outputs.Length == 0 ? "no outputs" : outputs)}.";
        }
        var contract = SourceContract(p);
        if (parametersFromSources)
        {
            var unknown = contract.UnknownKeys;
            if (unknown.Length > 0) return $"Unknown source query parameters: {string.Join(", ", unknown)}.";
            var outside = SourceContracts.missingQueryKeys(QueryKeys, contract).Distinct(StringComparer.Ordinal).ToArray();
            return outside.Length == 0 ? "" : $"Setup query keys must be a subset of this source's results query keys. Keys absent from this source: {string.Join(", ", outside)}.";
        }
        var missing = requiredParameters is null ? [] : SourceContracts.unacceptedQueryKeys(requiredParameters, contract).ToArray();
        return missing.Length == 0 ? "" : $"Source query parameters not accepted by this setup function: {string.Join(", ", missing)}. Accepted parameters: {string.Join(", ", requiredParameters!)}.";
    }
    public string Error => !RequiresSource || allowUnassignedSource && SourceProjectId == Guid.Empty ? ""
        : SourceProjectId == Guid.Empty ? "Choose a source project."
        : Source is not { } p ? "The source project is missing from this workspace."
        : Incompatibility(p);
    private void Edited() { RefreshSources(); NotifyChanged(); }
    internal void RefreshSources()
    {
        OnPropertyChanged("");
    }
    internal void SetContract(bool requiresSource, bool allowUnassigned, string[] keys, string[]? requiredParameters = null, bool parametersFromSources = false)
    {
        RequiresSource = requiresSource; allowUnassignedSource = allowUnassigned;
        this.requiredParameters = requiredParameters;
        this.parametersFromSources = parametersFromSources;
        QueryKeys = keys.ToArray();
        RefreshSources();
    }
    internal setupDataTypeConfigDto ToDto() => new() { dataType = DataType.ToDto(), sourceProjectId = SourceProjectId, queryParameterKeys = QueryKeys.ToArray() };
}
