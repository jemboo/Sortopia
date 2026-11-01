using System.Globalization;
using Microsoft.FSharp.Collections;
using Sortopia.Core;
using Sortopia.Sorting.Inputs;
using Sortopia.Storage;

namespace Sortopia.Runs;

public sealed record SortableTestsDataset(Guid ParameterId, SortableTestsRecipe Recipe);

public sealed record SortableTestsDirectRun(Guid Id, string Name, SortableTestsDataset[] Datasets, string Kind)
{
    internal const string KindName = "DirectRun.SortableTests";

    public override string ToString() => $"{Name} · {Datasets.Length} datasets · {Id.ToString("N")[..8]}";
}

public sealed partial class RunLibrary
{
    public SortableTestsDirectRun SaveSortableTestsDefinition(string name, IEnumerable<SortableTestsRecipe> recipes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var datasets = recipes.Select(recipe => new SortableTestsDataset(Guid.NewGuid(), recipe)).ToArray();
        var run = new SortableTestsDirectRun(Guid.NewGuid(), name.Trim(), datasets, SortableTestsDirectRun.KindName);
        ValidateDefinition(run, run.Id);
        store.SaveRunDefinition(run.Id, run);
        return run;
    }

    private static void ValidateDefinition(SortableTestsDirectRun run, Guid id)
    {
        if (run is null || run.Kind != SortableTestsDirectRun.KindName || run.Id != id || id == Guid.Empty)
            throw new InvalidDataException($"Expected a sortable-tests DirectRun with ID {id}.");
        ArgumentException.ThrowIfNullOrWhiteSpace(run.Name);
        if (run.Datasets is not { Length: > 0 }) throw new ArgumentException("Add at least one dataset to the run.");
        var ids = new HashSet<Guid>();
        foreach (var row in run.Datasets)
        {
            if (row?.Recipe == null || row.ParameterId == Guid.Empty || !ids.Add(row.ParameterId))
                throw new InvalidDataException("Each dataset needs a recipe and a unique parameter ID.");
            row.Recipe.Validate();
        }
    }

    public SortableTestsDirectRun LoadSortableTestsDefinition(Guid id)
    {
        var run = store.LoadRunDefinition<SortableTestsDirectRun>(id);
        ValidateDefinition(run, id);
        return run;
    }

    public SortableTestsDirectRun[] SortableTestsDefinitions() =>
        store.ListRunDefinitionIds().Select(LoadSortableTestsDefinition).OrderBy(run => run.Name).ThenBy(run => run.Id).ToArray();

    public bool HasSortableTestsParameters(SortableTestsDirectRun definition) =>
        store.ListParameters(definition.Id).Select(p => p.ParameterId).ToHashSet()
            .SetEquals(definition.Datasets.Select(d => d.ParameterId));

    public void GenerateSortableTestsParameters(Guid id, CancellationToken cancellation = default)
    {
        var run = LoadSortableTestsDefinition(id);
        var existing = store.ListParameters(id).ToHashSet();
        foreach (var row in run.Datasets)
        {
            cancellation.ThrowIfCancellationRequested();
            var reference = new RunParameterRef(id, row.ParameterId);
            if (existing.Contains(reference)) continue;
            try { store.SaveParameters(reference, ToParameters(row.Recipe)); }
            catch (IOException) when (store.ListParameters(id).Contains(reference)) { }
        }
    }

    private static FSharpMap<string, string> ToParameters(SortableTestsRecipe recipe)
    {
        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        Dictionary<string, string> fields = recipe switch
        {
            MergeTests m => new() { ["generator"] = "merge", ["channels"] = Number(m.Channels), ["ways"] = Number(m.Ways) },
            PrefixTests p => new() { ["generator"] = "prefix", ["channels"] = Number(p.Channels),
                ["stages"] = Number(p.Stages), ["variant"] = p.Variant },
            _ => throw new ArgumentException("Unknown sortable-tests generator.")
        };
        return MapModule.OfSeq(fields.Select(field => Tuple.Create(field.Key, field.Value)));
    }

