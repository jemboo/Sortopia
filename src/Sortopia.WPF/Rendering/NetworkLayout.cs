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

    private NetworkLayout(NetworkDefinition network, int stageCount)
    {
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
                comparators.Add(new ComparatorPosition(index, stage, x, ce.Low, ce.High));
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
        Width = CutoffX + (stageCount < network.StageCount ? 310 : 0);
        Height = ChannelY(network.Width - 1) + 42;
    }

    public IReadOnlyList<StageBand> Bands { get; }
    public IReadOnlyList<ComparatorPosition> Comparators { get; }
    public IReadOnlyList<double> FrameX { get; }
    public double CutoffX { get; }
    public double Width { get; }
    public double Height { get; }
}
