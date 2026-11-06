using System.Windows;

namespace Sortopia.WPF.Rendering.AnimationSchemes;

/// <summary>Exchanges elements with a half-turn about the CE midpoint.</summary>
public sealed class CeSpinnerScheme : CeAnimationScheme
{
    public static CeSpinnerScheme Instance { get; } = new();

    protected override Point SwapPosition(double x, double fromY, double toY, double progress)
    {
        var angle = Math.PI * progress;
        var midpoint = (fromY + toY) / 2;
        var radius = (fromY - toY) / 2;
        return new Point(x - radius * Math.Sin(angle), midpoint + radius * Math.Cos(angle));
    }

    public override double HorizontalExcursion(double verticalDistance) => Math.Abs(verticalDistance) / 2;
}
