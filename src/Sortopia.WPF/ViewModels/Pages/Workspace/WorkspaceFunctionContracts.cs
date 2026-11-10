using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspaceConfigurationViewModel
{
    public bool HasResultsContract => FunctionCatalogs.resultsFunctionCatalog.ContainsKey(ResultsFunctionName);
    public bool HasSetupContract => FunctionCatalogs.setupFunctionCatalog.ContainsKey(SetupFunctionName);
    public bool SetupGeneratesAllOutputs => FunctionCatalogs.setupFunctionCatalog.TryGetValue(SetupFunctionName, out var definition) && definition.SourceDataTypes.IsEmpty;
    public string ResultsContractHint => HasResultsContract ? "Saved data types, their text, and each output’s parameter keys are defined by the results function." : "";
    public string SetupContractHint => HasSetupContract ? "Saved data types, their text, and parameter keys are defined by the setup function. Choose source projects only for outputs that consume source data." : "";
    public bool CanAddSetupOutputs => CanEditDefaults && !HasSetupContract;
    public string[] CompatibleResultsFunctions => FunctionCatalogs.resultsFunctionCatalog
        .Where(entry => IsBranch ? HasSetupContract && entry.Value.SourceDataTypes.All(input =>
            FunctionCatalogs.setupFunctionCatalog[SetupFunctionName].SortopiaDataTypes.Contains(input)) : entry.Value.SourceDataTypes.IsEmpty)
        .Select(entry => entry.Key).ToArray();
    public bool FunctionsReady => HasResultsContract && (!IsBranch || HasSetupContract) && FunctionCompatibilityError.Length == 0;
    public bool ShowFunctionConfiguration => FunctionsReady;
    public string FunctionsTitle => IsModule ? "Choose functions" : "Inherited functions";
    public string FunctionsHint => IsModule
        ? IsBranch ? "Choose or inherit a setup function, then a compatible results function. Their required parameters, saved outputs, and source inputs determine the available settings."
            : "Choose or inherit a results function first. Its required parameters and saved outputs determine the available settings."
        : IsReadOnly ? "These functions were fixed when this project’s first run was saved. Create a new project version to use different functions."
        : IsRun ? "Functions come from the parent project. Saved runs keep that project’s fixed function choices. Supply spans only for the required keys below."
        : "Functions are inherited from the module. Follow a source link to change a function there; this project can override its allowed defaults and source projects.";
    public string FunctionsStatus => !FunctionsReady && !IsModule
        ? "The inherited functions are missing, unavailable, or incompatible. Select the parent module and choose compatible catalog functions before configuring parameters and outputs."
        : IsBranch && !HasSetupContract ? "Choose a setup function to see compatible results functions."
        : !HasResultsContract ? "Choose a results function to configure this module."
        : FunctionCompatibilityError.Length > 0 ? "Choose a compatible setup/results pair to continue."
        : IsRun ? "Select parameter spans, outputs, or generated parameter sets in Configuration to continue."
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
            if (!FunctionCatalogs.resultsFunctionCatalog.TryGetValue(ResultsFunctionName, out var results) || results.SourceDataTypes.IsEmpty) return "";
            if (!IsBranch) return "This results function consumes setup data. Select it on a branch module, or choose a results function that generates its own data for this root module.";
            if (!HasSetupContract) return IsModule ? "" : "Select a setup function on the module that provides the results function’s input data.";
            var missing = results.SourceDataTypes.Where(required => !EffectiveSetupOutputs.Any(output => Equals(SortopiaDataTypeDto.toDomain(output), required))).ToArray();
            return missing.Length == 0 ? "" : $"The results function needs setup outputs: {string.Join(", ", missing.Select(value => value.ToString()))}. Choose a setup function on the module that saves these exact data types.";
        }
    }
    private resultsDataTypeConfigDto[]? DeclaredResults => FunctionCatalogs.resultsFunctionCatalog.TryGetValue(ResultsFunctionName, out var definition)
        ? definition.DataTypes.Select(entry => new resultsDataTypeConfigDto { dataType = SortopiaDataTypeDto.fromDomain(entry.Key), queryParameterKeys = entry.Value.ToArray() }).ToArray() : null;
    private resultsDataTypeConfigDto[]? DeclaredSetup => FunctionCatalogs.setupFunctionCatalog.TryGetValue(SetupFunctionName, out var definition)
        ? definition.DataTypes.Select(entry => new resultsDataTypeConfigDto { dataType = SortopiaDataTypeDto.fromDomain(entry.Key), queryParameterKeys = entry.Value.ToArray() }).ToArray() : null;
    private static bool SameType(sortopiaDataTypeDto left, sortopiaDataTypeDto right) => left.kind == right.kind && left.value == right.value;
    private bool NeedsSource(sortopiaDataTypeDto dataType) => FunctionConfiguration.requiresSource(SetupFunctionName, SortopiaDataTypeDto.toDomain(dataType));
    private resultsDataTypeConfigDto[] EffectiveResultsDataTypes => frozenSettings?.resultsDataTypes ?? DeclaredResults
        ?? EffectiveOutputs.Select(type => new resultsDataTypeConfigDto { dataType = type, queryParameterKeys = EffectiveOutputKeys }).ToArray();
    public IEnumerable<WorkspaceOutputContractPreview> ResultsOutputContracts => EffectiveResultsDataTypes.Select(entry =>
        new WorkspaceOutputContractPreview(entry.dataType.kind, entry.dataType.value, string.Join(", ", entry.queryParameterKeys)));
    private setupDataTypeConfigDto[] ResolveSetupTypes(setupDataTypeConfigDto[] local)
    {
        if (DeclaredSetup is not { } outputs) return local;
        return outputs.Select(output => new setupDataTypeConfigDto {
            dataType = output.dataType, queryParameterKeys = output.queryParameterKeys,
            sourceProjectId = NeedsSource(output.dataType) ? local.FirstOrDefault(entry => SameType(entry.dataType, output.dataType))?.sourceProjectId ?? Guid.Empty : Guid.Empty
        }).ToArray();
    }
    private void RefreshContractRows()
    {
        if (DeclaredSetup is not { } outputs || IsRun || IsReadOnly) return;
        var local = SetupDataTypes.Select(row => row.ToDto()).ToArray();
        if (!local.Select(entry => (entry.dataType.kind, entry.dataType.value)).SequenceEqual(outputs.Select(entry => (entry.dataType.kind, entry.dataType.value))))
            Quietly(() => LoadSetupDataTypes(ResolveSetupTypes(SetupDataTypesOverride.IsOverridden ? local : Parent?.EffectiveSetupDataTypes ?? [])));
        Quietly(() => { foreach (var row in SetupDataTypes) row.SetContract(true, NeedsSource(row.DataType.ToDto()), IsModule,
            outputs.First(output => SameType(output.dataType, row.DataType.ToDto())).queryParameterKeys, Requirements(true)?.Select(key => key.Name).ToArray()); });
    }
}

public sealed record WorkspaceOutputContractPreview(string Kind, string Text, string ParameterKeys)
{
    public string TextSummary => Text.Length == 0 ? "Text: (empty)" : $"Text: {Text}";
}
