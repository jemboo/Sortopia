using Sortopia.Sorting.Networks;
using Sortopia.WPF.Models;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class NetworkDefinitionTests
{
    [Fact]
    public void StoredSorterKeepsItsIdentityAndTheSuppliedStageBoundaries()
    {
        ce[] expected = [ce.create(2, 3), ce.create(0, 1)];
        var sequence = stageSequence.create(
        [
            stage.create([], 4),
            stage.create([expected[0]], 4),
            stage.create([], 4),
            stage.create([expected[1]], 4),
            stage.create([], 4)
        ], 4);
        var definition = new NetworkDefinition("native stages", sequence);
        var complete = definition.ToSorter();

        Assert.NotEqual(Guid.Empty, complete.SorterId);
        Assert.Equal(complete.SorterId, definition.ToSorter().SorterId);
        Assert.Equal(4, definition.Width);
        Assert.Equal(2, definition.ComparatorCount);
        Assert.Equal(expected, complete.Ces);
        Assert.Equal(new[] { 0, 1, 0, 1, 0 }, definition.StageLengths);
        Assert.Equal(sequence, definition.Sequence);

        // Exposed CE arrays cannot change the stored sorter or its relationship to the stages.
        var exposed = definition.Comparators;
        exposed[0] = ce.create(0, 2);
        Assert.Equal(expected, definition.Comparators);
        Assert.Equal(expected, definition.ToSorter().Ces);
        Assert.Equal(complete.SorterId, definition.ToSorter().SorterId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyNetworksKeepTheirWidthAndEmptyStages(int stageCount)
    {
        var sequence = stageSequence.create(Enumerable.Range(0, stageCount).Select(_ => stage.create([], 5)).ToArray(), 5);
        var definition = new NetworkDefinition("empty", sequence);
        var complete = definition.ToSorter();

        Assert.Equal(5, complete.ChannelCount);
        Assert.Empty(complete.Ces);
        Assert.Equal(0, definition.ComparatorCount);
        Assert.Equal(stageCount, definition.StageCount);
        Assert.All(definition.StageLengths, length => Assert.Equal(0, length));
        Assert.Equal(sequence, definition.Sequence);
        Assert.Equal(complete.SorterId, definition.ToSorter().SorterId);
    }

    [Fact]
    public void SorterImportKeepsThePrefixBoundaryAndUsesTheDisplayedComparatorOrder()
    {
        var source = sorter.create(Guid.NewGuid(), 8,
            [ce.create(2, 3), ce.create(0, 1), ce.create(6, 7), ce.create(4, 5)]);
        var definition = NetworkDefinition.FromSorter("prefix", source, prefixLength: 2);
        var complete = definition.ToSorter();

        Assert.Equal(new[] { 2, 2 }, definition.StageLengths);
        Assert.Equal(new[] { ce.create(0, 1), ce.create(2, 3), ce.create(4, 5), ce.create(6, 7) }, complete.Ces);
        Assert.Equal(definition.Sequence.Stages.SelectMany(stage => stage.Ces), complete.Ces);
        Assert.Equal(complete.SorterId, definition.ToSorter().SorterId);
    }
}
