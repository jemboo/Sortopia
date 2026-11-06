using System.Windows;

namespace Sortopia.WPF.Rendering.AnimationSchemes;

/// <summary>Exchanges elements by sliding them along the vertical CE axis.</summary>
public sealed class CeSliderScheme : CeAnimationScheme
{
    public static CeSliderScheme Instance { get; } = new();

    protected override Point SwapPosition(double x, double fromY, double toY, double progress) =>
        new(x, fromY + (toY - fromY) * progress);

    public override double HorizontalExcursion(double verticalDistance) => 0;
}
