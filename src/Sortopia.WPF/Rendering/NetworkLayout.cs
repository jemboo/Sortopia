using Sortopia.WPF.Models;

namespace Sortopia.WPF.Rendering;

public sealed record StageBand(double Left, double Right);
public sealed record ComparatorPosition(int Index, int StageIndex, double X, int Low, int High);

/// <summary>Geometry only. Overlapping comparator spans receive separate columns, as in Archean.</summary>
public sealed class NetworkLayout
{
    public const double LeftMargin = 58;
    public const double TopMargin = 62;
    public const double ChannelSpacing = 28;
    public static double ChannelY(double channel) => TopMargin + channel * ChannelSpacing;

    public NetworkLayout(NetworkTrace trace) : this(trace.Network, trace.DisplayStageCount) { }
    public NetworkLayout(NetworkDefinition network) : this(network, NetworkTrace.PreviewStages(network)) { }

    public NetworkLayout(NetworkDefinition network, NetworkCheckResult? usage, int mode) : this(Filter(network, usage, mode)) { }

    private NetworkLayout((NetworkDefinition Network, IReadOnlyList<int>? Indices) display)
        : this(display.Network, NetworkTrace.PreviewStages(display.Network), display.Indices) { }

    private NetworkLayout(NetworkDefinition network, int stageCount, IReadOnlyList<int>? originalIndices = null)
    {
        TotalStages = network.StageCount;
        TotalComparators = network.ComparatorCount;
        var bands = new List<StageBand>();
        var comparators = new List<ComparatorPosition>();
        var frameX = new double[network.StageLengths.Take(stageCount).Sum() + 1];
        frameX[0] = LeftMargin - 18;
        double left = LeftMargin;
        var offset = 0;
        for (var stage = 0; stage < stageCount; stage++)
        {
            int column = 0;
            var occupied = new List<ComparatorEndpoints>();
            var end = offset + network.StageLengths[stage];
            for (var index = offset; index < end; index++)
            {
                var comparator = network.Comparators[index];
                var ce = new ComparatorEndpoints(comparator.LowChannel, comparator.HighChannel);
                if (occupied.Any(other => ce.Low <= other.High && other.Low <= ce.High))
                {
                    column++;
                    occupied.Clear();
                }
                occupied.Add(ce);
                var x = left + 16 + column * 12;
                comparators.Add(new ComparatorPosition(originalIndices?[index] ?? index, stage, x, ce.Low, ce.High));
                frameX[index + 1] = x + 6;
            }
            var right = left + 36 + column * 12;
            bands.Add(new StageBand(left, right));
            offset = end;
            frameX[end] = right - 6;
            left = right;
        }
        Bands = bands.AsReadOnly();
        Comparators = comparators.AsReadOnly();
        FrameX = Array.AsReadOnly(frameX);
        CutoffX = left + 36;
        Width = CutoffX + (IsTruncated ? 310 : 0);
        Height = ChannelY(network.Width - 1) + 42;
    }

    private static (NetworkDefinition Network, IReadOnlyList<int>? Indices) Filter(NetworkDefinition network, NetworkCheckResult? usage, int mode)
    {
        if (usage is null || mode == 0 || usage.CeUses.Count != network.ComparatorCount)
            return (network, null);
        var offset = 0;
        var indices = new List<int>();
        var stages = network.Sequence.Stages.Select(stage =>
        {
            var start = offset;
            var original = stage.Ces;
            offset += original.Length;
            var keepStage = mode == 1 && Enumerable.Range(start, original.Length).Any(i => usage.CeUses[i] > 0);
            var ces = original.Where((ce, i) =>
            {
                var keep = mode == 1 ? keepStage : usage.CeUses[start + i] > 0;
                if (keep) indices.Add(start + i);
                return keep;
            }).ToArray();
            return Sortopia.Sorting.Networks.stage.create(ces, network.Width);
        }).Where(stage => stage.Ces.Length > 0).ToArray();
        var filtered = new NetworkDefinition(network.Label, Sortopia.Sorting.Networks.stageSequence.create(stages, network.Width));
        if (mode == 3)
        {
            filtered = NetworkTransforms.Compact(filtered);
            var queues = indices.GroupBy(i => (network.Comparators[i].LowChannel, network.Comparators[i].HighChannel))
                .ToDictionary(g => g.Key, g => new Queue<int>(g));
            indices = filtered.Comparators.Select(ce => queues[(ce.LowChannel, ce.HighChannel)].Dequeue()).ToList();
        }
        return (filtered, indices);
    }

    public IReadOnlyList<StageBand> Bands { get; }
    public int TotalStages { get; }
    public int TotalComparators { get; }
    public bool IsTruncated => Bands.Count < TotalStages;
    public IReadOnlyList<ComparatorPosition> Comparators { get; }
    public IReadOnlyList<double> FrameX { get; }
    public double CutoffX { get; }
    public double Width { get; }
    public double Height { get; }
}
