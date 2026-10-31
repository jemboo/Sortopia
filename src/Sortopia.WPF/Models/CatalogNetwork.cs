using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Networks;

namespace Sortopia.WPF.Models;

/// <summary>A catalog selection with its original stage boundaries and canonical within-stage order.</summary>
public sealed class CatalogNetwork
{
    private CatalogNetwork(sorterLibId id)
    {
        Id = id;
        Width = id.ChannelCount;
        Label = $"{Width} channels · {SorterLibVariant.toString(id.SorterVariant).Replace("Variant", "Variant ")}";
        var text = SorterLib.tryGet(id) ?? throw new InvalidOperationException("Missing catalog network.");
        var stages = SorterDataParse.parseTo2dCeArray(text.Value)
            .Select(ces => stage.create(ces, Width)).ToArray();
        Sequence = stageSequence.create(stages, Width);
        Comparators = stages.SelectMany(s => s.Ces).ToArray();
        StageLengths = Array.AsReadOnly(stages.Select(s => s.Ces.Length).ToArray());
    }

    public sorterLibId Id { get; }
    public string Label { get; }
    public int Width { get; }
    public int StageCount => StageLengths.Count;
    public int ComparatorCount => Comparators.Length;
    public IReadOnlyList<int> StageLengths { get; }
    internal stageSequence Sequence { get; }
    internal ce[] Comparators { get; }

    public static IReadOnlyList<CatalogNetwork> LoadAll() =>
        Array.AsReadOnly(SorterLib.ids().Select(id => new CatalogNetwork(id)).ToArray());
}
