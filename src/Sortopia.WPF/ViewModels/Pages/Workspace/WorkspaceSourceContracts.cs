using Sortopia.Functions;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>Adapt editable source settings to the shared query contract without resolving source setup or runs.</summary>
internal static class WorkspaceSourceContracts
{
    internal static bool HasOutput(WorkspaceSourceProject source, string kind, string text) =>
        source.Settings.resultsDataTypes.Any(output => output.dataType.kind == kind && output.dataType.value == text);

    internal static sourceQueryContract Resolve(WorkspaceSourceProject? source, string kind, string text)
    {
        var keys = source?.Settings.resultsDataTypes
            .FirstOrDefault(output => output.dataType.kind == kind && output.dataType.value == text)?.queryParameterKeys ?? [];
        return SourceContracts.resolve(keys);
    }
}
