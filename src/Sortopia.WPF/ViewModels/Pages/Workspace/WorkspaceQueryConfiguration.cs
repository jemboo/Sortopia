using Sortopia.Runs;
using Sortopia.Functions;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceExcludedDefault(string Key, RelayCommand RestoreCommand);
public sealed class WorkspaceQueryKeyChoice : ViewModelBase
{
    private bool selected;
    private readonly Action<bool> changed;
    public WorkspaceQueryKeyChoice(string key, bool selected, bool available, bool editable, string status, Action<bool> changed)
    { Key = key; this.selected = selected; Available = available; Editable = editable; Status = status; this.changed = changed; }
    public string Key { get; }
    public string Status { get; }
    public bool Available { get; }
    public bool Editable { get; }
    public bool CanChange => Editable && (Available || selected);
    public bool Selected { get => selected; set { if (CanChange && SetProperty(ref selected, value)) changed(value); } }
}
public sealed record WorkspaceInheritedSetupQuery(string Kind, string Text, string[] Keys)
{
    public string Label => Text.Length == 0 ? Kind : $"{Kind} · {Text}";
    public string KeysSummary => Keys.Length == 0 ? "No setup query keys selected" : string.Join(", ", Keys);
}
public sealed record WorkspaceOutputQuery(string Kind, string Text, WorkspaceQueryKeyChoice[] Keys, string Summary)
{
    public WorkspaceInheritedSetupQuery[] InheritedSetupQueries { get; init; } = [];
    public bool HasSetupInputs => InheritedSetupQueries.Length > 0;
    public bool HasInheritedSetupKeys => InheritedSetupQueries.Any(input => input.Keys.Length > 0);
    public bool HasNoQueryKeys => HasEmptyProjection && !HasInheritedSetupKeys;
    public string TextSummary => Text.Length == 0 ? "Text: (empty)" : $"Text: {Text}";
    public bool HasEmptyProjection => !Keys.Any(key => key.Selected);
    public string SelectedKeysSummary => (HasSetupInputs ? "Additional results query parameters: " : "Query parameters: ")
        + (HasEmptyProjection ? "none selected" : string.Join(", ", Keys.Where(key => key.Selected).Select(key => key.Key)));
    public string InspectorSummary
    {
        get
        {
            var own = HasEmptyProjection ? "none" : string.Join(", ", Keys.Where(key => key.Selected).Select(key => key.Key));
            var inherited = InheritedSetupQueries.SelectMany(input => input.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return HasSetupInputs ? $"{Kind} · inherited setup keys: {(inherited.Length == 0 ? "none" : string.Join(", ", inherited))} · additional results keys: {own}"
                : $"{Kind} · query keys: {own}";
        }
    }
}

public sealed partial class WorkspaceConfigurationViewModel
{
    private readonly Func<IEnumerable<WorkspaceConfigurationViewModel>> childEditors;
    private readonly HashSet<string> excludedResultsDefaults = new(StringComparer.Ordinal);
    private readonly HashSet<string> excludedSetupDefaults = new(StringComparer.Ordinal);
    private resultsDataTypeConfigDto[] resultsQueries = [], setupQueries = [];
    public bool ShowQueryConfiguration => !IsModule;
    public bool CanEditQuerySelection => IsProject && IsEditable;
    private string QuerySelectionGuidance => CanEditQuerySelection ? "Select query keys here, then supply their spans in each run. Remove or exclude a parameter's default before selecting it."
        : IsRun && Parent is { IsEditable: true } ? "Edit query projections on the project before generating run parameters."
        : "Remove generated parameter files from every run in the project to resume editing, or create a new version to choose different query keys.";
    public string QueryConfigurationHint => IsModule ? "Query keys are selected on each project."
        : IsRun ? $"Inherited from project ‘{Parent?.Name}’ · Read-only on this run. Every selected key requires an explicit span in this run. {QuerySelectionGuidance}"
        : CanEditQuerySelection ? $"Project ‘{Name}’: choose query keys before creating runs. Each output can use a different subset. Every run must supply exactly the combined query keys for each phase; other required parameters need fixed defaults. Choices are fixed when run parameters are generated."
        : "Query keys are locked while this project has generated parameter files. Remove them from all its runs to resume editing, or create a new version. Every selected key requires an explicit span in each run.";
    public IEnumerable<WorkspaceOutputQuery> ResultsQueryOutputs => QueryRows(false);
    public IEnumerable<WorkspaceOutputQuery> SetupQueryOutputs => QueryRows(true);
    public string ResultsProjectionNotice => ProjectionNotice(false);
    public string SetupProjectionNotice => ProjectionNotice(true);
    private string ProjectionNotice(bool setup)
    {
        if (!IsRun || !ActiveParameters(setup).Any(span => span.Key.Length > 0)) return "";
        var empty = QueryRows(setup).Where(output => output.HasEmptyProjection).ToArray();
        if (empty.Length > 0 && empty.All(output => output.HasSetupInputs))
            return $"No additional results query keys selected for {string.Join(", ", empty.Select(output => output.Kind))}. Different results span values share the same output for the same setup inputs. Different setup files still produce separate results. " + QuerySelectionGuidance;
        return empty.Length == 0 ? "" : $"No query keys selected for {string.Join(", ", empty.Select(output => output.Kind))}. Different span values share the same output when input data is unchanged. "
            + QuerySelectionGuidance;
    }
    public IEnumerable<WorkspaceExcludedDefault> ExcludedResultsDefaults => Exclusions(false);
    public IEnumerable<WorkspaceExcludedDefault> ExcludedSetupDefaults => Exclusions(true);
    internal void RefreshQueryChoices()
    {
        OnPropertyChanged(nameof(ResultsQueryOutputs));
        OnPropertyChanged(nameof(SetupQueryOutputs));
    }
    private IEnumerable<WorkspaceExcludedDefault> Exclusions(bool setup) => (setup ? excludedSetupDefaults : excludedResultsDefaults)
        .Order(StringComparer.Ordinal).Select(key => new WorkspaceExcludedDefault(key, new RelayCommand(() => {
            (setup ? excludedSetupDefaults : excludedResultsDefaults).Remove(key); NotifyChanged();
        }, () => CanEditDefaults)));
    private void ExcludeDefault(string key, bool setup)
    {
        if (!CanEditDefaults) return;
        Quietly(() => {
            (setup ? excludedSetupDefaults : excludedResultsDefaults).Add(key);
            var rows = setup ? SetupParameterDefaults : ResultsParameterDefaults;
            foreach (var row in rows.Where(row => row.Key == key).ToArray()) rows.Remove(row);
        });
        NotifyChanged();
    }
    private void LoadParameterPolicy(projectOverridesDto value)
    {
        excludedResultsDefaults.Clear(); excludedResultsDefaults.UnionWith(value.excludedResultsDefaults ?? []);
        excludedSetupDefaults.Clear(); if (IsBranch) excludedSetupDefaults.UnionWith(value.excludedSetupDefaults ?? []);
        resultsQueries = IsProject ? value.resultsQueries ?? [] : [];
        setupQueries = IsProject && IsBranch ? value.setupQueries ?? [] : [];
    }
    private string[] SelectedQueryKeys(sortopiaDataTypeDto type, bool setup) => IsProject
        ? (setup ? setupQueries : resultsQueries).FirstOrDefault(entry => SameType(entry.dataType, type))?.queryParameterKeys ?? [] : [];
    private string[] QueryCandidates(bool setup)
    {
        var owner = IsRun ? Parent : this;
        if (owner is null) return [];
        var defaults = (setup ? owner.EffectiveSetupDefaults : owner.EffectiveOutputDefaults).Select(value => value.keyName);
        return (owner.Requirements(setup) ?? []).Select(key => key.Name).Except(defaults, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    private void SetQueryKey(sortopiaDataTypeDto type, bool setup, string key, bool selected)
    {
        if (!CanEditQuerySelection || selected && !QueryCandidates(setup).Contains(key, StringComparer.Ordinal)) return;
        if (selected && (setup ? EffectiveSetupDefaults : EffectiveOutputDefaults).Any(value => value.keyName == key)) return;
        if (setup && SetupParametersFromSources && selected && !SourceKeysFor(EffectiveSetupDataTypes.Single(entry => SameType(entry.dataType, type))).Contains(key, StringComparer.Ordinal)) return;
        var entries = (setup ? setupQueries : resultsQueries).Where(entry => !SameType(entry.dataType, type)).ToList();
        var keys = SelectedQueryKeys(type, setup).ToHashSet(StringComparer.Ordinal);
        if (selected) keys.Add(key); else keys.Remove(key);
        entries.Add(new() { dataType = type, queryParameterKeys = keys.Order(StringComparer.Ordinal).ToArray() });
        if (setup) setupQueries = entries.ToArray(); else resultsQueries = entries.ToArray();
        NotifyChanged();
    }
    private IEnumerable<WorkspaceOutputQuery> QueryRows(bool setup)
    {
        var outputs = setup ? EffectiveSetupDataTypes.Select(entry => new resultsDataTypeConfigDto { dataType = entry.dataType, queryParameterKeys = entry.queryParameterKeys }) : EffectiveResultsDataTypes;
        var available = QueryCandidates(setup);
        var defaults = (setup ? EffectiveSetupDefaults : EffectiveOutputDefaults).Select(value => value.keyName).ToHashSet(StringComparer.Ordinal);
        foreach (var output in outputs)
        {
            var chosen = output.queryParameterKeys;
            var sourceKeys = setup && SetupParametersFromSources ? SourceKeysFor(EffectiveSetupDataTypes.Single(entry => SameType(entry.dataType, output.dataType))) : null;
            var availableForOutput = sourceKeys is null ? available : available.Intersect(sourceKeys, StringComparer.Ordinal).ToArray();
            var keys = (IsRun ? chosen : availableForOutput.Concat(chosen)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(key => {
                var supported = availableForOutput.Contains(key, StringComparer.Ordinal);
                var supplied = IsRun && ActiveParameters(setup).Any(span => span.Key == key && span.Error.Length == 0);
                var fromSource = sourceKeys is null || sourceKeys.Contains(key, StringComparer.Ordinal);
                var status = !fromSource ? "Not a query key of this source" : defaults.Contains(key) ? "Remove or exclude its default to use a query key"
                    : !supported ? "Not required by this function"
                    : IsRun ? supplied ? "Explicit span supplied" : "Missing explicit span"
                    : chosen.Contains(key, StringComparer.Ordinal) ? "Requires a span in every run" : "Available query key";
                return new WorkspaceQueryKeyChoice(key, chosen.Contains(key, StringComparer.Ordinal), supported && fromSource && !defaults.Contains(key), CanEditQuerySelection, status,
                    selected => SetQueryKey(output.dataType, setup, key, selected));
            }).ToArray();
            var inherited = setup ? [] : InheritedSetupQueries(output.dataType);
            var summary = inherited.Length > 0
                ? (chosen.Length == 0 ? "No additional results query keys selected. " : "Selected results keys distinguish outputs within each setup combination. ")
                    + "Different setup input files produce separate results. Changes to other results spans reuse the output when the setup inputs are unchanged. " + QuerySelectionGuidance
                : chosen.Length == 0
                ? "No query keys selected: all parameter sets share one output for the same project and input data, even when span values differ. "
                    + QuerySelectionGuidance
                : "Only the selected span keys distinguish this output. Other span changes reuse it for the same input data.";
            yield return new(output.dataType.kind, output.dataType.value, keys, summary) { InheritedSetupQueries = inherited };
        }
    }
    private WorkspaceInheritedSetupQuery[] InheritedSetupQueries(sortopiaDataTypeDto output)
    {
        if (!IsBranch || !PhaseVersionCurrent(false) || !ResultsFunctionCatalog.functions.TryGetValue(ResultsFunctionName, out var definition)
            || !definition.OutputDependencies.TryGetValue(SortopiaDataTypeDto.toDomain(output), out var inputs)) return [];
        return inputs.Select(input => {
            var type = SortopiaDataTypeDto.fromDomain(input);
            var setup = EffectiveSetupDataTypes.FirstOrDefault(entry => SameType(entry.dataType, type));
            return new WorkspaceInheritedSetupQuery(type.kind, type.value, setup?.queryParameterKeys ?? []);
        }).ToArray();
    }
    private string SourceQueryContractError()
    {
        if (SetupParametersFromSources)
        {
            var unknown = SourceSetupContract.UnknownKeys;
            if (unknown.Length > 0) return $"Unknown source query parameters: {string.Join(", ", unknown)}.";
            foreach (var output in EffectiveSetupDataTypes)
            {
                var outsideSource = SourceContracts.missingQueryKeys(output.queryParameterKeys, SourceContractFor(output)).Distinct(StringComparer.Ordinal).ToArray();
                if (outsideSource.Length > 0) return $"Setup query keys for {output.dataType.kind} must be a subset of that source's results query keys. Unsupported keys: {string.Join(", ", outsideSource)}.";
            }
        }
        var outside = Requirements(true) is { } required ? RequiredSetupKeys.Where(key => !required.Any(value => value.Name == key)).ToArray() : [];
        return outside.Length == 0 ? "" : $"The source project requires parameters outside the setup function's inputs: {string.Join(", ", outside)}. Choose a compatible source project.";
    }
}
