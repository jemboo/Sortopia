using Sortopia.Runs;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public enum WorkspaceParameterSupply { None, Default, Span }

public sealed record WorkspaceFunctionParameter(string Name, string Value, string ValueType,
    WorkspaceParameterSupply Supply, string Source)
{
    public WorkspaceConfigurationViewModel? TargetEditor { get; init; }
    public bool IsSetup { get; init; }
    public bool NeedsEntry { get; init; }
    public string? SourceKind { get; init; }
    public string SourceText { get; init; } = "";
    public bool IsSourceInput => SourceKind is not null;
    public bool IsSourceHeading { get; init; }
    public string? RequiredBy { get; init; }
    public bool IsSourceParameter => RequiredBy is not null;
    public bool TargetsSpan => TargetEditor?.IsRun == true;
    private string MissingEntryError => !IsSourceInput && Supply == WorkspaceParameterSupply.None
        ? TargetEditor?.MissingFunctionParameterError(Name, IsSetup) ?? "" : "";
    public bool CanNavigate => TargetEditor is not null && MissingEntryError.Length == 0;
    public string ItemKey => IsSourceInput ? "setupOutputs" : (IsSetup ? "setup" : "results") + (TargetsSpan ? "Spans" : "Defaults");
    public string NavigationLabel => CanNavigate
        ? IsSourceInput ? $"Open {Name} source selection on {TargetEditor!.Name}"
        : $"{(NeedsEntry ? "Add" : "Open")} {Name} {(IsSetup ? "setup" : "results")} {(TargetsSpan ? "span" : "default")} on {TargetEditor!.Name}"
        : IconDescription;
    public string NavigationHint => CanNavigate ? $"{NavigationLabel}. {Hint}"
        : MissingEntryError.Length > 0 ? $"{Hint} {MissingEntryError}" : Hint;
    public string IconDescription => Supply switch
    {
        WorkspaceParameterSupply.Default => "Fixed/default value",
        WorkspaceParameterSupply.Span => "Run parameter span",
        _ => "No value assigned"
    };
    public string Hint => $"{IconDescription} · {ValueType}. {Source}"
        + (RequiredBy is null ? "" : $" Required by {RequiredBy}.");
}

public sealed partial class WorkspaceConfigurationViewModel
{
    public WorkspaceFunctionParameter[] ResultsFunctionParameters => FunctionParameters(false);
    public WorkspaceFunctionParameter[] SetupFunctionParameters => FunctionParameters(true);
    public WorkspaceFunctionParameter[] SetupFunctionInputs
    {
        get
        {
            var parameters = SetupFunctionParameters;
            if (!SetupParametersFromSources) return [.. SetupFunctionSources(), .. parameters];
            var values = parameters.ToDictionary(row => row.Name, StringComparer.Ordinal);
            var outputs = EffectiveSetupDataTypes;
            var rows = new List<WorkspaceFunctionParameter>();
            foreach (var source in SetupFunctionSources())
            {
                rows.Add(source with { IsSourceHeading = true });
                var output = outputs.Single(row => row.dataType.kind == source.SourceKind && row.dataType.value == source.SourceText);
                // Shared keys appear under every source that requires them, with the same edit target.
                foreach (var key in SourceContractFor(output).RequiredParameters)
                    if (values.TryGetValue(key.Name, out var parameter)) rows.Add(parameter with { RequiredBy = source.Name });
            }
            return rows.ToArray();
        }
    }

    // Keep Requirements unchanged: a module's unresolved sources must not filter out defaults
    // intended for a source that a descendant will choose later.
    private paramKeyDescriptor[]? FunctionGridRequirements(bool setup) => Requirements(setup)
        ?? (setup && IsBranch && PhaseVersionCurrent(true) && SetupParametersFromSources
            ? SourceSetupContract.RequiredParameters.ToArray() : null);

    private IEnumerable<WorkspaceFunctionParameter> SetupFunctionSources()
    {
        if (!IsBranch || !HasSetupContract) yield break;
        foreach (var row in InheritedSetupDataTypes.Where(row => row.RequiresSource))
        {
            var source = row.Source;
            var name = row.DataType.Text.Length == 0 ? row.DataType.Kind : $"{row.DataType.Kind} \"{row.DataType.Text}\"";
            yield return new(name, source?.Name ?? (row.SourceProjectId == Guid.Empty ? "Not set" : "Missing source project"),
                "Source project", source is null ? WorkspaceParameterSupply.None : WorkspaceParameterSupply.Default,
                row.Error.Length > 0 ? row.Error : row.SourceQueryHint)
            {
                TargetEditor = SetupSourceOwner(row.DataType.ToDto()), IsSetup = true,
                SourceKind = row.DataType.Kind, SourceText = row.DataType.Text
            };
        }
    }

