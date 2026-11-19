using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;

namespace Sortopia.WPF.ViewModels.Shared;

/// <summary>One refresh's display of exact saved setup inputs, shared by both file tables.</summary>
internal sealed class SavedSetupQueries(string projectFolder)
{
    private readonly Dictionary<storedDataReference, (string Values, string Details, parameterBinding[]? Bindings)> cache = [];
    private readonly Dictionary<Guid, SortopiaFileDb> stores = [];

    internal (string Values, string Details, SetupQueryData Queries) Read(runParameters item, CancellationToken token)
    {
        var queries = new SetupQueryData();
        if (item.SetupInputs.IsEmpty) return ("—", "These results do not consume setup files.", queries);
        if (!stores.TryGetValue(item.ProjectId, out var store))
            stores[item.ProjectId] = store = new SortopiaFileDb(projectFolder, item.ProjectId);
        var rows = new List<(string Values, string Details)>();
        foreach (var reference in item.SetupInputs.Distinct().OrderBy(input => SortopiaDataType.kindName(input.DataType), StringComparer.Ordinal)
            .ThenBy(input => SortopiaDataType.text(input.DataType), StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var text = SortopiaDataType.text(reference.DataType);
            var label = SortopiaDataType.kindName(reference.DataType) + (text.Length == 0 ? "" : $" · {text}");
            if (reference.Phase != runExecutionPhase.Setup || reference.ProjectId != item.ProjectId)
            {
                rows.Add(($"{label}: Unavailable", $"{label}: The input is not a setup file in this project."));
                queries.Inputs[reference.DataType] = null;
                continue;
            }
            if (!cache.TryGetValue(reference, out var row))
            {
                try
                {
                    var bindings = store.ReadQueryParameters(reference, token);
                    var values = bindings.IsEmpty ? "No query keys" : string.Join("; ", bindings.Select(binding => $"{binding.KeyName} = {binding.FormattedValue}"));
                    row = ($"{label}: {values}", $"{label}: {values}\nSetup file: {store.GetPath(reference)}", bindings.ToArray());
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { row = ($"{label}: Unavailable", $"{label}: {error.GetBaseException().Message}", null); }
                cache[reference] = row;
            }
            rows.Add((row.Values, row.Details));
            queries.Inputs[reference.DataType] = row.Bindings;
        }
        return (string.Join("\n", rows.Select(row => row.Values)),
            "Inherited setup query values from the saved input files.\n"
                + string.Join("\n\n", rows.Select(row => row.Details)), queries);
    }
}
