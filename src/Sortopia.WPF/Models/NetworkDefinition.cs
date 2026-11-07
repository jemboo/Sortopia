using Sortopia.Sorting.Networks;
namespace Sortopia.WPF.Models;
/// <summary>Shared immutable network geometry for catalog and generated networks.</summary>
public class NetworkDefinition
{
    internal static NetworkDefinition FromSorter(string label, sorter sorter, int prefixLength = 0)
    {
        var ces = sorter.Ces;
        if (prefixLength < 0 || prefixLength > ces.Length) throw new ArgumentOutOfRangeException(nameof(prefixLength));
        var stages = new List<stage>();
        var current = new List<ce>();
        var occupied = new bool[sorter.ChannelCount];
        void Flush()
        {
            if (current.Count > 0) stages.Add(stage.create(current.ToArray(), sorter.ChannelCount));
            current.Clear();
            Array.Clear(occupied);
        }
        for (var i = 0; i < ces.Length; i++)
        {
            var comparator = ces[i];
            if (i == prefixLength || occupied[comparator.LowChannel] || occupied[comparator.HighChannel]) Flush();
            current.Add(comparator);
            occupied[comparator.LowChannel] = occupied[comparator.HighChannel] = true;
        }
        Flush();
        return new(label, stageSequence.create(stages.ToArray(), sorter.ChannelCount));
    }

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
