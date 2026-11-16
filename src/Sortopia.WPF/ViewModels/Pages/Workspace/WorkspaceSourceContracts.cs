using Sortopia.Functions;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>Adapt editable source settings to the shared query contract without resolving source setup or runs.</summary>
internal static class WorkspaceSourceContracts
{
    internal static bool HasOutput(WorkspaceSourceProject source, string kind, string text) =>
        source.Settings.resultsSortopiaDataTypes.Any(type => type.kind == kind && type.value == text);

    internal static sourceQueryContract Resolve(WorkspaceSourceProject? source, string kind, string text)
    {
        // Keep support for the legacy project-wide projection while editing older drafts.
        var keys = source is null ? [] : source.Settings.resultsDataTypes?
            .FirstOrDefault(output => output.dataType.kind == kind && output.dataType.value == text)?.queryParameterKeys
            ?? source.Settings.resultsQueryParameterKeys ?? [];
        return SourceContracts.resolve(keys);
    }
}
