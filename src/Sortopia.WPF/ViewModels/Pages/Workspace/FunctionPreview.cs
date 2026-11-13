using Sortopia.Functions;
using Sortopia.Runs;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record FunctionParameterPreview(string Name, string ValueType);

/// <summary>Read-only guidance from the highlighted function's catalog contract.</summary>
public sealed record FunctionPreview(string Description, FunctionParameterPreview[] RequiredParameters, string[] Outputs)
{
    public static FunctionPreview? Find(string name, bool setup) => setup
        ? SetupFunctionCatalog.functions.TryGetValue(name, out var setupDefinition) ? From(setupDefinition) : null
        : ResultsFunctionCatalog.functions.TryGetValue(name, out var resultsDefinition) ? From(resultsDefinition) : null;

    private static FunctionPreview From<T>(functionDefinition<T> definition) => new(definition.Description,
        definition.RequiredParameters.Select(parameter => new FunctionParameterPreview(parameter.Name, ParameterValue.label(parameter.ValueType))).ToArray(),
        definition.SortopiaDataTypes.Select(dataType => dataType.ToString()).ToArray());
}
