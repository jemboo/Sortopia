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
    public NetworkDefinition Network { get; } = Network;
    internal NetworkDefinition RawNetwork { get; init; } = Network;
    internal NetworkCheckResult RawUsage { get; init; } = Usage;
    internal int? RequestedSamples => Usage.Exhaustive ? null : Usage.Tested;
    internal SorterFingerprint Fingerprint { get; } = SorterFingerprint.FromPacked(Network.Sequence);
    internal bool IsDuplicate { get; init; }
    internal SorterScore Score => new(Usage.Failed, Network.StageCount, Network.ComparatorCount);
    internal Point Position => Score.Position;
    internal bool Improves(SearchCandidate other) => Score.Improves(other.Score);
}

internal static class NetworkSearch
{
    public static SearchCandidate Prepare(string label, simpleSorterModel model, int? samples, CancellationToken token,
        NetworkCheck? inputs = null)
    {
        token.ThrowIfCancellationRequested();
        var prefix = SimpleSorterModel.getPrefix(model);
        var full = SimpleSorterModel.makeSorter(null, model);
        inputs ??= new NetworkCheck(full.ChannelCount, samples, useSimd: false, token);
        if (!inputs.Matches(full.ChannelCount, samples))
            throw new ArgumentException("Prepared inputs must have the same width and requested sample coverage as the model.", nameof(inputs));
        var checkedSequence = inputs.Evaluate(full, token, includeCounterexample: false);
        var raw = NetworkDefinition.FromSorter(label, full, prefix.Length);
        var check = NetworkTransforms.PreserveUsage(full.Ces, raw, checkedSequence, removeUnused: false, prefix.Length)!;
        token.ThrowIfCancellationRequested();
        var used = NetworkTransforms.RemoveUnusedCes(raw, check);
        token.ThrowIfCancellationRequested();
        var compact = NetworkTransforms.Compact(used);
        token.ThrowIfCancellationRequested();
        var usage = NetworkTransforms.PreserveUsage(raw, compact, check, removeUnused: true)!;
        return new(model, compact, usage) { RawNetwork = raw, RawUsage = check };
    }

    public static async Task<SearchCandidate> NextGenerationAsync(SearchCandidate parent, simpleSorterModelMutator mutator,
        int generation, int children, CancellationToken token, Func<SorterScore, int, Task>? childEvaluated = null,
        NetworkCheck? inputs = null, Func<SearchCandidate, int, Task>? candidateEvaluated = null,
        SgdDuplicateTracker? duplicates = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(children);
        var samples = parent.RequestedSamples;
        inputs ??= new NetworkCheck(parent.Network.Width, samples, useSimd: false, token);
        var best = parent;
        for (var child = 0; child < children; child++)
        {
            token.ThrowIfCancellationRequested();
            var childIndex = child;
            var candidate = await Task.Run(() =>
            {
                var mutant = SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(mutator, parent.Model, childIndex, generation);
                var prepared = Prepare(parent.Network.Label, mutant, samples, token, inputs);
                return duplicates is null ? prepared : prepared with { IsDuplicate = duplicates.Observe(prepared.Fingerprint) };
            }, token);
            token.ThrowIfCancellationRequested();
            if (candidate.Improves(best)) best = candidate;
            if (childEvaluated is not null) await childEvaluated(candidate.Score, child + 1);
            if (candidateEvaluated is not null) await candidateEvaluated(candidate, child + 1);
        }
        token.ThrowIfCancellationRequested();
        return best;
    }
}
