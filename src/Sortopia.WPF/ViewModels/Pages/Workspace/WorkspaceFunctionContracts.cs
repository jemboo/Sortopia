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
        ? definition.DataTypes.Select(entry => new resultsDataTypeConfigDto { dataType = SortopiaDataTypeDto.fromDomain(entry.Key), queryParameterKeys = entry.Value.Select(key => key.Name).ToArray() }).ToArray() : null;
    private resultsDataTypeConfigDto[]? DeclaredSetup => FunctionCatalogs.setupFunctionCatalog.TryGetValue(SetupFunctionName, out var definition)
        ? definition.DataTypes.Select(entry => new resultsDataTypeConfigDto { dataType = SortopiaDataTypeDto.fromDomain(entry.Key), queryParameterKeys = entry.Value.Select(key => key.Name).ToArray() }).ToArray() : null;
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
            outputs.First(output => SameType(output.dataType, row.DataType.ToDto())).queryParameterKeys); });
    }
}

public sealed record WorkspaceOutputContractPreview(string Kind, string Text, string ParameterKeys)
{
    public string TextSummary => Text.Length == 0 ? "Text: (empty)" : $"Text: {Text}";
}
