using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.Controls;

public sealed class EvolutionPlot : FrameworkElement
{
    public static readonly DependencyProperty VisitsProperty = DependencyProperty.Register(
        nameof(Visits), typeof(EvolutionVisits), typeof(EvolutionPlot),
        new FrameworkPropertyMetadata(EvolutionVisits.Empty, FrameworkPropertyMetadataOptions.AffectsRender, UpdateDomain));
    public static readonly DependencyProperty BestPositionProperty = DependencyProperty.Register(
        nameof(BestPosition), typeof(Point?), typeof(EvolutionPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, UpdateDomain));
    public static readonly DependencyProperty BestKnownNetworksProperty = DependencyProperty.Register(
        nameof(BestKnownNetworks), typeof(IReadOnlyList<CatalogNetwork>), typeof(EvolutionPlot),
        new FrameworkPropertyMetadata(Array.Empty<CatalogNetwork>(), FrameworkPropertyMetadataOptions.AffectsRender, UpdateDomain));
    public Point? BestPosition { get => (Point?)GetValue(BestPositionProperty); set => SetValue(BestPositionProperty, value); }
    public IReadOnlyList<CatalogNetwork> BestKnownNetworks
    {
        get => (IReadOnlyList<CatalogNetwork>?)GetValue(BestKnownNetworksProperty) ?? Array.Empty<CatalogNetwork>();
        set => SetValue(BestKnownNetworksProperty, value);
    }

    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth), typeof(double), typeof(EvolutionPlot), new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(
        nameof(HorizontalOffset), typeof(double), typeof(EvolutionPlot), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double HorizontalOffset { get => (double)GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }

    public EvolutionVisits Visits
    {
        get => (EvolutionVisits?)GetValue(VisitsProperty) ?? EvolutionVisits.Empty;
        set => SetValue(VisitsProperty, value);
    }

    private const int ShadesPerBand = 64;
    private static readonly Color[] Spectrum = [Colors.Blue, Colors.Cyan, Colors.Lime, Colors.Yellow, Colors.Red];
    private static readonly SolidColorBrush[] VisitColors = Enumerable.Range(0, (Spectrum.Length - 1) * ShadesPerBand + 1).Select(index =>
    {
        var band = Math.Min(index / ShadesPerBand, Spectrum.Length - 2);
        var fraction = (double)index / ShadesPerBand - band;
        var from = Spectrum[band];
        var to = Spectrum[band + 1];
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * fraction);
        var brush = new SolidColorBrush(Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B)));
        brush.Freeze();
        return brush;
    }).ToArray();

    public static LinearGradientBrush FrequencyLegend { get; } = CreateLegend();

    private static LinearGradientBrush CreateLegend()
    {
        var legend = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0),
            ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation };
        for (var i = 0; i < Spectrum.Length; i++)
            legend.GradientStops.Add(new GradientStop(Spectrum[i], (double)i / (Spectrum.Length - 1)));
        legend.Freeze();
        return legend;
    }

    private static readonly Typeface Font = new("Segoe UI");
    private static readonly Pen AxisPen = Stroke(Brushes.LightSlateGray, 1);
    private static readonly Brush LatticeDot = (Brush)new SolidColorBrush(Color.FromRgb(77, 106, 119)).GetAsFrozen();

    private const double PaddingUnits = 5;
    private const double TopInset = 12;
    private const double BottomInset = 42;
    private Rect domain = new(-PaddingUnits, -PaddingUnits, 2 * PaddingUnits, 2 * PaddingUnits);

    private static void UpdateDomain(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var plot = (EvolutionPlot)sender;
        var bounds = plot.Visits.Bounds;
        if (plot.BestPosition is { } best) bounds.Union(best);
        foreach (var known in plot.BestKnownNetworks) bounds.Union(known.Position);
        if (bounds.IsEmpty) bounds = new Rect(0, 0, 0, 0);
        bounds.Inflate(PaddingUnits, PaddingUnits);
        if (plot.domain == bounds) return;
        plot.domain = bounds;
        plot.InvalidateMeasure();
    }

    private double LabelWidth => Label(Math.Max(domain.Right, domain.Bottom).ToString("N0")).Width;

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 176;
        var unit = Math.Max(0, height - TopInset - BottomInset) / domain.Height;
        var width = domain.Width * unit + 2 * (32 + LabelWidth);
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var labelWidth = LabelWidth;
        var gutter = 32 + labelWidth;
        var unit = Math.Min((ActualWidth - 2 * gutter) / domain.Width, (ActualHeight - TopInset - BottomInset) / domain.Height);
        if (unit <= 0) return;
        var origin = (ActualWidth - domain.Width * unit) / 2;
        var top = TopInset + (ActualHeight - TopInset - BottomInset - domain.Height * unit) / 2;
        var bottom = top + domain.Height * unit;
        double X(double count) => origin + (count - domain.Left) * unit;
        double Y(double count) => bottom - (count - domain.Top) * unit;

        // Draw only the visible columns of wide lattices, keeping their axis labels in the viewport.
        var left = Math.Max(origin, HorizontalOffset + gutter);
        var right = Math.Min(X(domain.Right), HorizontalOffset + ViewportWidth - gutter);
        if (right <= left) return;
        var minX = domain.Left + (left - origin) / unit;
        var maxX = domain.Left + (right - origin) / unit;

        // Keep background dots distinguishable when several integer coordinates share a pixel.
        var latticeStep = IntegerStep(6 / unit);
        var labelStep = latticeStep * IntegerStep(Math.Max(32, labelWidth + 12) / (unit * latticeStep));
        for (var x = FirstTick(minX, maxX, latticeStep); x <= maxX; x += latticeStep)
            for (var y = FirstTick(domain.Top, domain.Bottom, latticeStep); y <= domain.Bottom; y += latticeStep)
                dc.DrawEllipse(LatticeDot, null, new Point(X(x), Y(y)), 1, 1);

        for (var tick = FirstTick(minX, maxX, labelStep); tick <= maxX; tick += labelStep)
        {
            var x = X(tick);
            dc.DrawLine(AxisPen, new Point(x, bottom), new Point(x, bottom + 3));
            CenterLabel(dc, tick.ToString("N0"), x, bottom + 11);
        }
        for (var tick = FirstTick(domain.Top, domain.Bottom, labelStep); tick <= domain.Bottom; tick += labelStep)
        {
            var y = Y(tick);
            dc.DrawLine(AxisPen, new Point(left - 3, y), new Point(left, y));
            var text = Label(tick.ToString("N0"));
            dc.DrawText(text, new Point(left - text.Width - 6, y - text.Height / 2));
        }
        dc.DrawLine(AxisPen, new Point(left, top), new Point(left, bottom));
        dc.DrawLine(AxisPen, new Point(left, bottom), new Point(right, bottom));
        var xCaption = latticeStep == 1 ? "CE count" : $"CE count   ·   Lattice spacing {latticeStep:N0}";
        CenterLabel(dc, xCaption, (left + right) / 2, bottom + 31);
        var midY = (top + bottom) / 2;
        var yCaptionX = left - gutter + 8;
        dc.PushTransform(new RotateTransform(-90, yCaptionX, midY));
        CenterLabel(dc, "Stage count", yCaptionX, midY);
        dc.Pop();

        var visits = Visits;
        var current = visits.Current;
        var best = BestPosition;
        var knownNetworks = BestKnownNetworks;
        if (current is null && best is null && knownNetworks.Count == 0)
        {
            CenterLabel(dc, "Generated sorters and evaluated children appear here.", (left + right) / 2, midY);
            return;
        }

        void DrawCell(Point position, Brush? fill, Pen? outline = null)
        {
            if (position.X + 0.5 < minX || position.X - 0.5 > maxX) return;
            var cell = new Rect(X(position.X - 0.5), Y(position.Y + 0.5), unit, unit);
            if (outline is not null) cell.Inflate(-outline.Thickness / 2, -outline.Thickness / 2);
            dc.DrawRectangle(fill, outline, cell);
        }
        dc.PushClip(new RectangleGeometry(new Rect(left, top, right - left, bottom - top)));
        var minimum = visits.Counts.IsEmpty ? 1 : visits.Counts.Values.Min();
        var logMinimum = Math.Log(minimum);
        var logRange = Math.Log(Math.Max(1, visits.Maximum)) - logMinimum;
        var lastShade = VisitColors.Length - 1;
        foreach (var (position, count) in visits.Counts)
        {
            if (position == current || position == best) continue;
            var shade = count == minimum ? 0 : count == visits.Maximum ? lastShade
                : Math.Clamp((int)Math.Round(lastShade * (Math.Log(count) - logMinimum) / logRange), 1, lastShade - 1);
            DrawCell(position, VisitColors[shade]);
        }
        if (best is { } bestCell && bestCell != current) DrawCell(bestCell, Brushes.LimeGreen);
        if (current is { } currentCell) DrawCell(currentCell, Brushes.HotPink);

        var thickness = Math.Min(2, unit / 5);
        var catalogOutline = new Pen(Brushes.LimeGreen, thickness) { DashStyle = DashStyles.Dash };
        foreach (var known in knownNetworks)
            if (known.Position != best) DrawCell(known.Position, null, catalogOutline);
        if (current is { } shared && shared == best)
            DrawCell(shared, null, new Pen(Brushes.LimeGreen, thickness));
        dc.Pop();
    }

    private static double FirstTick(double min, double max, double step)
    {
        var tick = Math.Ceiling(min / step) * step;
        return tick <= max ? tick : Math.Ceiling(min);
    }

    private static double IntegerStep(double minimum)
    {
        if (minimum <= 1) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(minimum)));
        return magnitude * ((minimum / magnitude) switch { <= 1 => 1, <= 2 => 2, <= 5 => 5, _ => 10 });
    }

    private FormattedText Label(string value) => new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        Font, 12, Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void CenterLabel(DrawingContext dc, string value, double x, double y)
    {
        var text = Label(value);
        dc.DrawText(text, new Point(x - text.Width / 2, y - text.Height / 2));
    }

    private static Pen Stroke(Brush brush, double width)
    {
        var pen = new Pen(brush, width);
        pen.Freeze();
        return pen;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
