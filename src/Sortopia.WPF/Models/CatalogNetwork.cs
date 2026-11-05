using System.Windows;
using System.Windows.Media;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Networks;
namespace Sortopia.WPF.Models;
public sealed class CatalogNetwork : NetworkDefinition
{
    private CatalogNetwork(sorterLibId id) : base($"{id.ChannelCount} channels · {SorterLibVariant.toString(id.SorterVariant).Replace("Variant", "Variant ")}", ReadSequence(id))
    {
        Id = id;
        VariantName = SorterLibVariant.toString(id.SorterVariant).Replace("Variant", "Variant ");
        BestKnownTitle = $"{VariantName} · {StageCount} stages · {ComparatorCount} CEs";
        VariantBrush = id.SorterVariant.Tag switch
        {
            sorterLibVariant.Tags.VariantA => Brushes.DeepSkyBlue,
            sorterLibVariant.Tags.VariantB => Brushes.Gold,
            sorterLibVariant.Tags.VariantC => Brushes.Plum,
            sorterLibVariant.Tags.Batcher => Brushes.Turquoise,
            sorterLibVariant.Tags.BatcherBitonic => Brushes.Coral,
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
    }
    public sorterLibId Id { get; }
    public Point Position => new(ComparatorCount, StageCount);
    public string BestKnownTitle { get; }
    public string VariantName { get; }
    public Brush VariantBrush { get; }
    private static stageSequence ReadSequence(sorterLibId id)
    {
        var text = SorterLib.tryGet(id) ?? throw new InvalidOperationException("Missing catalog network.");
        return stageSequence.create(SorterDataParse.parseTo2dCeArray(text.Value).Select(ces => stage.create(ces, id.ChannelCount)).ToArray(), id.ChannelCount);
    }
    public static IReadOnlyList<CatalogNetwork> LoadAll() => Load(SorterLib.ids());
    public static IReadOnlyList<CatalogNetwork> LoadForWidth(int width) => Load(SorterLib.ids().Where(id => id.ChannelCount == width));
    private static IReadOnlyList<CatalogNetwork> Load(IEnumerable<sorterLibId> ids) =>
        Array.AsReadOnly(ids.Select(id => new CatalogNetwork(id)).ToArray());
}
