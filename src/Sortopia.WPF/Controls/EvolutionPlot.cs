using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Input;
using Sortopia.WPF.Models;
using Sortopia.WPF.Rendering;

namespace Sortopia.WPF.Controls;

public sealed class EvolutionPlot : FrameworkElement
{
    public static readonly DependencyProperty VisitsProperty = DependencyProperty.Register(
        nameof(Visits), typeof(EvolutionVisits), typeof(EvolutionPlot),
        new FrameworkPropertyMetadata(EvolutionVisits.Empty, FrameworkPropertyMetadataOptions.AffectsRender, UpdateDomain));
    public static readonly DependencyProperty AllBufferBoundsProperty = DependencyProperty.Register(
        nameof(AllBufferBounds), typeof(Rect?), typeof(EvolutionPlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, UpdateDomain));
    public Rect? AllBufferBounds { get => (Rect?)GetValue(AllBufferBoundsProperty); set => SetValue(AllBufferBoundsProperty, value); }
    public static readonly DependencyProperty FillHighlightMarkersProperty = DependencyProperty.Register(
        nameof(FillHighlightMarkers), typeof(bool), typeof(EvolutionPlot), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool FillHighlightMarkers { get => (bool)GetValue(FillHighlightMarkersProperty); set => SetValue(FillHighlightMarkersProperty, value); }
    public static readonly DependencyProperty ShowCurrentProperty = DependencyProperty.Register(
        nameof(ShowCurrent), typeof(bool), typeof(EvolutionPlot), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowCurrent { get => (bool)GetValue(ShowCurrentProperty); set => SetValue(ShowCurrentProperty, value); }
    public static readonly DependencyProperty SelectedPositionProperty = DependencyProperty.Register(
        nameof(SelectedPosition), typeof(Point?), typeof(EvolutionPlot),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, UpdateDomain));
    public Point? SelectedPosition { get => (Point?)GetValue(SelectedPositionProperty); set => SetValue(SelectedPositionProperty, value); }
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

    public EvolutionVisits Visits
    {
        get => (EvolutionVisits?)GetValue(VisitsProperty) ?? EvolutionVisits.Empty;
        set => SetValue(VisitsProperty, value);
    }

    public static readonly DependencyProperty SelectPointCommandProperty = DependencyProperty.Register(
        nameof(SelectPointCommand), typeof(ICommand), typeof(EvolutionPlot));
    public ICommand? SelectPointCommand { get => (ICommand?)GetValue(SelectPointCommandProperty); set => SetValue(SelectPointCommandProperty, value); }

    internal Point? PointAt(Point mouse)
    {
        var bounds = LatticeBounds(32 + LabelWidth, out var unit);
        if (!bounds.Contains(mouse)) return null;
        var point = new Point(Math.Floor(domain.Left + (mouse.X - bounds.Left) / unit + 0.5),
            Math.Floor(domain.Top + (bounds.Bottom - mouse.Y) / unit + 0.5));
        if (Visits.Counts.ContainsKey(point) || BestPosition == point || BestKnownNetworks.Any(network => network.Position == point)) return point;
        bool HitsMarker(Point p) => Math.Abs(mouse.X - (bounds.Left + (p.X - domain.Left) * unit)) <= MarkerSize / 2
            && Math.Abs(mouse.Y - (bounds.Bottom - (p.Y - domain.Top) * unit)) <= MarkerSize / 2;
        for (var i = BestKnownNetworks.Count - 1; i >= 0; i--)
            if (HitsMarker(BestKnownNetworks[i].Position)) return BestKnownNetworks[i].Position;
        if (ShowCurrent && Visits.Current is { } current && HitsMarker(current)) return current;
        return BestPosition is { } best && HitsMarker(best) ? best : point;
    }
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) =>
        LatticeBounds(32 + LabelWidth, out _).Contains(parameters.HitPoint)
            ? new PointHitTestResult(this, parameters.HitPoint) : null;
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (PointAt(e.GetPosition(this)) is { } point && SelectPointCommand is { } command && command.CanExecute(point))
        {
            command.Execute(point);
            e.Handled = true;
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = PointAt(e.GetPosition(this)).HasValue ? Cursors.Hand : null;
    }

    public static LinearGradientBrush FrequencyLegend => SpectralPalette.Gradient;

    private static readonly Typeface Font = new("Segoe UI");
    private static readonly Pen AxisPen = Stroke(Brushes.LightSlateGray, 1);
    private static readonly Brush LatticeDot = (Brush)new SolidColorBrush(Color.FromRgb(77, 106, 119)).GetAsFrozen();

    private const double PaddingUnits = 5;
    private const double MarkerSize = 10;
    private const double TopInset = 12;
    private const double BottomInset = 42;
    private Rect domain = new(-PaddingUnits, -PaddingUnits, 2 * PaddingUnits, 2 * PaddingUnits);

