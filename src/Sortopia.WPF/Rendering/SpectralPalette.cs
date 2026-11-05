using System.Windows;
using System.Windows.Media;

namespace Sortopia.WPF.Rendering;

public static class SpectralPalette
{
    private const int ShadesPerBand = 64;
    private static readonly Color[] Stops = [Colors.Blue, Colors.Cyan, Colors.Lime, Colors.Yellow, Colors.Red];
    public static IReadOnlyList<SolidColorBrush> Shades { get; } = Array.AsReadOnly(
        Enumerable.Range(0, (Stops.Length - 1) * ShadesPerBand + 1).Select(CreateShade).ToArray());
    public static LinearGradientBrush Gradient { get; } = CreateGradient();

    private static SolidColorBrush CreateShade(int index)
    {
        var band = Math.Min(index / ShadesPerBand, Stops.Length - 2);
        var fraction = (double)index / ShadesPerBand - band;
        var from = Stops[band];
        var to = Stops[band + 1];
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * fraction);
        var brush = new SolidColorBrush(Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B)));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateGradient()
    {
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation };
        for (var i = 0; i < Stops.Length; i++)
            gradient.GradientStops.Add(new GradientStop(Stops[i], (double)i / (Stops.Length - 1)));
        gradient.Freeze();
        return gradient;
    }
}
