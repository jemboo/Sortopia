using System.Windows;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;

namespace Sortopia.WPF.Models;

internal readonly record struct SorterScore(int Failures, int StageCount, int ComparatorCount)
{
    private int SizeCost => 10 * ComparatorCount + 11 * StageCount;
    internal Point Position => new(ComparatorCount, StageCount);
    internal bool Improves(SorterScore other) => Failures < other.Failures
        || (Failures == other.Failures && SizeCost < other.SizeCost);
}

// Mutate the family model, and score and display its tested, compacted network.
internal sealed record SearchCandidate(simpleSorterModel Model, NetworkDefinition Network, NetworkCheckResult Usage)
{
    internal SorterScore Score => new(Usage.Failed, Network.StageCount, Network.ComparatorCount);
    internal Point Position => Score.Position;
    internal bool Improves(SearchCandidate other) => Score.Improves(other.Score);
}

internal static class NetworkSearch
{
    public static SearchCandidate Prepare(string label, simpleSorterModel model, int? samples, CancellationToken token,
        SimdNetworkCheck? simd = null)
    {
        token.ThrowIfCancellationRequested();
        var raw = new NetworkDefinition(label, RandomNetwork.toStages(model));
        var check = simd?.Run(raw, token) ?? NetworkCheck.Run(raw, samples, token);
        token.ThrowIfCancellationRequested();
        var used = NetworkTransforms.RemoveUnusedCes(raw, check);
        token.ThrowIfCancellationRequested();
        var compact = NetworkTransforms.Compact(used);
        token.ThrowIfCancellationRequested();
        var usage = NetworkTransforms.PreserveUsage(raw, compact, check, removeUnused: true)!;
        return new(model, compact, usage);
    }

    public static async Task<SearchCandidate> NextGenerationAsync(SearchCandidate parent, simpleSorterModelMutator mutator,
        int generation, int children, CancellationToken token, Func<SorterScore, int, Task>? childEvaluated = null,
        SimdNetworkCheck? simd = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(children);
        int? samples = parent.Usage.Exhaustive ? null : parent.Usage.Tested;
        var best = parent;
        for (var child = 0; child < children; child++)
        {
            token.ThrowIfCancellationRequested();
            var childIndex = child;
            var candidate = await Task.Run(() =>
            {
                var mutant = SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(mutator, parent.Model, childIndex, generation);
                return Prepare(parent.Network.Label, mutant, samples, token, simd);
            }, token);
            token.ThrowIfCancellationRequested();
            if (candidate.Improves(best)) best = candidate;
            if (childEvaluated is not null) await childEvaluated(candidate.Score, child + 1);
        }
        token.ThrowIfCancellationRequested();
        return best;
    }
}