    private static void UpdateDomain(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var plot = (EvolutionPlot)sender;
        var bounds = plot.AllBufferBounds ?? plot.Visits.Bounds;
        if (plot.BestPosition is { } best) bounds.Union(best);
        foreach (var known in plot.BestKnownNetworks) bounds.Union(known.Position);
        if (bounds.IsEmpty) bounds = new Rect(0, 0, 0, 0);
        bounds.Inflate(PaddingUnits, PaddingUnits);
        // Selecting an empty cell in the existing margin must not add another margin and move the lattice.
        if (plot.SelectedPosition is { } selected) bounds.Union(selected);
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
        // A Viewbox measures with infinite space. Bound the natural width so distant points
        // increase the axis range without shrinking text and markers to subpixel sizes.
        var maxWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : 640;
        return new Size(Math.Min(width, maxWidth), height);
    }

    private Rect LatticeBounds(double gutter, out double unit)
    {
        unit = Math.Min((ActualWidth - 2 * gutter) / domain.Width, (ActualHeight - TopInset - BottomInset) / domain.Height);
        if (unit <= 0) return Rect.Empty;
        var width = domain.Width * unit;
        var height = domain.Height * unit;
        return new Rect((ActualWidth - width) / 2, TopInset + (ActualHeight - TopInset - BottomInset - height) / 2, width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var labelWidth = LabelWidth;
        var gutter = 32 + labelWidth;
        var bounds = LatticeBounds(gutter, out var unit);
        if (bounds.IsEmpty) return;
        var left = bounds.Left;
        var right = bounds.Right;
        var top = bounds.Top;
        var bottom = bounds.Bottom;
        double X(double count) => left + (count - domain.Left) * unit;
        double Y(double count) => bottom - (count - domain.Top) * unit;

        // Keep background dots distinguishable when several integer coordinates share a pixel.
        var latticeStep = IntegerStep(6 / unit);
        var labelStep = latticeStep * IntegerStep(Math.Max(32, labelWidth + 12) / (unit * latticeStep));
        for (var x = FirstTick(domain.Left, domain.Right, latticeStep); x <= domain.Right; x += latticeStep)
            for (var y = FirstTick(domain.Top, domain.Bottom, latticeStep); y <= domain.Bottom; y += latticeStep)
                dc.DrawEllipse(LatticeDot, null, new Point(X(x), Y(y)), 1, 1);

        for (var tick = FirstTick(domain.Left, domain.Right, labelStep); tick <= domain.Right; tick += labelStep)
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
        var current = ShowCurrent ? visits.Current : null;
        var best = BestPosition;
        var knownNetworks = BestKnownNetworks;
        if (visits.Counts.IsEmpty && best is null && SelectedPosition is null && knownNetworks.Count == 0)
        {
            CenterLabel(dc, "Generated sorters and evaluated children appear here.", (left + right) / 2, midY);
            return;
        }

        void DrawMarker(Point position, Brush? fill, Pen? outline = null)
        {
            var size = MarkerSize - (outline?.Thickness ?? 0);
            dc.DrawRectangle(fill, outline, new Rect(X(position.X) - size / 2, Y(position.Y) - size / 2, size, size));
        }
        dc.PushClip(new RectangleGeometry(bounds));
        var minimum = Math.Max(1, visits.Minimum);
        var logMinimum = Math.Log(minimum);
        var logRange = Math.Log(Math.Max(1, visits.Maximum)) - logMinimum;
        var lastShade = SpectralPalette.Shades.Count - 1;
        var minimumShade = visits.Maximum == 1 ? lastShade : 0;
        foreach (var (position, count) in visits.Counts)
        {
            var shade = count == minimum ? minimumShade : count == visits.Maximum ? lastShade
                : Math.Clamp((int)Math.Round(lastShade * (Math.Log(count) - logMinimum) / logRange), 1, lastShade - 1);
            dc.DrawRectangle(SpectralPalette.Shades[shade], null, new Rect(X(position.X - 0.5), Y(position.Y + 0.5), unit, unit));
        }
        if (best is { } bestCell && FillHighlightMarkers && bestCell != current) DrawMarker(bestCell, Brushes.LimeGreen);
        if (current is { } currentCell)
            DrawMarker(currentCell, FillHighlightMarkers ? Brushes.HotPink : null, FillHighlightMarkers ? null : new Pen(Brushes.HotPink, 1.5));
        if (best is { } bestMarker && (!FillHighlightMarkers || bestMarker == current))
            DrawMarker(bestMarker, null, new Pen(Brushes.LimeGreen, 1.5));
        foreach (var variant in knownNetworks)
            DrawMarker(variant.Position, null, new Pen(variant.VariantBrush, 1.5) { DashStyle = DashStyles.Dash });
        // Outline the displayed network without replacing visit, current, or best colors.
        if (SelectedPosition is { } selected)
        {
            DrawMarker(selected, null, new Pen(Brushes.Black, 3));
            DrawMarker(selected, null, new Pen(Brushes.White, 1.5));
        }
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
