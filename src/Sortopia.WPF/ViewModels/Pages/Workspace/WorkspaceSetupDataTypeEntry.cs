using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceSourceProject(Guid Id, string Name, bool IsSealed, projectSettingsDto Settings, WorkspaceConfigurationViewModel Editor,
    bool HasPendingRuns = false, string Location = "")
{
    public string Label => $"{Name}{(Location.Length == 0 ? "" : " · " + Location)} · {(IsSealed ? "sealed" : HasPendingRuns ? "seals on save" : "draft")}";
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
    public WorkspaceSetupDataTypeEntry(setupDataTypeConfigDto value, Func<IEnumerable<WorkspaceSourceProject>> sources)
    {
        this.sources = sources;
        sourceProjectId = value.sourceProjectId;
        DataType = new(value.dataType.kind, value.dataType.value);
        QueryKeys = value.queryParameterKeys.ToArray();
    }
    public WorkspaceOutputType DataType { get; }
    public Guid SourceProjectId { get => sourceProjectId; set { if (SetProperty(ref sourceProjectId, value)) Edited(); } }
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
    private bool Matches(WorkspaceSourceProject p) => p.Settings.resultsSortopiaDataTypes.Any(t => t.kind == DataType.Kind && t.value == DataType.Text);
    private string[] SourceKeys(WorkspaceSourceProject p) => p.Settings.resultsDataTypes?
        .FirstOrDefault(output => output.dataType.kind == DataType.Kind && output.dataType.value == DataType.Text)?.queryParameterKeys
        ?? p.Settings.resultsQueryParameterKeys ?? [];
    private string QuerySummary(WorkspaceSourceProject p) => SourceKeys(p) is { Length: > 0 } keys
        ? $"Source results query keys: {string.Join(", ", keys)}" : "Source has no results query keys.";
    private string Incompatibility(WorkspaceSourceProject p)
    {
        if (!Matches(p))
        {
            static string TypeName(sortopiaDataTypeDto type) => $"{type.kind} \"{type.value}\"";
            var outputs = string.Join(", ", p.Settings.resultsSortopiaDataTypes.Select(TypeName));
            return $"Requires the exact data type {DataType.Kind} \"{DataType.Text}\". Project produces {(outputs.Length == 0 ? "no outputs" : outputs)}.";
        }
        var missing = requiredParameters is null ? [] : SourceKeys(p).Except(requiredParameters, StringComparer.Ordinal).ToArray();
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
    internal void SetContract(bool requiresSource, bool allowUnassigned, string[] keys, string[]? requiredParameters = null)
    {
        RequiresSource = requiresSource; allowUnassignedSource = allowUnassigned;
        this.requiredParameters = requiredParameters;
        QueryKeys = keys.ToArray();
        RefreshSources();
    }
    internal setupDataTypeConfigDto ToDto() => new() { dataType = DataType.ToDto(), sourceProjectId = SourceProjectId, queryParameterKeys = QueryKeys.ToArray() };
}
