using Sortopia.Sorting.Networks;

namespace Sortopia.WPF.Models;

internal static class NetworkTransforms
{
    // Keep used CEs in their original stages; drop empty stages without compacting dependencies.
    public static NetworkDefinition RemoveUnusedCes(NetworkDefinition network, NetworkCheckResult result)
    {
        if (result.Tested <= 0 || result.CeUses.Count != network.ComparatorCount)
            throw new ArgumentException("Usage must describe the current network.", nameof(result));
        var offset = 0;
        var kept = network.Sequence.Stages.Select(originalStage =>
        {
            var ces = originalStage.Ces.Where(_ => result.CeUses[offset++] > 0).ToArray();
            return stage.create(ces, network.Width);
        }).Where(retainedStage => retainedStage.Ces.Length > 0).ToArray();
        return new NetworkDefinition(network.Label, stageSequence.create(kept, network.Width));
    }

    public static NetworkCheckResult? PreserveUsage(NetworkDefinition before, NetworkDefinition after,
        NetworkCheckResult? result, bool removeUnused)
    {
        if (result is null) return null;
        if (result.CeUses.Count != before.ComparatorCount)
            throw new ArgumentException("Usage must describe the original network.", nameof(result));
        // Compaction cannot reverse occurrences sharing endpoints. Keep a queue per pair
        // so repeated CEs retain their own counts even when independent CEs move around them.
        var counts = new Dictionary<(int, int), Queue<int>>();
        for (var i = 0; i < before.ComparatorCount; i++)
        {
            var uses = result.CeUses[i];
            if (removeUnused && uses == 0) continue;
            var ce = before.Comparators[i];
            var key = (ce.LowChannel, ce.HighChannel);
            if (!counts.TryGetValue(key, out var queue)) counts[key] = queue = new Queue<int>();
            queue.Enqueue(uses);
        }
        var remapped = after.Comparators.Select(ce => counts[(ce.LowChannel, ce.HighChannel)].Dequeue()).ToArray();
        if (counts.Values.Any(queue => queue.Count != 0))
            throw new InvalidOperationException("Transformation did not retain the expected CEs.");
        return result with { CeUses = Array.AsReadOnly(remapped) };
    }

    public static NetworkDefinition Compact(NetworkDefinition network) =>
        new(network.Label, StageBuilderSequence.toStageSequence(network.Width, network.Comparators));
}
