using Sortopia.Models.Sorting.Simple;
using Sortopia.Models.Sorting.Simple.Ce;
using Sortopia.Models.Sorting;
using Sortopia.Core;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Networks;
using Sortopia.WPF.Models;
using Sortopia.WPF.Rendering;
using Sortopia.WPF.ViewModels.Shared.Controls;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class PrefixNetworkTests
{
    [Fact]
    public void SearchRetainsUnusedPrefixComparatorsAndScoresTheCompleteCompactedNetwork()
    {
        ce[] prefix = [ce.create(0, 1), ce.create(0, 1)];
        int[] suffix = [Ce.toIndex(ce.create(0, 1)), Ce.toIndex(ce.create(2, 3))];
        var model = simpleSorterModel.NewMsce(msce.create(Guid.NewGuid(), 4, prefix, suffix));

        var candidate = NetworkSearch.Prepare("Prefixed", model, null, CancellationToken.None);

        Assert.Equal(4, candidate.RawNetwork.ComparatorCount);
        Assert.Equal(0, candidate.RawUsage.CeUses[1]);
        Assert.Equal(0, candidate.RawUsage.CeUses[2]);
        Assert.Equal(3, candidate.Network.ComparatorCount);
        Assert.Equal(2, candidate.Network.StageCount);
        Assert.Equal(2, candidate.Network.Comparators.Count(c => c.LowChannel == 0 && c.HighChannel == 1));
        Assert.Equal(3, candidate.Score.ComparatorCount);
        Assert.Equal(2, candidate.Score.StageCount);
        Assert.Equal(ReducedOracle(candidate.Network, prefix, candidate.Usage.FixedCes, null).CeUses, candidate.Usage.CeUses);
        Assert.Equal(prefix, SimpleSorterModel.getPrefix(candidate.Model));
        Assert.True(candidate.RawUsage.IsFixedCe(1));
        foreach (var mode in new[] { 1, 2, 3 })
        {
            var layout = new NetworkLayout(candidate.RawNetwork, candidate.RawUsage, mode);
            Assert.Contains(layout.Comparators, comparator => comparator.Index == 1);
            if (mode != 1) Assert.Equal(candidate.Network.ComparatorCount, layout.TotalComparators);
        }

        var shorter = simpleSorterModel.NewMsce(msce.create(Guid.NewGuid(), 4, [prefix[0]], suffix));
        var other = NetworkSearch.Prepare("Shorter prefix", shorter, null, CancellationToken.None);
        Assert.NotEqual(candidate.Fingerprint, other.Fingerprint);
    }

    [Theory]
    [InlineData(8, null, false)]
    [InlineData(8, null, true)]
    [InlineData(9, null, false)]
    [InlineData(9, null, true)]
    [InlineData(24, 513, false)]
    [InlineData(24, 513, true)]
    [InlineData(64, 1025, false)]
    [InlineData(64, 1025, true)]
    [InlineData(8, 1, true)]
    public void PresortedSuffixChecksCountEachDistinctPrefixOutputOnce(int width, int? samples, bool simd)
    {
        var token = TestContext.Current.CancellationToken;
        ce[] prefix = [ce.create(0, 1), ce.create(1, 2), ce.create(0, 1)];
        var generator = msceRandGen.create(rngFactory.SmxFactory, width, prefix, true, 7);
        var inputs = new PreparedNetworkCheck(width, samples, prefix, simd, token);
        // The prepared set owns its prefix, and evaluation must not mutate the cached inputs.
        prefix[0] = ce.create(0, width - 1);
        for (var index = 0; index < 3; index++)
        {
            var model = simpleSorterModel.NewMsce(generator.MakeSorterModelFromIndex(index));
            var candidate = NetworkSearch.Prepare("prefixed", model, samples, token, inputs);
            var fullCheck = ReducedOracle(candidate.RawNetwork, SimpleSorterModel.getPrefix(model), candidate.RawUsage.FixedCes, samples);
            Assert.Equal(fullCheck.Tested, candidate.RawUsage.Tested);
            Assert.Equal(fullCheck.Exhaustive, candidate.RawUsage.Exhaustive);
            Assert.Equal(fullCheck.Failed, candidate.RawUsage.Failed);
            Assert.True(candidate.RawUsage.UsesReducedInputs);
            Assert.Equal(samples, candidate.RequestedSamples);
            Assert.Null(candidate.RawUsage.Counterexample);
            Assert.Equal(fullCheck.CeUses, candidate.RawUsage.CeUses);
            Assert.Equal(3, candidate.RawUsage.FixedCes.Count(fixedCe => fixedCe));
            var replay = NetworkSearch.Prepare("replay", model, samples, token, inputs);
            Assert.Equal(candidate.RawUsage.CeUses, replay.RawUsage.CeUses);
            Assert.Equal(candidate.RawUsage.Failed, replay.RawUsage.Failed);
            Assert.Null(replay.RawUsage.Counterexample);
            var compactCheck = ReducedOracle(candidate.Network, SimpleSorterModel.getPrefix(model), candidate.Usage.FixedCes, samples);
            Assert.Equal(compactCheck.CeUses, candidate.Usage.CeUses);
            Assert.Equal(compactCheck.Failed, candidate.Score.Failures);
        }
        var wrong = simpleSorterModel.NewMsce(msceRandGen.create(rngFactory.SmxFactory, width, [], true, 7).MakeSorterModelFromIndex(0));
        Assert.Throws<ArgumentException>(() => NetworkSearch.Prepare("wrong prefix", wrong, samples, token, inputs));
        Assert.Throws<OperationCanceledException>(() => inputs.EvaluateSuffix(
            SimpleSorterModel.makeSorterWithoutPrefix(null, wrong), new CancellationToken(true)));
    }

    [Theory]
    [InlineData("msce", 9)]
    [InlineData("mssi", 9)]
    [InlineData("msrs", 8)]
    [InlineData("msuf4", 8)]
    [InlineData("msuf6", 12)]
    public async Task EveryFamilyDisplaysTheCompleteSorterAndChildrenKeepTheirPrefix(string kind, int width)
    {
        var token = TestContext.Current.CancellationToken;
        var prefix = SorterDataParse.getCeArrayFromPrefixLib(prefixLibId.create(width, 1, prefixLibVariant.Prefix1)).Value;
        var generator = SimpleSorterModelGen.makeUniform(rngFactory.SmxFactory, width, prefix, 3,
            SimpleSorterModelType.fromString(kind), true);
        var model = SimpleSorterModelGen.makeSorterModelFromIndex(0, generator);
        var inputs = new PreparedNetworkCheck(width, 513, prefix, useSimd: false, token);
        var parent = NetworkSearch.Prepare(kind, model, 513, token, inputs);
        Assert.Equal(SimpleSorterModel.makeSorterWithPrefix(null, model).Ces.Length, parent.RawNetwork.ComparatorCount);
        Assert.Equal(prefix, parent.RawNetwork.Comparators.Take(prefix.Length));
        Assert.Equal(ReducedOracle(parent.RawNetwork, prefix, parent.RawUsage.FixedCes, 513).CeUses, parent.RawUsage.CeUses);
        var children = new List<SearchCandidate>();
        await NetworkSearch.NextGenerationAsync(parent, RandomNetwork.createMutator(model, 0.5), 1, 3, token,
            inputs: inputs, candidateEvaluated: (child, _) => { children.Add(child); return Task.CompletedTask; });
        Assert.Equal(3, children.Count);
        foreach (var child in children)
        {
            Assert.Equal(prefix, SimpleSorterModel.getPrefix(child.Model));
            Assert.Equal(prefix.Length, child.Usage.FixedCes.Count(fixedCe => fixedCe));
            Assert.Equal(513, child.RequestedSamples);
            var expected = ReducedOracle(child.RawNetwork, prefix, child.RawUsage.FixedCes, 513);
            Assert.Equal(expected.Tested, child.RawUsage.Tested);
            Assert.Equal(expected.Failed, child.RawUsage.Failed);
            Assert.Equal(expected.CeUses, child.RawUsage.CeUses);
        }
    }

    [Fact]
    public void CatalogSelectionFollowsWidthAndSnapshotsRetainTheirChosenPrefix()
    {
        var settings = new NetworkGeneratorSettings(configureSimpleGenerator: true);
        Assert.Equal(new[] { "None", "Prefix1 · 1 stage", "PrefixA · 3 stages" }, settings.Prefix.Choices.Select(choice => choice.Name));
        settings.Prefix.Selected = settings.Prefix.Choices.Single(choice => choice.Key == "Prefix1/1");
        var recipe = settings.Snapshot();
        settings.ChannelCount = 9;
        Assert.Equal("Prefix1/1", settings.Prefix.Selected.Key);
        Assert.Equal(2, settings.Prefix.Choices.Count);
        Assert.Equal(4, settings.Prefix.Selected.Comparators.Length);
        Assert.Equal(8, SimpleSorterModel.getChannelCount(recipe.CreateModel()));
        Assert.Equal(recipe.Prefix, SimpleSorterModel.getPrefix(recipe.CreateModel()));
        settings.ChannelCount = 4;
        Assert.Equal("None", settings.Prefix.Selected.Name);
        Assert.Single(settings.Prefix.Choices);
        settings.ChannelCount = 24;
        Assert.Contains(settings.Prefix.Choices, choice => choice.Key == "PrefixB/4");
        settings.Prefix.Selected = settings.Prefix.Choices.Single(choice => choice.Key == "PrefixA/4");
        settings.ChannelCount = 32;
        Assert.Equal("PrefixA/4", settings.Prefix.Selected.Key);
        settings.ChannelCount = 16;
        Assert.Equal("None", settings.Prefix.Selected.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptySuffixUsesOnlyTheOnceAppliedPrefix(bool simd)
    {
        ce[] prefix = [ce.create(0, 1), ce.create(1, 2)];
        var token = TestContext.Current.CancellationToken;
        var inputs = new PreparedNetworkCheck(3, null, prefix, simd, token);
        var actual = inputs.EvaluateSuffix(sorter.create(Guid.NewGuid(), 3, []), token);
        var complete = NetworkDefinition.FromSorter("prefix only", sorter.create(Guid.NewGuid(), 3, prefix));
        var expected = ReducedOracle(complete, prefix, actual.FixedCes, null);
        Assert.Equal(expected.Failed, actual.Failed);
        Assert.Null(actual.Counterexample);
        Assert.DoesNotContain("first failing input", actual.Description);
        Assert.Equal(expected.CeUses, actual.CeUses);
        Assert.True(actual.Failed > 0); // Applying this prefix twice would incorrectly report success.
    }

    [Theory]
    [InlineData(8, 81, false)]
    [InlineData(8, 81, true)]
    [InlineData(9, 162, false)]
    [InlineData(9, 162, true)]
    [InlineData(20, 59049, false)]
    [InlineData(20, 59049, true)]
    public void Prefix1ExhaustiveInputsUseTheReducedCatalogSet(int width, int expectedCount, bool simd)
    {
        var token = TestContext.Current.CancellationToken;
        var prefix = SorterDataParse.getCeArrayFromPrefixLib(prefixLibId.create(width, 1, prefixLibVariant.Prefix1)).Value;
        var inputs = new PreparedNetworkCheck(width, null, prefix, simd, token);
        var actual = inputs.EvaluateSuffix(sorter.create(Guid.NewGuid(), width, []), token);
        Assert.Equal(expectedCount, inputs.Count);
        Assert.Equal(expectedCount, actual.Tested);
        Assert.Equal(expectedCount - width - 1, actual.Failed); // The width + 1 sorted binary sequences are all present.
        Assert.True(actual.Exhaustive);
        Assert.All(actual.CeUses, count => Assert.Equal(0, count));
        Assert.All(actual.FixedCes, fixedCe => Assert.True(fixedCe));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiStageCatalogPrefixMatchesDistinctOutputOracle(bool simd)
    {
        var token = TestContext.Current.CancellationToken;
        var prefix = SorterDataParse.getCeArrayFromPrefixLib(prefixLibId.create(8, 3, prefixLibVariant.PrefixA)).Value;
        var model = simpleSorterModel.NewMsce(msceRandGen.create(rngFactory.SmxFactory, 8, prefix, true, 5).MakeSorterModelFromIndex(0));
        var inputs = new PreparedNetworkCheck(8, null, prefix, simd, token);
        var candidate = NetworkSearch.Prepare("PrefixA", model, null, token, inputs);
        var expected = ReducedOracle(candidate.RawNetwork, prefix, candidate.RawUsage.FixedCes, null);
        Assert.True(expected.Tested < 256);
        Assert.Equal(expected.Tested, candidate.RawUsage.Tested);
        Assert.Equal(expected.Failed, candidate.RawUsage.Failed);
        Assert.Equal(expected.CeUses, candidate.RawUsage.CeUses);
    }

    internal static NetworkCheckResult ReducedOracle(NetworkDefinition network, ce[] prefix, IReadOnlyList<bool> fixedCes, int? samples)
    {
        var seen = new HashSet<string>();
        var random = samples is null ? null : new Random(NetworkCheck.SampleSeed);
        var input = new int[network.Width];
        var uses = new int[network.ComparatorCount];
        var failed = 0;
        for (var index = 0; index < NetworkCheck.InputCount(network.Width, samples); index++)
        {
            NetworkCheck.FillInput(input, index, random);
            foreach (var comparator in prefix)
                if (input[comparator.LowChannel] > input[comparator.HighChannel])
                    (input[comparator.LowChannel], input[comparator.HighChannel]) = (input[comparator.HighChannel], input[comparator.LowChannel]);
            if (!seen.Add(string.Concat(input))) continue;
            for (var ceIndex = 0; ceIndex < network.ComparatorCount; ceIndex++)
            {
                if (fixedCes[ceIndex]) continue;
                var comparator = network.Comparators[ceIndex];
                if (input[comparator.LowChannel] <= input[comparator.HighChannel]) continue;
                (input[comparator.LowChannel], input[comparator.HighChannel]) = (input[comparator.HighChannel], input[comparator.LowChannel]);
                uses[ceIndex]++;
            }
            if (!input.SequenceEqual(input.Order())) failed++;
        }
        return new(seen.Count, samples is null, null, failed, uses);
    }
}
