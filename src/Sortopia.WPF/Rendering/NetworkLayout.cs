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

    public NetworkLayout(NetworkTrace trace)
    {
        var bands = new List<StageBand>();
        var comparators = new List<ComparatorPosition>();
        var frameX = new double[trace.Comparators.Count + 1];
        frameX[0] = LeftMargin - 18;
        double left = LeftMargin;
        for (var stage = 0; stage < trace.Network.StageCount; stage++)
        {
            int column = 0;
            var occupied = new List<ComparatorEndpoints>();
            for (var index = trace.StageBoundaries[stage]; index < trace.StageBoundaries[stage + 1]; index++)
            {
                var ce = trace.Comparators[index];
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
            frameX[trace.StageBoundaries[stage + 1]] = right - 6;
            left = right;
        }
        Bands = bands.AsReadOnly();
        Comparators = comparators.AsReadOnly();
        FrameX = Array.AsReadOnly(frameX);
        Width = left + 36;
        Height = ChannelY(trace.Network.Width - 1) + 42;
    }

    public IReadOnlyList<StageBand> Bands { get; }
    public IReadOnlyList<ComparatorPosition> Comparators { get; }
    public IReadOnlyList<double> FrameX { get; }
    public double Width { get; }
    public double Height { get; }
}
