using System.Runtime.InteropServices;
using Sortopia.Sorting.Networks;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class SorterFingerprintTests
{
    private static NetworkDefinition Network(string label, int width, params ce[][] stages) =>
        new(label, stageSequence.create(stages.Select(ces => stage.create(ces, width)).ToArray(), width));

    [Fact]
    public void Fingerprint_normalizes_stage_packing_and_CE_order_and_ignores_labels()
    {
        var a = ce.create(0, 1);
        var b = ce.create(2, 3);
        var together = Network("together", 4, new[] { a, b });
        var reordered = Network("different label", 4, new[] { b }, Array.Empty<ce>(), new[] { a });
        Assert.Equal(SorterFingerprint.Create(together), SorterFingerprint.Create(reordered));
        Assert.Equal(SorterFingerprint.FromPacked(together.Sequence), SorterFingerprint.Create(reordered));
        Assert.Equal(16, Marshal.SizeOf<SorterFingerprint>());
        Assert.Equal(new SorterFingerprint(0x07a7f628c5d4df52UL, 0xd8a3c9140e37d87cUL), SorterFingerprint.Create(together));
    }

    [Fact]
    public void Fingerprint_preserves_width_dependencies_and_repeated_CEs()
    {
        var a = ce.create(0, 1);
        var b = ce.create(1, 2);
        var forward = Network("forward", 4, new[] { a }, new[] { b });
        var reverse = Network("reverse", 4, new[] { b }, new[] { a });
        Assert.NotEqual(SorterFingerprint.Create(forward), SorterFingerprint.Create(reverse));
        Assert.NotEqual(SorterFingerprint.Create(forward), SorterFingerprint.Create(Network("width", 5, new[] { a }, new[] { b })));
        Assert.NotEqual(SorterFingerprint.Create(Network("one", 4, new[] { a })),
            SorterFingerprint.Create(Network("two", 4, new[] { a }, new[] { a })));
    }

    [Fact]
    public async Task Run_tracker_detects_repeats_across_generations_and_resets_for_another_run()
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Setup.Generator.ChannelCount = 2;
        vm.Setup.Generator.CountText = "8";
        var recipe = vm.Setup.Generator.Snapshot();
        var parent = NetworkSearch.Prepare(recipe.Label, recipe.CreateModel(), null, CancellationToken.None);
        var tracker = new SgdDuplicateTracker();
        Assert.False(tracker.Observe(parent.Network));
        var mutator = vm.Evolution.Mutation.Create(parent.Model);
        var observed = new List<SearchCandidate>();
        for (var generation = 1; generation <= 2; generation++)
            parent = await NetworkSearch.NextGenerationAsync(parent, mutator, generation, 4, CancellationToken.None,
                candidateEvaluated: (candidate, _) => { observed.Add(candidate); return Task.CompletedTask; }, duplicates: tracker);
        Assert.Equal(1, tracker.UniqueCount);
        Assert.Equal(8, tracker.DuplicateCount);
        Assert.Equal(8, observed.Count);
        Assert.All(observed, candidate =>
        {
            Assert.True(candidate.IsDuplicate);
            var copy = candidate with { };
            Assert.Equal(parent.Fingerprint, candidate.Fingerprint);
            Assert.Equal(copy, candidate);
        });
        var nextRun = new SgdDuplicateTracker();
        Assert.False(nextRun.Observe(parent.Network));
        Assert.Equal(0, nextRun.DuplicateCount);
    }
}