    private WorkspaceFunctionParameter[] FunctionParameters(bool setup)
    {
        if (setup && !IsBranch) return [];
        static string Display(string value) => value.Length == 0 ? "(empty)" : value;
        return ParameterStates(setup).Select(state =>
        {
            var values = state.Spans.SelectMany(span => span.Values).Select(value => Display(value.Text)).ToArray();
            var value = state.Defaults.Length > 0 ? string.Join("; ", state.Defaults.Select(row => Display(row.value)))
                : values.Length > 0 ? $"[{string.Join(", ", values)}]" : "Not set";
            if (state.Supply == WorkspaceParameterSupply.None) value = "Not set";
            if (state.Defaults.Length > 0 && state.Spans.Length > 0) value = $"Default: {value}; span: [{string.Join(", ", values)}]";
            var source = state.HasIssue ? $"{state.Status}: {state.Error}" : state.Supply == WorkspaceParameterSupply.Default
                ? $"Default from {state.DefaultEditor?.Name ?? "project"}." : $"{values.Length} value(s) supplied by this run.";
            return new WorkspaceFunctionParameter(state.Key, value, ParameterValue.label(state.ValueType), state.Supply, source)
            {
                TargetEditor = state.Defaults.Length > 0 ? state.DefaultEditor
                    : IsRun && !state.IsQuery && state.Spans.Length == 0 ? Parent : this,
                IsSetup = setup, NeedsEntry = state.Defaults.Length == 0 && state.Spans.Length == 0
            };
        }).ToArray();
    }
    internal string MissingFunctionParameterError(string key, bool setup)
    {
        if (!IsEditable) return "These settings are locked. Resume editing or create a new project version to add values.";
        if (setup && !IsBranch) return "This node has no setup parameters.";
        if (Requirements(setup) is { } required && !required.Any(value => value.Name == key))
            return "This parameter is no longer required by the selected function.";
        var defaults = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        if (IsRun) return defaults.Any(value => value.keyName == key)
            ? "This parameter already has a default. Open its defining node to edit it."
            : !QueryKeys(setup).Contains(key) ? "Only project query keys can have run spans. Set a fixed default on the project or select this query key there."
            : FunctionGridRequirements(setup)?.Any(value => value.Name == key) == true ? "" : "Choose a function that requires this parameter first.";
        if (IsProject && QueryKeys(setup).Contains(key)) return "This is a project query key. Supply its span on each run instead of a default.";
        if ((setup ? SetupParameterDefaults : ResultsParameterDefaults).Any(value => value.Key == key)) return "";
        if (defaults.Any(value => value.keyName == key)) return "This parameter already has an inherited default. Open its defining node to edit it.";
        return FunctionGridRequirements(setup)?.Any(value => value.Name == key) == true ? "" : "Choose a function that requires this parameter first.";
    }
    internal bool PrepareMissingFunctionParameter(string key, bool setup)
    {
        if (MissingFunctionParameterError(key, setup).Length > 0) return false;
        if (IsRun)
        {
            var rows = setup ? SetupParameters : ResultsParameters;
            var existing = rows.FirstOrDefault(row => row.Key == key);
            if (existing is null) AddSpan(rows, key, [""]);
            else if (existing.Values.Count == 0) existing.AddValueCommand.Execute(null);
        }
        else
        {
            var rows = setup ? SetupParameterDefaults : ResultsParameterDefaults;
            (setup ? SetupParameterDefaultsOverride : ResultsParameterDefaultsOverride).IsOverridden = true;
            if (!rows.Any(row => row.Key == key))
            {
                var descriptor = FunctionGridRequirements(setup)!.Single(value => value.Name == key);
                AddDefault(rows, new() { keyName = key, valueType = (int)descriptor.ValueType, value = "" });
            }
            (setup ? excludedSetupDefaults : excludedResultsDefaults).Remove(key);
        }
        return true;
    }
    public string ResultsFunctionParametersHint => Requirements(false) is not { } required ? ResultsRequirementsHint
        : required.Length == 0 ? "This function has no required parameters." : "";
    public string SetupFunctionParametersHint => !IsBranch ? ""
        : SetupParametersFromSources && InheritedSetupDataTypes.Any(row => row.RequiresSource && row.Source is null)
            ? "Each selected source's parameters appear beneath it. Use the source icons to choose projects for the remaining inputs."
        : Requirements(true) is not { } required ? SetupRequirementsHint
        : required.Length == 0 ? SetupParametersFromSources ? "The selected sources need no query parameters." : "This function has no required parameters." : "";
}
