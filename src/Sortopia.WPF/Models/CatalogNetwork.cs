using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Networks;
namespace Sortopia.WPF.Models;
public sealed class CatalogNetwork : NetworkDefinition
{
    private CatalogNetwork(sorterLibId id) : base($"{id.ChannelCount} channels · {SorterLibVariant.toString(id.SorterVariant).Replace("Variant", "Variant ")}", ReadSequence(id)) => Id = id;
    public sorterLibId Id { get; }
    private static stageSequence ReadSequence(sorterLibId id)
    {
        var text = SorterLib.tryGet(id) ?? throw new InvalidOperationException("Missing catalog network.");
        return stageSequence.create(SorterDataParse.parseTo2dCeArray(text.Value).Select(ces => stage.create(ces, id.ChannelCount)).ToArray(), id.ChannelCount);
    }
    public static IReadOnlyList<CatalogNetwork> LoadAll() => Array.AsReadOnly(SorterLib.ids().Select(id => new CatalogNetwork(id)).ToArray());
}
