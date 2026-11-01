using System.Text.Json;
using System.Text.Json.Serialization;
using Sortopia.Sorting.Inputs;

namespace Sortopia.Runs;

public enum OutputDataType { SortableTestsWithPrefix, SorterModelSet, SorterSetEval }

public sealed record RunOutput(Guid ArtifactId, RunRecipe Recipe, int TestCount, EvalSummary? Evaluation = null, Guid? ParameterId = null)
{
    public OutputDataType DataType => Recipe switch
    {
        SortableTestsRecipe => OutputDataType.SortableTestsWithPrefix,
        ModelSetRecipe => OutputDataType.SorterModelSet,
        EvalRecipe => OutputDataType.SorterSetEval,
        _ => throw new InvalidDataException("Unknown run output.")
    };
    public override string ToString() => Recipe switch
    {
        SortableTestsRecipe => $"{Recipe} · {TestCount:N0} tests",
        ModelSetRecipe models => models.ToString(),
        _ => $"{Evaluation} · {TestCount:N0} tests per model"
    };
}

public sealed record CompletedRun(Guid Id, string Name, DateTimeOffset CompletedAt, RunOutput[] Outputs)
{
    public override string ToString() => $"{Name} · {CompletedAt.LocalDateTime:g} · {Id.ToString("N")[..8]}";
}

public sealed record ManifestEntry(OutputDataType DataType, int Index, Guid ArtifactId);
public sealed record OutputManifest(Guid Id, Guid RunId, string Name, ManifestEntry[] Entries)
{
    public override string ToString() => $"{Name} · {Entries.Length} slots · {Id.ToString("N")[..8]}";
}

public static class ManifestDesigner
{
    public static OutputManifest Create(CompletedRun source, string name, IEnumerable<ManifestEntry> selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var entries = selection.OrderBy(entry => entry.DataType).ThenBy(entry => entry.Index).ToArray();
        if (entries.Length == 0) throw new ArgumentException("Choose at least one output.");
        var outputs = source.Outputs.ToDictionary(output => output.ArtifactId);
        var slots = new HashSet<(OutputDataType, int)>();
        foreach (var entry in entries)
        {
            if (entry.Index < 0) throw new ArgumentException("Slot indices must be zero or greater.");
            if (!slots.Add((entry.DataType, entry.Index)))
                throw new ArgumentException($"Duplicate slot {entry.DataType}[{entry.Index}].");
            if (!outputs.TryGetValue(entry.ArtifactId, out var output) || output.DataType != entry.DataType)
                throw new ArgumentException($"The run has no {entry.DataType} output with ID {entry.ArtifactId}.");
        }
        return new(Guid.NewGuid(), source.Id, name.Trim(), entries);
    }

    public static OutputManifest AllOutputs(CompletedRun run) => Create(run, "All outputs",
        run.Outputs.GroupBy(output => output.DataType).SelectMany(group =>
            group.Select((output, index) => new ManifestEntry(group.Key, index, output.ArtifactId))));
}

/// <summary>Each completed directory contains an inventory, its datasets, and independent manifest files.</summary>
public sealed partial class RunLibrary(string directory)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    private readonly Storage.ProjectFileStore store = Storage.ProjectFileStore.FromRunsFolder(directory);
    private string RunPath(Guid id) => Path.Combine(DirectoryPath, id.ToString("N"));

    public CompletedRun Execute(string name, IEnumerable<RunRecipe> recipes, bool publishAll,
        CancellationToken cancellation = default, IProgress<string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var plan = recipes.ToArray();
        if (plan.Length == 0) throw new ArgumentException("Add at least one dataset to the run.");
        cancellation.ThrowIfCancellationRequested();
        if (plan.All(recipe => recipe is SortableTestsRecipe))
        {
            var definition = SaveSortableTestsDefinition(name, plan.Cast<SortableTestsRecipe>());
            GenerateSortableTestsParameters(definition.Id, cancellation);
            return ExecuteSortableTests(definition.Id, publishAll, cancellation, progress);
        }
        var id = Guid.NewGuid();
        var staging = Path.Combine(DirectoryPath, $".building-{id:N}");
        Directory.CreateDirectory(staging);
        try
        {
            var outputs = new List<RunOutput>();
            foreach (var recipe in plan)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report($"Output {outputs.Count + 1} of {plan.Length} · {recipe}");
                var artifactId = Guid.NewGuid();
                var (output, payload) = Produce(recipe, artifactId, cancellation, progress);
                cancellation.ThrowIfCancellationRequested();
                File.WriteAllBytes(Path.Combine(staging, $"{artifactId:N}.mpk"), payload);
                outputs.Add(output);
            }
            var run = new CompletedRun(id, name.Trim(), DateTimeOffset.UtcNow, outputs.ToArray());
            WriteNew(Path.Combine(staging, "inventory.json"), run);
            if (publishAll) SaveManifest(staging, ManifestDesigner.AllOutputs(run));
            cancellation.ThrowIfCancellationRequested();
            Directory.Move(staging, RunPath(id));
            return run;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public CompletedRun[] CompletedRuns() => Directory.Exists(DirectoryPath)
        ? Directory.EnumerateDirectories(DirectoryPath)
            .Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _) &&
                File.Exists(Path.Combine(path, "inventory.json")))
            .Select(path => Read<CompletedRun>(Path.Combine(path, "inventory.json")))
            .OrderByDescending(run => run.CompletedAt).ToArray()
        : [];

    public OutputManifest Publish(Guid runId, string name, IEnumerable<ManifestEntry> selection)
    {
        var directory = RunPath(runId);
        var run = Read<CompletedRun>(Path.Combine(directory, "inventory.json"));
        var manifest = ManifestDesigner.Create(run, name, selection);
        SaveManifest(directory, manifest);
        return manifest;
    }

    public OutputManifest[] Manifests(Guid runId)
    {
        var directory = Path.Combine(RunPath(runId), "manifests");
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json").Select(Read<OutputManifest>)
                .OrderBy(m => m.Name).ThenBy(m => m.Id).ToArray()
            : [];
    }

    public sortableTests LoadTests(Guid runId, Guid manifestId, int index)
    {
        return LoadTestsWithPrefix(new(runId, manifestId, index)).Tests;
    }

    private static void SaveManifest(string directory, OutputManifest manifest, bool ifMissing = false)
    {
        var manifests = Directory.CreateDirectory(Path.Combine(directory, "manifests"));
        WriteNew(Path.Combine(manifests.FullName, $"{manifest.Id:N}.json"), manifest, ifMissing);
    }

    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json)
        ?? throw new InvalidDataException($"Empty metadata in {path}.");

    private static void WriteNew<T>(string path, T value, bool ifMissing = false)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(value, Json));
            File.Move(temporary, path);
        }
        catch (IOException) when (ifMissing && File.Exists(path)) { }
        finally { File.Delete(temporary); }
    }
}
