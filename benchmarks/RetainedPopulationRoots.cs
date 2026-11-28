// Benchmark-only roots for an external ClrMD walk. Never referenced by production.
using System;
using System.Text.Json;

namespace Sortopia.RetainedBench;

public sealed class PopulationRoots
{
    public object Snapshot = null!;
    public object[] Models = null!;
    public object[] Evaluations = null!;
    public object[] SeedModels = null!;
    public object[] SeedEvaluations = null!;
}

public static class HeapCheckpoint
{
    public static PopulationRoots? Current;

    public static void Pause(string label, object snapshot, object[] models, object[] evaluations,
        object[] seedModels, object[] seedEvaluations)
    {
        Current = new() { Snapshot = snapshot, Models = models, Evaluations = evaluations,
            SeedModels = seedModels, SeedEvaluations = seedEvaluations };
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        Console.WriteLine("HEAP_READY " + JsonSerializer.Serialize(new {
            Label = label, Pid = Environment.ProcessId, Members = models.Length,
            ManagedBytes = GC.GetTotalMemory(false), ServerGc = System.Runtime.GCSettings.IsServerGC
        }));
        Console.Out.Flush();
        if (Console.ReadLine() != "continue") throw new InvalidOperationException("Heap inspector disconnected.");
        Current = null;
    }
}
