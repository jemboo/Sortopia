using Sortopia.Sorting.Networks;
namespace Sortopia.WPF.Models;
/// <summary>Shared immutable network geometry for catalog and generated networks.</summary>
public class NetworkDefinition
{
    public NetworkDefinition(string label, stageSequence sequence)
    {
        Label = label;
        Sequence = sequence;
        Width = sequence.ChannelCount;
        var stages = sequence.Stages;
        Comparators = stages.SelectMany(s => s.Ces).ToArray();
        StageLengths = Array.AsReadOnly(stages.Select(s => s.Ces.Length).ToArray());
    }
    public string Label { get; }
    public int Width { get; }
    public int StageCount => StageLengths.Count;
    public int ComparatorCount => Comparators.Length;
    public IReadOnlyList<int> StageLengths { get; }
    internal stageSequence Sequence { get; }
    internal ce[] Comparators { get; }
}
