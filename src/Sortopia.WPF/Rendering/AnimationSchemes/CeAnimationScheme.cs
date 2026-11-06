using System.Windows;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.Rendering.AnimationSchemes;

/// <summary>Shared approach, pause, swap and departure timing for recorded CE playback.</summary>
public abstract class CeAnimationScheme
{
    private const double Arrival = 0.3;
    private const double SwapStart = 0.45;
    private const double SwapEnd = 0.9;

    internal double PlayheadX(NetworkLayout geometry, NetworkTrace trace, int from, int to, double fraction)
    {
        if (from == to || trace.SwapCounts[from] == 0)
            return Interpolate(geometry.FrameX[from], geometry.FrameX[to], Ease(fraction));

        var ceX = geometry.Comparators[from].X;
        if (fraction < Arrival)
            return Interpolate(geometry.FrameX[from], ceX, Ease(fraction / Arrival));
        if (fraction <= SwapEnd) return ceX;
        return Interpolate(ceX, geometry.FrameX[to], Ease((fraction - SwapEnd) / (1 - SwapEnd)));
    }

    internal Point TokenPosition(double x, double fromY, double toY, double fraction) =>
        SwapPosition(x, fromY, toY, Ease((fraction - SwapStart) / (SwapEnd - SwapStart)));

    protected abstract Point SwapPosition(double x, double fromY, double toY, double progress);

    /// <summary>Maximum horizontal distance from the CE axis needed by a swap.</summary>
    public abstract double HorizontalExcursion(double verticalDistance);

    private static double Ease(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Interpolate(double from, double to, double fraction) => from + (to - from) * fraction;
}
