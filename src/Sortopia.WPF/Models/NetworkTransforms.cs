using Sortopia.Sorting.Networks;

namespace Sortopia.WPF.Models;

internal static class NetworkTransforms
{
    // Keep used CEs in their original stages; drop empty stages without compacting dependencies.
    public static NetworkDefinition RemoveUnusedCes(NetworkDefinition network, NetworkCheckResult result, int prefixLength = 0)
    {
        if (prefixLength < 0 || prefixLength > network.ComparatorCount)
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        if (result.Tested <= 0 || result.CeUses.Count != network.ComparatorCount)
            throw new ArgumentException("Usage must describe the current network.", nameof(result));
        var offset = 0;
        var kept = network.Sequence.Stages.Select(originalStage =>
        {
            var ces = originalStage.Ces.Where(_ =>
            {
                var index = offset++;
                return index < prefixLength || result.IsFixedCe(index) || result.CeUses[index] > 0;
            }).ToArray();
            return stage.create(ces, network.Width);
        }).Where(retainedStage => retainedStage.Ces.Length > 0).ToArray();
        return new NetworkDefinition(network.Label, stageSequence.create(kept, network.Width));
    }

    public static NetworkCheckResult? PreserveUsage(NetworkDefinition before, NetworkDefinition after,
        NetworkCheckResult? result, bool removeUnused, int prefixLength = 0) =>
        PreserveUsage(before.Comparators, after, result, removeUnused, prefixLength);

    internal static NetworkCheckResult? PreserveUsage(ce[] before, NetworkDefinition after,
        NetworkCheckResult? result, bool removeUnused, int prefixLength = 0)
    {
        if (prefixLength < 0 || prefixLength > before.Length)
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        if (result is null) return null;
        if (result.CeUses.Count != before.Length)
            throw new ArgumentException("Usage must describe the original network.", nameof(result));
        // Compaction cannot reverse occurrences sharing endpoints. Keep a queue per pair
        // so repeated CEs retain their own counts even when independent CEs move around them.
        var counts = new Dictionary<(int, int), Queue<(int Uses, bool Fixed)>>();
        for (var i = 0; i < before.Length; i++)
        {
            var uses = result.CeUses[i];
            var isFixed = i < prefixLength || result.IsFixedCe(i);
            if (removeUnused && uses == 0 && !isFixed) continue;
            var ce = before[i];
            var key = (ce.LowChannel, ce.HighChannel);
            if (!counts.TryGetValue(key, out var queue)) counts[key] = queue = new();
            queue.Enqueue((uses, isFixed));
        }
        var remapped = after.Comparators.Select(ce => counts[(ce.LowChannel, ce.HighChannel)].Dequeue()).ToArray();
        if (counts.Values.Any(queue => queue.Count != 0))
            throw new InvalidOperationException("Transformation did not retain the expected CEs.");
        return result with
        {
            CeUses = Array.AsReadOnly(remapped.Select(value => value.Uses).ToArray()),
            FixedCes = Array.AsReadOnly(remapped.Select(value => value.Fixed).ToArray())
        };
    }

    public static NetworkDefinition Compact(NetworkDefinition network) =>
        new(network.Label, StageBuilderSequence.toStageSequence(network.Width, network.Comparators));
}
