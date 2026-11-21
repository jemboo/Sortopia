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
            if (DefaultsError(setup, bindings).Length > 0 || inputs.Any(span => span.Error.Length > 0)) return "";
            var defaults = ListModule.OfSeq(bindings.Select(ParameterBindingDto.toDomain));
            var spans = ListModule.OfSeq(inputs.Select(span =>
                ParameterSpan.fromTexts(span.Key, span.ValueType, ListModule.OfSeq(span.Values.Select(value => value.Text)))));
            return validate.Invoke(defaults).Invoke(spans);
        }
        catch (ArgumentException error) { return error.Message; }
    }

    private functionParameterSupport? ParameterSupport(bool setup) => setup
        ? SetupFunctionCatalog.functions.TryGetValue(SetupFunctionName, out var setupDefinition) ? setupDefinition.ParameterSupport : null
        : ResultsFunctionCatalog.functions.TryGetValue(ResultsFunctionName, out var resultsDefinition) ? resultsDefinition.ParameterSupport : null;

    private string FunctionInputGuidance(bool setup)
    {
        var support = ParameterSupport(setup);
        if (!PhaseVersionCurrent(setup) || support is null) return "";
        var bindings = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var inputs = (IsRun ? ActiveParameters(setup) : []).ToArray();
        if (DefaultsError(setup, bindings).Length > 0 || inputs.Any(span => span.Error.Length > 0))
            return support.Guidance.Invoke(ListModule.Empty<parameterBinding>()).Invoke(ListModule.Empty<parameterSpan>());
        try
        {
            var defaults = ListModule.OfSeq(bindings.Select(ParameterBindingDto.toDomain));
            var spans = ListModule.OfSeq(inputs.Select(span =>
                ParameterSpan.fromTexts(span.Key, span.ValueType, ListModule.OfSeq(span.Values.Select(value => value.Text)))));
            return support.Guidance.Invoke(defaults).Invoke(spans);
        }
        catch (ArgumentException error) { return error.Message; }
    }
    public string ResultsInputGuidance => FunctionInputGuidance(false);
    public string SetupInputGuidance => FunctionInputGuidance(true);
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
            if (current.IsProject && current.IsReadOnly) return ""; // Sealed projects own their recorded function choices.
            if (current.IsModule && !string.IsNullOrWhiteSpace((setup ? current.SetupFunctionOverride : current.ResultsFunctionOverride).OverrideValue))
                modules.Add(current.Name);
        }
        return modules.Count <= 1 ? "" : $"The {(setup ? "setup" : "results")} function is defined on multiple modules: {string.Join(", ", modules)}. Keep its definition on exactly one module along the path to a run. Choose ‘Use inherited function’ on a child module to remove its extra definition.";
    }
    public bool FunctionsReady => HasResultsContract && (!IsBranch || HasSetupContract) && FunctionCompatibilityError.Length == 0
        && FunctionDefinitionError(false).Length == 0 && (!IsBranch || FunctionDefinitionError(true).Length == 0);
    public bool ShowFunctionConfiguration => FunctionsReady || IsModule && (HasResultsContract || HasSetupContract) && FunctionCompatibilityError.Length == 0;
    public string FunctionsHint => IsReadOnly ? "Functions are locked while descendant runs have generated parameter files. Remove those files or runs to resume editing, or create a new project version."
        : IsModule
        ? IsBranch ? "A module may leave either function undefined. Each branch run needs exactly one setup definition and one compatible results definition along its module path; they may be on different modules. Clear ‘Define on this module’ to inherit or leave the choice to a child module."
            : "A module may leave the results function undefined. Each root run needs exactly one results-function definition along its module path. Clear ‘Define on this module’ to inherit or leave the choice to a child module."
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
    private setupDataTypeConfigDto[] ResolveSetupTypes()
    {
        if (DeclaredSetup is not { } outputs) return [];
        return outputs.Select(output => new setupDataTypeConfigDto {
            dataType = output.dataType, queryParameterKeys = SelectedQueryKeys(output.dataType, true),
            sourceProjectId = NeedsSource(output.dataType) ? EffectiveSetupSource(output.dataType) : Guid.Empty
        }).ToArray();
    }
    private void RefreshContractRows()
    {
        if (DeclaredSetup is not { } outputs || IsRun || IsReadOnly) return;
        if (!SetupDataTypes.Select(row => (row.DataType.Kind, row.DataType.Text)).SequenceEqual(outputs.Select(entry => (entry.dataType.kind, entry.dataType.value))))
            Quietly(() => LoadSetupDataTypes(ResolveSetupTypes()));
        Quietly(() => { foreach (var row in SetupDataTypes) row.SetContract(NeedsSource(row.DataType.ToDto()), IsModule,
            SelectedQueryKeys(row.DataType.ToDto(), true), Requirements(true)?.Select(key => key.Name).ToArray(), SetupParametersFromSources); });
    }
}

public sealed record WorkspaceOutputContractPreview(string Kind, string Text, string ParameterKeys)
{
    public string TextSummary => Text.Length == 0 ? "Text: (empty)" : $"Text: {Text}";
}
