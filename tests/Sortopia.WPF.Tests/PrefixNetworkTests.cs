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
        var expected = new NetworkCheck(candidate.Network.Width, null, useSimd: false, CancellationToken.None)
            .Evaluate(candidate.Network.ToSorter(), CancellationToken.None);
        Assert.Equal(expected.CeUses, candidate.Usage.CeUses);
        Assert.Equal(4, candidate.RawUsage.CeUses[0]);
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
    [InlineData(3, 513, false)]
    [InlineData(3, 513, true)]
    public void PreparedInputsAreReusedAcrossCompleteSortersWithDifferentPrefixes(int width, int? samples, bool simd)
    {
        var token = TestContext.Current.CancellationToken;
        ce[] prefix = [ce.create(0, 1), ce.create(1, 2), ce.create(0, 1)];
        var inputs = new NetworkCheck(width, samples, simd, token);
        var reference = new NetworkCheck(width, samples, useSimd: false, token);
        ce[][] prefixes = [prefix, [ce.create(0, width - 1)], []];
        for (var index = 0; index < 3; index++)
        {
            var generator = msceRandGen.create(rngFactory.SmxFactory, width, prefixes[index], true, 7);
            var model = simpleSorterModel.NewMsce(generator.MakeSorterModelFromIndex(index));
            var candidate = NetworkSearch.Prepare("prefixed", model, samples, token, inputs);
            var fullCheck = reference.Evaluate(candidate.RawNetwork.ToSorter(), token);
            Assert.Equal(samples ?? (1 << width), inputs.Count);
            Assert.Equal(fullCheck.Tested, candidate.RawUsage.Tested);
            Assert.Equal(fullCheck.Exhaustive, candidate.RawUsage.Exhaustive);
            Assert.Equal(fullCheck.Failed, candidate.RawUsage.Failed);
            Assert.Equal(samples, candidate.RequestedSamples);
            Assert.Null(candidate.RawUsage.Counterexample);
            Assert.Equal(fullCheck.CeUses, candidate.RawUsage.CeUses);
            Assert.Equal(prefixes[index].Length, candidate.RawUsage.FixedCes.Count(fixedCe => fixedCe));
            var replay = NetworkSearch.Prepare("replay", model, samples, token, inputs);
            Assert.Equal(candidate.RawUsage.CeUses, replay.RawUsage.CeUses);
            Assert.Equal(candidate.RawUsage.Failed, replay.RawUsage.Failed);
            Assert.Null(replay.RawUsage.Counterexample);
            var compactCheck = reference.Evaluate(candidate.Network.ToSorter(), token);
            Assert.Equal(compactCheck.CeUses, candidate.Usage.CeUses);
            Assert.Equal(compactCheck.Failed, candidate.Score.Failures);
        }
        var empty = sorter.create(Guid.NewGuid(), width, []);
        var expectedEmpty = reference.Evaluate(empty, token);
        var actualEmpty = inputs.Evaluate(empty, token);
        Assert.Equal(expectedEmpty.Failed, actualEmpty.Failed);
        Assert.Equal(inputs.Count, actualEmpty.Tested);
        Assert.Empty(actualEmpty.CeUses);
        int[] suffix = [Ce.toIndex(ce.create(0, 1))];
        var modelForCoverage = simpleSorterModel.NewMsce(msce.create(Guid.NewGuid(), width, [], suffix));
        Assert.Throws<ArgumentException>(() => NetworkSearch.Prepare("wrong coverage", modelForCoverage, samples == 1 ? 2 : 1, token, inputs));
        var otherWidth = width == 64 ? 63 : width + 1;
        var wrongWidth = simpleSorterModel.NewMsce(msce.create(Guid.NewGuid(), otherWidth, [], suffix));
        Assert.Throws<ArgumentException>(() => NetworkSearch.Prepare("wrong width", wrongWidth, samples, token, inputs));
        Assert.Throws<ArgumentException>(() => inputs.Evaluate(sorter.create(Guid.NewGuid(), otherWidth, []), token));
        Assert.Throws<OperationCanceledException>(() => new NetworkCheck(width, samples, simd, new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => inputs.Evaluate(
            empty, new CancellationToken(true)));
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
        var inputs = new NetworkCheck(width, 513, useSimd: false, token);
        var parent = NetworkSearch.Prepare(kind, model, 513, token, inputs);
        Assert.Equal(SimpleSorterModel.makeSorter(null, model).Ces.Length, parent.RawNetwork.ComparatorCount);
        Assert.Equal(prefix, parent.RawNetwork.Comparators.Take(prefix.Length));
        var parentCheck = inputs.Evaluate(parent.RawNetwork.ToSorter(), token);
        Assert.Equal(parentCheck.CeUses, parent.RawUsage.CeUses);
        var children = new List<SearchCandidate>();
        await NetworkSearch.NextGenerationAsync(parent, RandomNetwork.createMutator(model, 0.5), 1, 3, token,
            inputs: inputs, candidateEvaluated: (child, _) => { children.Add(child); return Task.CompletedTask; });
        Assert.Equal(3, children.Count);
        foreach (var child in children)
        {
            Assert.Equal(prefix, SimpleSorterModel.getPrefix(child.Model));
            Assert.Equal(prefix.Length, child.Usage.FixedCes.Count(fixedCe => fixedCe));
            Assert.Equal(513, child.RequestedSamples);
            var expected = inputs.Evaluate(child.RawNetwork.ToSorter(), token);
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
    public void PrefixOnlySorterIsEvaluatedExactlyOnce(bool simd)
    {
        ce[] prefix = [ce.create(0, 1), ce.create(1, 2)];
        var token = TestContext.Current.CancellationToken;
        var inputs = new NetworkCheck(3, null, simd, token);
        var complete = sorter.create(Guid.NewGuid(), 3, prefix);
        var actual = inputs.Evaluate(complete, token, includeCounterexample: false);
        var expected = new NetworkCheck(3, null, useSimd: false, token).Evaluate(complete, token);
        Assert.Equal(8, actual.Tested);
        Assert.Empty(actual.FixedCes); // Prefix ownership stays with the model and search, not the evaluator.
        Assert.Equal(expected.Failed, actual.Failed);
        Assert.Null(actual.Counterexample);
        Assert.DoesNotContain("first failing input", actual.Description);
        Assert.Equal(expected.CeUses, actual.CeUses);
        Assert.True(actual.Failed > 0); // Applying this prefix twice would incorrectly report success.
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    [InlineData(9, true)]
    [InlineData(20, false)]
    [InlineData(20, true)]
    public void CatalogPrefixIsMeasuredAgainstEveryOriginalInput(int width, bool simd)
    {
        var token = TestContext.Current.CancellationToken;
        var prefix = SorterDataParse.getCeArrayFromPrefixLib(prefixLibId.create(width, 1, prefixLibVariant.Prefix1)).Value;
        var inputs = new NetworkCheck(width, null, simd, token);
        var actual = inputs.Evaluate(sorter.create(Guid.NewGuid(), width, prefix), token);
        var expectedCount = 1 << width;
        Assert.Equal(expectedCount, inputs.Count);
        Assert.Equal(expectedCount, actual.Tested);
        // Each sorted output with its transition inside a compared pair has two original inputs.
        Assert.Equal(expectedCount - (width + 1 + width / 2), actual.Failed);
        Assert.True(actual.Exhaustive);
        Assert.All(actual.CeUses, count => Assert.Equal(expectedCount / 4, count));
        Assert.Empty(actual.FixedCes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiStageCatalogPrefixMatchesCompleteNetworkOracle(bool simd)
    {
        var token = TestContext.Current.CancellationToken;
        var prefix = SorterDataParse.getCeArrayFromPrefixLib(prefixLibId.create(8, 3, prefixLibVariant.PrefixA)).Value;
        var model = simpleSorterModel.NewMsce(msceRandGen.create(rngFactory.SmxFactory, 8, prefix, true, 5).MakeSorterModelFromIndex(0));
        var inputs = new NetworkCheck(8, null, simd, token);
        var candidate = NetworkSearch.Prepare("PrefixA", model, null, token, inputs);
        var expected = new NetworkCheck(8, null, useSimd: false, token)
            .Evaluate(candidate.RawNetwork.ToSorter(), token);
        Assert.Equal(256, expected.Tested);
        Assert.Equal(expected.Tested, candidate.RawUsage.Tested);
        Assert.Equal(expected.Failed, candidate.RawUsage.Failed);
        Assert.Equal(expected.CeUses, candidate.RawUsage.CeUses);
    }
}
