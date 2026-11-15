using Sortopia.Functions;
using Microsoft.FSharp.Collections;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Sortopia.Sorting;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspaceConfigurationViewModel
{
    private IEnumerable<WorkspaceFunctionDefinitionSource> DescendantFunctionSources(bool setup, string prefix = "")
    {
        foreach (var child in childEditors().Where(child => child.IsModule))
        {
            var path = prefix.Length == 0 ? child.Name : $"{prefix} / {child.Name}";
            var field = setup ? child.SetupFunctionOverride : child.ResultsFunctionOverride;
            if (!string.IsNullOrWhiteSpace(field.OverrideValue)) yield return new(child, path, field.OverrideValue);
            foreach (var definition in child.DescendantFunctionSources(setup, path)) yield return definition;
        }
    }
    internal void RefreshFunctionDefinitions()
    {
        ResultsFunctionOverride.RefreshInherited(); SetupFunctionOverride.RefreshInherited();
    }

    private string FunctionInputsError(bool setup, bool includeSpans)
    {
        if (!PhaseVersionCurrent(setup)) return "";
        var validate = setup
            ? SetupFunctionCatalog.functions.TryGetValue(SetupFunctionName, out var setupDefinition) ? setupDefinition.InputError : null
            : ResultsFunctionCatalog.functions.TryGetValue(ResultsFunctionName, out var resultsDefinition) ? resultsDefinition.InputError : null;
        if (validate is null) return "";
        try
        {
            var bindings = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
            var inputs = (includeSpans ? ActiveParameters(setup) : []).ToArray();
            if (DefaultsError(bindings).Length > 0 || inputs.Any(span => span.Error.Length > 0)) return "";
            var defaults = ListModule.OfSeq(bindings.Select(ParameterBindingDto.toDomain));
            var spans = ListModule.OfSeq(inputs.Select(span =>
                ParameterSpan.fromTexts(span.Key, span.ValueType, ListModule.OfSeq(span.Values.Select(value => value.Text)))));
            return validate.Invoke(defaults).Invoke(spans);
        }
        catch (ArgumentException error) { return error.Message; }
    }

    private static string ModelInputGuidance(string functionName)
    {
        bool prefix = functionName.StartsWith("sorter-model-set.prefix.", StringComparison.Ordinal);
        if (!WorkspaceParameterInitialValues.IsModelFunction(functionName)) return "";
        var dimensions = prefix ? "Channel count and prefix stage length come from PrefixLibId. SorterSuccessFraction determines the random suffix capacity; the fixed prefix is always added. "
            : "ChannelCount sets the width; SorterSuccessFraction determines the stage capacity with zero prefix stages. The generated models have no fixed prefix. ";
        var tests = functionName.StartsWith("sorter-eval.merge.", StringComparison.Ordinal)
            ? "MergeLibId selects the source tests; its width must match ChannelCount. Choose the source project for SortableTests in setup outputs. "
            : functionName.StartsWith("sorter-eval.prefix.", StringComparison.Ordinal)
                ? "PrefixLibId selects the source tests; its width must match ChannelCount. Choose the source project for SortableTests in setup outputs. "
                : functionName.StartsWith("sorter-eval.standard.", StringComparison.Ordinal) || functionName.StartsWith("sorter-eval-inputs.standard.", StringComparison.Ordinal)
                    ? "Generates full sortable tests using ChannelCount and SortableDataFormat. Tests and models have separate output projections. " : "";
        var family = functionName.EndsWith(".simple-r", StringComparison.Ordinal)
            ? "The function fixes the algorithm and SimpleR model type. AInitial sets every suffix activator's starting value and must be within [-AMax, AMax]. Only positive activators enable entries. "
            : functionName.EndsWith(".simple-s", StringComparison.Ordinal)
                ? "The function fixes the algorithm and SimpleS model type. ActivationProbability controls Boolean suffix activators. "
                : functionName.EndsWith(".simple", StringComparison.Ordinal)
                    ? "The function fixes the algorithm and Simple model type. All suffix entries are enabled; no activator parameters are required. "
                    : "";
        return family
            + dimensions + "Choose 50%, 90%, or 99% of generated networks sorting every input. These budgets are provisional; custom weights and inactive entries can change the actual success rate. "
            + tests + "Repl selects a reproducible batch of SorterCount models. Mode weights are shared by every stage; UF4/UF6 have separate seed and unfolding-level weights. Every width and unfolding span combination must be compatible.";
    }
    private string ModelStageLengthSuggestion(bool setup)
    {
        var name = setup ? SetupFunctionName : ResultsFunctionName;
        if (!PhaseVersionCurrent(setup) || !WorkspaceParameterInitialValues.IsModelFunction(name)) return "";
        var defaults = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var spans = IsRun ? ActiveParameters(setup).ToArray() : [];
        parameterValue[] Values(string key)
        {
            var bindings = defaults.Where(value => value.keyName == key).ToArray();
            var matches = spans.Where(span => span.Key == key).ToArray();
            if (bindings.Length > 1 || matches.Length > 1 || bindings.Length > 0 && matches.Length > 0) return [];
            var texts = bindings.Select(value => value.value).Concat(matches.SelectMany(span => span.Values.Select(value => value.Text))).ToArray();
            var parsed = texts.Select(text => ParameterValue.tryParse(ParamKeys.valueType(key), text)).ToArray();
            return parsed.Any(value => value is null) ? [] : parsed.Select(value => value.Value).Distinct().ToArray();
        }
        var fractions = Values("SorterSuccessFraction").OfType<parameterValue.SorterSuccessFractionValue>().Select(value => value.Item).ToArray();
        var dimensions = name.StartsWith("sorter-model-set.prefix.", StringComparison.Ordinal)
            ? Values("PrefixLibId").OfType<parameterValue.PrefixLibIdValue>().Select(value => (Width: value.Item.ChannelCount, Prefix: value.Item.StageLength)).ToArray()
            : Values("ChannelCount").OfType<parameterValue.IntegerValue>().Select(value => (Width: value.Item, Prefix: 0)).ToArray();
        if (fractions.Length == 0 || dimensions.Length == 0) return "Select a success fraction and channel count or prefix to see the suggested stage length. ";
        var model = SimpleSorterModelType.fromString(name.Split('.').First(part => part is "msce" or "mssi" or "msrs" or "msuf4" or "msuf6"));
        try
        {
            var suggestions = (from dimension in dimensions from fraction in fractions
                select SimpleSorterStageLengths.forSuccessFraction(fraction, dimension.Width, dimension.Prefix, model)).ToArray();
            if (suggestions.Any(value => !value.IsSupported)) return "Stage suggestion unavailable for an unsupported channel count. ";
            var minimum = suggestions.Min(value => value.Stages); var maximum = suggestions.Max(value => value.Stages);
            if (suggestions.Length == 1)
                return $"Suggested random suffix: {minimum:N0} stages ({dimensions[0].Width} channels, {dimensions[0].Prefix} prefix stages, {SorterSuccessFraction.toString(fractions[0])} target; {suggestions[0].Source}). ";
            return $"Suggested random suffix: {minimum:N0}–{maximum:N0} stages across the selected widths, prefixes, and success fractions. ";
        }
        catch (ArgumentException error) { return $"Stage suggestion unavailable: {error.Message} "; }
    }
    public string ResultsInputGuidance => HasResultsContract ? ModelStageLengthSuggestion(false) + ModelInputGuidance(ResultsFunctionName) : "";
    public string SetupInputGuidance => HasSetupContract ? ModelStageLengthSuggestion(true) + ModelInputGuidance(SetupFunctionName) : "";
    public bool HasResultsInputGuidance => ResultsInputGuidance.Length > 0;
    public bool HasSetupInputGuidance => SetupInputGuidance.Length > 0;
    public bool HasResultsContract => ResultsFunctionCatalog.functions.ContainsKey(ResultsFunctionName);
    public bool HasSetupContract => SetupFunctionCatalog.functions.ContainsKey(SetupFunctionName);
    public bool SetupGeneratesAllOutputs => SetupFunctionCatalog.functions.TryGetValue(SetupFunctionName, out var definition) && definition.SourceDataTypes.IsEmpty;
    public string ResultsContractHint => HasResultsContract ? "Saved data types and their text are defined by the results function. Query keys are chosen on the project from explicit run spans." : "";
    public string SetupContractHint => HasSetupContract ? "Saved data types and their text are defined by the setup function. Query keys are chosen on the project from explicit run spans. Choose source projects only for outputs that consume source data." : "";
    public string[] CompatibleResultsFunctions => ResultsFunctionCatalog.functions
        .Where(entry => IsBranch ? !HasSetupContract || entry.Value.SourceDataTypes.All(input =>
            SetupFunctionCatalog.functions[SetupFunctionName].SortopiaDataTypes.Contains(input)) : entry.Value.SourceDataTypes.IsEmpty)
        .Select(entry => entry.Key).ToArray();
    private string FunctionDefinitionError(bool setup)
    {
        if (IsModule) return "";
        var modules = new List<string>();
        for (var current = this; current is not null; current = current.Parent)
        {
            if (current.IsReadOnly) return ""; // Sealed projects own their recorded function choices.
            if (current.IsModule && !string.IsNullOrWhiteSpace((setup ? current.SetupFunctionOverride : current.ResultsFunctionOverride).OverrideValue))
                modules.Add(current.Name);
        }
        return modules.Count <= 1 ? "" : $"The {(setup ? "setup" : "results")} function is defined on multiple modules: {string.Join(", ", modules)}. Keep its definition on exactly one module along the path to a run. Choose ‘Use inherited function’ on a child module to remove its extra definition.";
    }
    public bool FunctionsReady => HasResultsContract && (!IsBranch || HasSetupContract) && FunctionCompatibilityError.Length == 0
        && FunctionDefinitionError(false).Length == 0 && (!IsBranch || FunctionDefinitionError(true).Length == 0);
    public bool ShowFunctionConfiguration => FunctionsReady || IsModule && (HasResultsContract || HasSetupContract) && FunctionCompatibilityError.Length == 0;
    public string FunctionsTitle => IsModule ? "Choose functions" : "Inherited functions";
    public string FunctionsHint => IsModule
        ? IsBranch ? "A module may leave either function undefined. Each branch run needs exactly one setup definition and one compatible results definition along its module path; they may be on different modules. Clear ‘Define on this module’ to inherit or leave the choice to a child module."
            : "A module may leave the results function undefined. Each root run needs exactly one results-function definition along its module path. Clear ‘Define on this module’ to inherit or leave the choice to a child module."
        : IsReadOnly ? "These functions were fixed when this project first generated work. Create a new project version to use different functions."
        : IsRun ? "Functions come from the parent project. Saved runs keep that project’s fixed function choices. Supply spans only for the required keys below."
        : "Functions are inherited from the module. Follow a source link to change a function there; this project can override its allowed defaults and source projects.";
    public string FunctionsStatus => !FunctionVersionsCurrent ? "This project uses a different function version. Saved settings and data remain available; create a new project version to execute the installed functions."
        : FunctionDefinitionError(false).Length > 0 ? FunctionDefinitionError(false)
        : IsBranch && FunctionDefinitionError(true).Length > 0 ? FunctionDefinitionError(true)
        : !FunctionsReady && !IsModule ? "The inherited functions are missing, unavailable, or incompatible. Select the parent module and choose compatible catalog functions before configuring parameters and outputs."
        : ResultsFunctionError.Length > 0 || IsBranch && SetupFunctionError.Length > 0 ? "Review the function issues below before configuring parameters and outputs."
        : IsModule && (!HasResultsContract || IsBranch && !HasSetupContract) ? "Functions not defined here can be supplied by a child module before a run is configured. This module can be saved as it is."
        : FunctionCompatibilityError.Length > 0 ? "Choose a compatible setup/results pair to continue."
        : IsRun ? "Select parameter spans, outputs, or Run parameter files in Configuration to continue."
        : "Select parameter defaults or outputs in Configuration to continue.";
    public string RetainedResultsDefaultsHint => RetainedDefaultsHint(false);
    public string RetainedSetupDefaultsHint => RetainedDefaultsHint(true);
    private string RetainedDefaultsHint(bool setup)
    {
        if (!CanEditDefaults || Requirements(setup) is not { } required) return "";
        var count = (setup ? SetupParameterDefaults : ResultsParameterDefaults).Count(row => !string.IsNullOrWhiteSpace(row.Key) && !required.Any(key => key.Name == row.Key));
        return count == 0 ? "" : $"{count} default(s) from other functions are kept for this editing session. They are not used or saved with this function; switching back restores them.";
    }
    public string RetainedResultsSpansHint => RetainedSpansHint(false);
    public string RetainedSetupSpansHint => RetainedSpansHint(true);
    private string RetainedSpansHint(bool setup)
    {
        if (!IsRun || Requirements(setup) is not { } required) return "";
        var count = (setup ? SetupParameters : ResultsParameters).Count(row => !string.IsNullOrWhiteSpace(row.Key) && !required.Any(key => key.Name == row.Key));
        return count == 0 ? "" : $"{count} span(s) from other functions are kept for this editing session. They are not used or saved with this function; restoring the inherited function restores them.";
    }
    public string FunctionCompatibilityError
    {
        get
        {
            if (!FunctionVersionsCurrent) return "";
            if (!ResultsFunctionCatalog.functions.TryGetValue(ResultsFunctionName, out var results) || results.SourceDataTypes.IsEmpty) return "";
            if (!IsBranch) return "This results function consumes setup data. Select it on a branch module, or choose a results function that generates its own data for this root module.";
            if (!HasSetupContract) return IsModule ? "" : "Select a setup function on the module that provides the results function’s input data.";
            var missing = results.SourceDataTypes.Where(required => !EffectiveSetupOutputs.Any(output => Equals(SortopiaDataTypeDto.toDomain(output), required))).ToArray();
            return missing.Length == 0 ? "" : $"The results function needs setup outputs: {string.Join(", ", missing.Select(value => value.ToString()))}. Choose a setup function on the module that saves these exact data types.";
        }
    }
    private resultsDataTypeConfigDto[]? DeclaredResults => ResultsFunctionCatalog.functions.TryGetValue(ResultsFunctionName, out var definition)
        ? definition.DataTypes.Select(entry => new resultsDataTypeConfigDto { dataType = SortopiaDataTypeDto.fromDomain(entry.Key), queryParameterKeys = [] }).ToArray() : null;
    private resultsDataTypeConfigDto[]? DeclaredSetup => SetupFunctionCatalog.functions.TryGetValue(SetupFunctionName, out var definition)
        ? definition.DataTypes.Select(entry => new resultsDataTypeConfigDto { dataType = SortopiaDataTypeDto.fromDomain(entry.Key), queryParameterKeys = [] }).ToArray() : null;
    private static bool SameType(sortopiaDataTypeDto left, sortopiaDataTypeDto right) => left.kind == right.kind && left.value == right.value;
    private bool NeedsSource(sortopiaDataTypeDto dataType) => !PhaseVersionCurrent(true)
        ? EffectiveSetupDataTypes.Any(entry => SameType(entry.dataType, dataType) && entry.sourceProjectId != Guid.Empty)
        : FunctionConfiguration.requiresSource(SetupFunctionName, SortopiaDataTypeDto.toDomain(dataType));
    private resultsDataTypeConfigDto[] EffectiveResultsDataTypes => (IsRun ? Parent?.EffectiveResultsDataTypes : null) ?? frozenSettings?.resultsDataTypes
        ?? (DeclaredResults ?? []).Select(entry => new resultsDataTypeConfigDto { dataType = entry.dataType, queryParameterKeys = SelectedQueryKeys(entry.dataType, false) }).ToArray();
    public IEnumerable<WorkspaceOutputContractPreview> ResultsOutputContracts => EffectiveResultsDataTypes.Select(entry =>
        new WorkspaceOutputContractPreview(entry.dataType.kind, entry.dataType.value, IsModule ? "Configured on each project" : string.Join(", ", entry.queryParameterKeys)));
    private setupDataTypeConfigDto[] ResolveSetupTypes(setupDataTypeConfigDto[] local)
    {
        if (DeclaredSetup is not { } outputs) return [];
        return outputs.Select(output => new setupDataTypeConfigDto {
            dataType = output.dataType, queryParameterKeys = SelectedQueryKeys(output.dataType, true),
            sourceProjectId = NeedsSource(output.dataType) ? local.FirstOrDefault(entry => SameType(entry.dataType, output.dataType))?.sourceProjectId ?? Guid.Empty : Guid.Empty
        }).ToArray();
    }
    private void RefreshContractRows()
    {
        if (DeclaredSetup is not { } outputs || IsRun || IsReadOnly) return;
        var local = SetupDataTypes.Select(row => row.ToDto()).ToArray();
        if (!local.Select(entry => (entry.dataType.kind, entry.dataType.value)).SequenceEqual(outputs.Select(entry => (entry.dataType.kind, entry.dataType.value))))
            Quietly(() => LoadSetupDataTypes(ResolveSetupTypes(SetupDataTypesOverride.IsOverridden ? local : Parent?.EffectiveSetupDataTypes ?? [])));
        Quietly(() => { foreach (var row in SetupDataTypes) row.SetContract(NeedsSource(row.DataType.ToDto()), IsModule,
            SelectedQueryKeys(row.DataType.ToDto(), true), Requirements(true)?.Select(key => key.Name).ToArray()); });
    }
}

public sealed record WorkspaceOutputContractPreview(string Kind, string Text, string ParameterKeys)
{
    public string TextSummary => Text.Length == 0 ? "Text: (empty)" : $"Text: {Text}";
}
