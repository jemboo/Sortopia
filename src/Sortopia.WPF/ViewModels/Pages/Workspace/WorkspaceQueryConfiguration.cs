using Sortopia.Runs;
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
public sealed record WorkspaceOutputQuery(string Kind, string Text, WorkspaceQueryKeyChoice[] Keys, string Summary)
{
    public string TextSummary => Text.Length == 0 ? "Text: (empty)" : $"Text: {Text}";
    public bool HasEmptyProjection => !Keys.Any(key => key.Selected);
    public string SelectedKeysSummary => HasEmptyProjection ? "Query parameters: none selected"
        : "Query parameters: " + string.Join(", ", Keys.Where(key => key.Selected).Select(key => key.Key));
}

public sealed partial class WorkspaceConfigurationViewModel
{
    private readonly Func<IEnumerable<WorkspaceConfigurationViewModel>> childEditors;
    private readonly HashSet<string> excludedResultsDefaults = new(StringComparer.Ordinal);
    private readonly HashSet<string> excludedSetupDefaults = new(StringComparer.Ordinal);
    private resultsDataTypeConfigDto[] resultsQueries = [], setupQueries = [];
    public bool ShowQueryConfiguration => !IsModule;
    public bool CanEditQuerySelection => IsProject ? IsEditable : IsRun && Parent is { IsProject: true, IsEditable: true };
    public string QueryConfigurationHint => IsModule ? "Query keys are selected on each project."
        : CanEditQuerySelection ? $"Project ‘{(IsRun ? Parent?.Name : Name)}’: choose query keys from explicit spans shared by its runs. Add run spans first. Each output can use a different subset. Choices are fixed when run parameters are generated."
        : "Query keys are fixed by the sealed project. Select the project to resume editing if it has no generated work, or create a new version. Every selected key requires an explicit span in each run.";
    public IEnumerable<WorkspaceOutputQuery> ResultsQueryOutputs => QueryRows(false);
    public IEnumerable<WorkspaceOutputQuery> SetupQueryOutputs => QueryRows(true);
    public string ResultsProjectionNotice => ProjectionNotice(false);
    public string SetupProjectionNotice => ProjectionNotice(true);
    private string ProjectionNotice(bool setup)
    {
        if (!IsRun || !ActiveParameters(setup).Any(span => span.Key.Length > 0)) return "";
        var empty = QueryRows(setup).Where(output => output.HasEmptyProjection).Select(output => output.Kind).ToArray();
        return empty.Length == 0 ? "" : $"No query keys selected for {string.Join(", ", empty)}. Different span values share the same output when input data is unchanged. "
            + (CanEditQuerySelection ? "Choose each output's projection below before generating run parameters."
                : "Select the project to resume editing if unused, or create a new version to choose different query keys.");
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
        var runs = owner.childEditors().Where(child => child.IsRun).ToArray();
        if (runs.Length == 0) return [];
        var keys = runs.Select(run => run.ActiveParameters(setup).Where(span => span.Error.Length == 0).Select(span => span.Key).ToHashSet(StringComparer.Ordinal)).ToArray();
        var defaults = (setup ? owner.EffectiveSetupDefaults : owner.EffectiveOutputDefaults).Select(value => value.keyName);
        return keys[0].Where(key => keys.All(set => set.Contains(key))).Except(defaults, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    private void SetQueryKey(sortopiaDataTypeDto type, bool setup, string key, bool selected)
    {
        var owner = IsRun ? Parent : this;
        if (!CanEditQuerySelection || owner is null || selected && !QueryCandidates(setup).Contains(key, StringComparer.Ordinal)) return;
        var entries = (setup ? owner.setupQueries : owner.resultsQueries).Where(entry => !SameType(entry.dataType, type)).ToList();
        var keys = owner.SelectedQueryKeys(type, setup).ToHashSet(StringComparer.Ordinal);
        if (selected) keys.Add(key); else keys.Remove(key);
        entries.Add(new() { dataType = type, queryParameterKeys = keys.Order(StringComparer.Ordinal).ToArray() });
        if (setup) owner.setupQueries = entries.ToArray(); else owner.resultsQueries = entries.ToArray();
        owner.NotifyChanged();
    }
    private IEnumerable<WorkspaceOutputQuery> QueryRows(bool setup)
    {
        var outputs = setup ? EffectiveSetupDataTypes.Select(entry => new resultsDataTypeConfigDto { dataType = entry.dataType, queryParameterKeys = entry.queryParameterKeys }) : EffectiveResultsDataTypes;
        var available = QueryCandidates(setup);
        var defaults = (setup ? EffectiveSetupDefaults : EffectiveOutputDefaults).Select(value => value.keyName).ToHashSet(StringComparer.Ordinal);
        foreach (var output in outputs)
        {
            var chosen = output.queryParameterKeys;
            var keys = available.Concat(chosen).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(key => {
                var supplied = IsRun ? ActiveParameters(setup).Any(span => span.Key == key && span.Error.Length == 0) : available.Contains(key, StringComparer.Ordinal);
                var status = defaults.Contains(key) ? "Conflict: supplied by a default" : supplied ? "Explicit span supplied" : "Missing explicit span";
                return new WorkspaceQueryKeyChoice(key, chosen.Contains(key, StringComparer.Ordinal), supplied, CanEditQuerySelection, status,
                    selected => SetQueryKey(output.dataType, setup, key, selected));
            }).ToArray();
            yield return new(output.dataType.kind, output.dataType.value, keys, chosen.Length == 0
                ? "No query keys selected: all parameter sets share one output for the same project and input data, even when span values differ. "
                    + (CanEditQuerySelection ? "Select span keys to distinguish those outputs." : "Use a new project with the desired query keys to distinguish those outputs.")
                : "Only the selected span keys distinguish this output. Other span changes reuse it for the same input data.");
        }
    }
    private string SourceQueryContractError()
    {
        var outside = Requirements(true) is { } required ? RequiredSetupKeys.Where(key => !required.Any(value => value.Name == key)).ToArray() : [];
        return outside.Length == 0 ? "" : $"The source project requires parameters outside the setup function's inputs: {string.Join(", ", outside)}. Choose a compatible source project.";
    }
}
