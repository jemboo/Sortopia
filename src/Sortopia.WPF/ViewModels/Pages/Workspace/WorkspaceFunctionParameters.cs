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
    public string Hint => $"{IconDescription} · {ValueType}. {Source}";
}

public sealed partial class WorkspaceConfigurationViewModel
{
    public WorkspaceFunctionParameter[] ResultsFunctionParameters => FunctionParameters(false);
    public WorkspaceFunctionParameter[] SetupFunctionParameters => FunctionParameters(true);
    // Show each selected source's query parameters while the remaining source choices are unfinished.
    public WorkspaceFunctionParameter[] SetupFunctionInputs => [.. SetupFunctionSources(), .. SetupFunctionParameters];

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
        var defaults = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var spans = (IsRun ? ActiveParameters(setup) : []).ToArray();
        static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "(empty)" : value;
        return (FunctionGridRequirements(setup) ?? []).Select(key =>
        {
            var defaultEditor = ParameterSource(key.Name, setup);
            var bindings = defaults.Where(value => value.keyName == key.Name).ToArray();
            var matches = spans.Where(span => span.Key == key.Name).ToArray();
            var values = matches.SelectMany(span => span.Values).Select(value => value.Text).ToArray();
            var type = ParameterValue.label(key.ValueType);
            var fixedValue = string.Join("; ", bindings.Select(value => Display(value.value)));
            var spanValue = $"[{string.Join(", ", values.Select(Display))}]";
            if (bindings.Length > 0 && matches.Length > 0)
                return new WorkspaceFunctionParameter(key.Name, $"Default: {fixedValue}; span: {spanValue}", type,
                    WorkspaceParameterSupply.Default, "Both a default and a span are assigned. Remove one to resolve the conflict.")
                    { TargetEditor = defaultEditor, IsSetup = setup };
            if (bindings.Length > 0)
                return new WorkspaceFunctionParameter(key.Name,
                    bindings.Any(value => !string.IsNullOrWhiteSpace(value.value)) ? fixedValue : "Not set", type,
                    bindings.Any(value => !string.IsNullOrWhiteSpace(value.value)) ? WorkspaceParameterSupply.Default : WorkspaceParameterSupply.None,
                    defaultEditor is { } source ? $"Default from {source.Name}." : "Project default.")
                    { TargetEditor = defaultEditor, IsSetup = setup };
            if (values.Any(value => !string.IsNullOrWhiteSpace(value)))
                return new WorkspaceFunctionParameter(key.Name, spanValue, type, WorkspaceParameterSupply.Span,
                    $"{values.Length} value(s) supplied by this run.") { TargetEditor = this, IsSetup = setup };
            return new WorkspaceFunctionParameter(key.Name, "Not set", type, WorkspaceParameterSupply.None,
                matches.Length > 0 ? "The run's span has no values yet." : "Assign a default or supply a span on a run.")
                { TargetEditor = this, IsSetup = setup, NeedsEntry = matches.Length == 0 };
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
            : FunctionGridRequirements(setup)?.Any(value => value.Name == key) == true ? "" : "Choose a function that requires this parameter first.";
        if ((setup ? SetupParameterDefaults : ResultsParameterDefaults).Any(value => value.Key == key)) return "";
        if (defaults.Any(value => value.keyName == key)) return "This parameter already has an inherited default. Open its defining node to edit it.";
        if (IsProject && childEditors().Any(child => child.ActiveParameters(setup).Any(span => span.Key == key)))
            return "This parameter is supplied by child run spans. Remove those spans before adding a project default.";
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
        }
        return true;
    }
    public string ResultsFunctionParametersHint => Requirements(false) is not { } required ? ResultsRequirementsHint
        : required.Length == 0 ? "This function has no required parameters." : "";
    public string SetupFunctionParametersHint => !IsBranch ? ""
        : SetupParametersFromSources && InheritedSetupDataTypes.Any(row => row.RequiresSource && row.Source is null)
            ? "Parameters from selected sources are shown below the source rows. Use the source icons to choose projects for the remaining inputs."
        : Requirements(true) is not { } required ? SetupRequirementsHint
        : required.Length == 0 ? SetupParametersFromSources ? "The selected sources need no query parameters." : "This function has no required parameters." : "";
}