    private static SortableTestsRecipe FromParameters(FSharpMap<string, string> fields)
    {
        int Number(string key) => int.Parse(fields[key], NumberStyles.Integer, CultureInfo.InvariantCulture);
        SortableTestsRecipe recipe = fields["generator"] switch
        {
            "merge" when fields.Count == 3 => new MergeTests(Number("channels"), Number("ways")),
            "prefix" when fields.Count == 4 => new PrefixTests(Number("channels"), Number("stages"), fields["variant"]),
            _ => throw new InvalidDataException("Expected merge parameters (channels, ways) or prefix parameters (channels, stages, variant).")
        };
        recipe.Validate();
        return recipe;
    }

    private (SortableTestsRecipe Recipe, ResultRef Result) ReadWorkItem(RunParameterRef parameters)
    {
        try
        {
            var fields = store.LoadParameters(parameters);
            var recipe = FromParameters(fields);
            // Bind the output identity to the producer and the values read from its file.
            var identity = new List<object> { SortableTestsDirectRun.KindName, parameters.ParameterId };
            foreach (var (key, value) in fields) { identity.Add(key); identity.Add(value); }
            return (recipe, new(parameters, Storage.outputDataType.SortableTestsWithPrefix, GuidUtils.guidFromObjs(identity)));
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or KeyNotFoundException
            or InvalidDataException or System.Text.Json.JsonException)
        {
            throw new InvalidDataException($"Invalid runParameter file {parameters.ParameterId:N}. {error.Message}", error);
        }
    }

    public CompletedRun ExecuteSortableTests(Guid id, bool publishAll = false, CancellationToken cancellation = default,
        IProgress<string>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var run = LoadSortableTestsDefinition(id);
        if (!HasSortableTestsParameters(run))
            throw new InvalidOperationException("Generate all runParameter files for this definition before executing.");
        var existing = store.ListResults(id).ToDictionary(r => r.Parameters);
        var outputs = new List<RunOutput>();
        foreach (var row in run.Datasets)
        {
            cancellation.ThrowIfCancellationRequested();
            var (recipe, target) = ReadWorkItem(new(id, row.ParameterId));
            progress?.Report($"Dataset {outputs.Count + 1} of {run.Datasets.Length} · {recipe}");
            ResultData.SortableTestsWithPrefix data;
            if (existing.TryGetValue(target.Parameters, out var saved))
            {
                if (!saved.Equals(target))
                    throw new InvalidDataException($"Parameters {row.ParameterId:N} have changed since their Result was saved. Create a new run for the changed settings.");
                data = (ResultData.SortableTestsWithPrefix)store.LoadResult(target);
            }
            else
            {
                var (tests, prefix) = recipe.Generate(target.DataId);
                cancellation.ThrowIfCancellationRequested();
                data = (ResultData.SortableTestsWithPrefix)ResultData.NewSortableTestsWithPrefix(tests, prefix);
                try { store.SaveResult(target.Parameters, data); }
                catch (IOException) when (store.ListResults(id).Contains(target))
                {
                    data = (ResultData.SortableTestsWithPrefix)store.LoadResult(target);
                }
            }
            outputs.Add(new(target.DataId, recipe, SortableTests.getSortableCount(data.Item1), ParameterId: row.ParameterId));
        }

        cancellation.ThrowIfCancellationRequested();
        var directory = RunPath(id);
        var inventory = Path.Combine(directory, "inventory.json");
        WriteNew(inventory, new CompletedRun(id, run.Name, DateTimeOffset.UtcNow, outputs.ToArray()), ifMissing: true);
        var completed = Read<CompletedRun>(inventory);
        if (publishAll)
        {
            var manifest = ManifestDesigner.AllOutputs(completed) with { Id = id };
            SaveManifest(directory, manifest, ifMissing: true);
        }
        return completed;
    }
}
