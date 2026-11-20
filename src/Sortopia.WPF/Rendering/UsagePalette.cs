using System.Globalization;
using System.Windows.Media;

namespace Sortopia.WPF.Rendering;

public sealed record UsageBand(double UpperPercent, string Label, Brush Fill, Brush Foreground, string Description);

/// <summary>Approximately logarithmic bands preserve detail in rarely used, later CEs.</summary>
public static class UsagePalette
{
    public static IReadOnlyList<UsageBand> Bands { get; } = CreateBands();
    public static UsageBand Prefix { get; } = CreatePrefix();
    public static IReadOnlyList<UsageBand> LegendBands { get; } = Array.AsReadOnly(Bands.Append(Prefix).ToArray());

    private static UsageBand CreatePrefix()
    {
        var fill = new SolidColorBrush(Color.FromRgb(240, 141, 214));
        fill.Freeze();
        return new(-1, "Prefix", fill, Brushes.Black,
            "Fixed model prefix. A usage count of -1 means it was already applied during input filtering and skipped in evaluation.");
    }

    private static IReadOnlyList<UsageBand> CreateBands()
    {
        double[] thresholds = [0, .001, .003, .01, .03, .1, .3, 1, 3, 10, 30, 100];
        string[] colors = ["#667784", "#6944A8", "#655BD3", "#4478DE", "#319ACB", "#39BFB8",
            "#58CB8C", "#A0D66D", "#DFD766", "#F4B457", "#EE8457", "#E35564"];
        var bands = thresholds.Select((upper, i) =>
        {
            var fill = (SolidColorBrush)new BrushConverter().ConvertFromString(colors[i])!;
            fill.Freeze();
            var number = upper.ToString("0.###", CultureInfo.InvariantCulture);
            var label = i == 0 ? "0%" : $"≤{number}%";
            var description = i == 0 ? "Unused: zero swaps in the tested inputs."
                : $"More than {thresholds[i - 1].ToString("0.###", CultureInfo.InvariantCulture)}% through {number}% of tested inputs caused a swap.";
            return new UsageBand(upper, label, fill, i <= 3 ? Brushes.White : Brushes.Black, description);
        }).ToArray();
        return Array.AsReadOnly(bands);
    }

    public static int Level(int uses, int tested)
    {
        if (uses <= 0 || tested <= 0) return 0;
        for (var i = 1; i < Bands.Count; i++)
            if ((decimal)uses * 100 <= (decimal)Bands[i].UpperPercent * tested) return i;
        return Bands.Count - 1;
    }
}
