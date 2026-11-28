// External, suspended-process ClrMD census. Counts each reachable object once,
// using CLR-reported sizes; does not traverse getters or copy model arrays.
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

var start = new ProcessStartInfo(Path.GetFullPath(args[0])) {
    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardInput = true
};
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
if (args.Length > 4)
{
    foreach (var argument in JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[4]))!)
        start.ArgumentList.Add(argument);
}
else
{
    start.ArgumentList.Add(Path.GetFullPath(args[1]));
    start.ArgumentList.Add(output);
}
using var process = Process.Start(start)!;
try
{
    while (process.StandardOutput.ReadLine() is { } line)
    {
        Console.WriteLine(line);
        if (!line.StartsWith("HEAP_READY ")) continue;
        using var info = JsonDocument.Parse(line[11..]);
        string label = info.RootElement.GetProperty("Label").GetString()!;
        if (args.Length > 3 && label != args[3])
        {
            process.StandardInput.WriteLine("continue");
            process.StandardInput.Flush();
            continue;
        }
        var watch = Stopwatch.StartNew();
        using (var target = DataTarget.AttachToProcess(process.Id, suspend: true))
        using (var runtime = target.ClrVersions[0].CreateRuntime(
            Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "mscordaccore.dll")))
        {
            if (!runtime.Heap.CanWalkHeap) throw new InvalidOperationException("Heap is not walkable.");
            var type = runtime.EnumerateModules().Select(m => m.GetTypeByName("Sortopia.RetainedBench.HeapCheckpoint")).First(t => t != null)!;
            var holder = type.GetStaticFieldByName("Current")!.ReadObject(runtime.AppDomains[0]);
            if (holder.IsNull) throw new InvalidOperationException("Missing checkpoint roots.");
            var seen = new HashSet<ulong>();
            var categories = new Dictionary<string, object>();
            var types = new Dictionary<(string Category, string Type), (long Count, ulong Bytes)>();
            var names = new Dictionary<ulong, string>();
            (long Count, ulong Bytes) Walk(string category, IEnumerable<ClrObject> roots)
            {
                var todo = new Stack<ClrObject>();
                foreach (var root in roots) if (!root.IsNull && seen.Add(root.Address)) todo.Push(root);
                long count = 0;
                ulong bytes = 0;
                while (todo.TryPop(out var value))
                {
                    if (!value.IsValid) throw new InvalidOperationException($"Invalid object {value.Address:x}.");
                    ulong size = value.Size;
                    count++; bytes += size;
                    var valueType = value.Type!;
                    if (!names.TryGetValue(valueType.MethodTable, out var name)) names[valueType.MethodTable] = name = valueType.Name!;
                    var key = (category, name);
                    var old = types.GetValueOrDefault(key);
                    types[key] = (old.Count + 1, old.Bytes + size);
                    foreach (var child in value.EnumerateReferences(carefully: false, considerDependantHandles: false))
                        if (!child.IsNull && seen.Add(child.Address)) todo.Push(child);
                }
                categories[category] = new { Objects = count, Bytes = bytes, MiB = bytes / 1048576.0 };
                return (count, bytes);
            }
            IEnumerable<ClrObject> ArrayRoots(string field) => holder.ReadObjectField(field).EnumerateReferences(false, false);
            Walk("Models", ArrayRoots("Models"));
            Walk("Evaluations", ArrayRoots("Evaluations"));
            Walk("PopulationMetadata", new[] { holder.ReadObjectField("Snapshot") });
            ulong populationBytes = types.Values.Aggregate(0UL, (total, row) => total + row.Bytes);
            long populationObjects = seen.Count;
            Walk("AdditionalSeedModels", ArrayRoots("SeedModels"));
            Walk("AdditionalSeedEvaluations", ArrayRoots("SeedEvaluations"));
            var result = new {
                Label = label, Members = info.RootElement.GetProperty("Members").GetInt32(),
                PopulationBytes = populationBytes, PopulationObjects = populationObjects,
                PopulationAndSeedContentBytes = types.Values.Aggregate(0UL, (total, row) => total + row.Bytes),
                ProcessManagedBytes = info.RootElement.GetProperty("ManagedBytes").GetInt64(),
                Categories = categories,
                Types = types.OrderByDescending(x => x.Value.Bytes).Select(x => new { x.Key.Category, x.Key.Type, Objects = x.Value.Count, x.Value.Bytes })
            };
            File.WriteAllText(Path.Combine(output, label + ".json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"CENSUS {label}: {populationBytes / 1048576.0:N2} MiB, {populationObjects:N0} objects, {watch.Elapsed.TotalSeconds:N1}s");
        }
        process.StandardInput.WriteLine("continue");
        process.StandardInput.Flush();
    }
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"Target failed: {process.ExitCode}");
}
finally
{
    if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
}
