using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Diagnostics;
using System.Windows.Threading;
using Sortopia.WPF.Models;
using Sortopia.WPF.Rendering;

namespace Sortopia.WPF.Controls;

/// <summary>Draws a network and interpolates recorded token positions. Contains no sorting operations.</summary>
public sealed class NetworkDiagram : FrameworkElement
{
    public static readonly DependencyProperty TraceProperty = DependencyProperty.Register(nameof(Trace), typeof(NetworkTrace), typeof(NetworkDiagram),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnTraceChanged));
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(double), typeof(NetworkDiagram),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnPlaybackChanged));
    public static readonly DependencyProperty ByStageProperty = DependencyProperty.Register(nameof(ByStage), typeof(bool), typeof(NetworkDiagram),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnPlaybackChanged));
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(NetworkDiagram),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnPlaybackChanged),
        value => value is double number && double.IsFinite(number) && number > 0);

    public static readonly DependencyProperty UseZeroOneRuleProperty = DependencyProperty.Register(nameof(UseZeroOneRule), typeof(bool), typeof(NetworkDiagram),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnPaletteChanged));
    public bool UseZeroOneRule { get => (bool)GetValue(UseZeroOneRuleProperty); set => SetValue(UseZeroOneRuleProperty, value); }
    public static readonly DependencyProperty UsageProperty = DependencyProperty.Register(nameof(Usage), typeof(NetworkCheckResult), typeof(NetworkDiagram),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public NetworkCheckResult? Usage { get => (NetworkCheckResult?)GetValue(UsageProperty); set => SetValue(UsageProperty, value); }
    private static readonly Pen[] UsagePens = UsagePalette.Bands.Select(band => { var pen = new Pen(band.Fill, 4); pen.Freeze(); return pen; }).ToArray();
    internal static int UsageLevel(int uses, int tested) => UsagePalette.Level(uses, tested);
    private Brush[] valueBrushes = [];
    private Brush[] labelBrushes = [];
    private Pen[] channelPens = [];

    private static readonly Brush BackgroundBrush = Frozen("#102B3B");
    private static readonly Brush BandBrush = Frozen("#18394A");
    private static readonly Brush MutedBrush = Frozen("#8DAAB9");
    private static readonly Brush ActiveBrush = Frozen("#F4CA7F");
    private static readonly Pen FuturePen = new(Frozen("#829AA6"), 3.5);
    private static readonly Pen UsedPen = new(Frozen("#56C7B4"), 4);
    private static readonly Pen IdlePen = new(Frozen("#456777"), 3.5);
    private static readonly Pen ActivePen = new(ActiveBrush, 5);
    private static readonly Typeface Typeface = new("Segoe UI");
    private static readonly Pen PlayheadPen = new(Frozen("#4DF4CA7F"), 1);
    private NetworkLayout? layout;
    public event EventHandler? CompactionCompleted;
    private readonly Stopwatch transitionClock = new();
    private readonly DispatcherTimer transitionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    public static readonly DependencyProperty CompactionProperty = DependencyProperty.Register(nameof(Compaction), typeof(CompactionTransition), typeof(NetworkDiagram),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnCompactionChanged));
    private static readonly DependencyProperty TransitionProgressProperty = DependencyProperty.Register("TransitionProgress", typeof(double), typeof(NetworkDiagram),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public CompactionTransition? Compaction { get => (CompactionTransition?)GetValue(CompactionProperty); set => SetValue(CompactionProperty, value); }
    public NetworkDiagram()
    {
        transitionTimer.Tick += (_, _) => AdvanceCompaction();
        Unloaded += (_, _) =>
        {
            StopCompactionClock();
            if (Compaction is not null) CompactionCompleted?.Invoke(this, EventArgs.Empty);
        };
    }
    private static void OnCompactionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var diagram = (NetworkDiagram)sender;
        diagram.StopCompactionClock();
        diagram.SetValue(TransitionProgressProperty, 0d);
        if (args.NewValue is not CompactionTransition transition) return;
        diagram.ToolTip = null;
        if (!SystemParameters.ClientAreaAnimation)
        {
            diagram.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ReferenceEquals(diagram.Compaction, transition)) diagram.CompactionCompleted?.Invoke(diagram, EventArgs.Empty);
            }));
            return;
        }
        diagram.transitionClock.Restart();
        diagram.transitionTimer.Start();
    }
    private void StopCompactionClock()
    {
        transitionTimer.Stop();
        transitionClock.Stop();
    }
    private void AdvanceCompaction()
    {
        if (Compaction is null) { StopCompactionClock(); return; }
        var t = Math.Clamp(transitionClock.Elapsed.TotalMilliseconds / 600, 0, 1);
        SetValue(TransitionProgressProperty, t * t * (3 - 2 * t));
        if (t >= 1)
        {
            StopCompactionClock();
            CompactionCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? PlayheadChanged;

    public double PlayheadX
    {
        get
        {
            if (Trace is not { } trace || layout is null) return 0;
            var position = double.IsFinite(Position) ? Math.Clamp(Position, 0, trace.StepCount(ByStage)) : 0;
            var step = (int)Math.Floor(position);
            var from = trace.FrameIndex(step, ByStage);
            var to = trace.FrameIndex(Math.Min(step + 1, trace.StepCount(ByStage)), ByStage);
            var fraction = position - step;
            return Interpolate(layout.FrameX[from], layout.FrameX[to], fraction * fraction * (3 - 2 * fraction)) * Zoom;
        }
    }

    private static void OnPlaybackChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var diagram = (NetworkDiagram)sender;
        diagram.PlayheadChanged?.Invoke(diagram, EventArgs.Empty);
    }

    public NetworkTrace? Trace { get => (NetworkTrace?)GetValue(TraceProperty); set => SetValue(TraceProperty, value); }
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public bool ByStage { get => (bool)GetValue(ByStageProperty); set => SetValue(ByStageProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    private static void OnTraceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var diagram = (NetworkDiagram)sender;
        diagram.layout = args.NewValue is NetworkTrace trace ? new NetworkLayout(trace) : null;
        diagram.UpdatePalette();
        diagram.PlayheadChanged?.Invoke(diagram, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size availableSize) => layout is null ? new Size(0, 0) : new Size((Compaction?.Width ?? layout.Width) * Zoom, layout.Height * Zoom);

    protected override void OnRender(DrawingContext dc)
    {
        if (Trace is not { } trace || layout is not { } geometry) return;
        if (Compaction is { } transition)
        {
            RenderCompaction(dc, transition, (double)GetValue(TransitionProgressProperty));
            return;
        }
        dc.PushTransform(new ScaleTransform(Zoom, Zoom));
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, geometry.Width, geometry.Height));
        var position = double.IsFinite(Position) ? Math.Clamp(Position, 0, trace.StepCount(ByStage)) : 0;
        var step = (int)Math.Floor(position);
        var from = trace.FrameIndex(step, ByStage);
        var to = trace.FrameIndex(Math.Min(step + 1, trace.StepCount(ByStage)), ByStage);
        var fraction = position - step;
        var eased = fraction * fraction * (3 - 2 * fraction);

        for (var stage = 0; stage < geometry.Bands.Count; stage++)
        {
            var band = geometry.Bands[stage];
            if (stage % 2 == 0) dc.DrawRectangle(BandBrush, null, new Rect(band.Left, 30, band.Right - band.Left, geometry.Height - 40));
            Text(dc, $"{stage + 1}", (band.Left + band.Right) / 2, 13, MutedBrush, 12, true);
        }
        Text(dc, "Stage", 5, 13, MutedBrush, 11);
        for (var channel = 0; channel < trace.Network.Width; channel++)
        {
            var y = NetworkLayout.ChannelY(channel);
            dc.DrawLine(channelPens[channel], new Point(30, y), new Point(geometry.CutoffX - 20, y));
            Text(dc, channel.ToString(CultureInfo.InvariantCulture), 10, y - 8, MutedBrush, 11);
        }

        foreach (var ce in geometry.Comparators)
        {
            var pen = ce.Index >= from && ce.Index < to ? ActivePen : ce.Index < from ? trace.SwapCounts[ce.Index] > 0 ? UsedPen : IdlePen : FuturePen;
            if (Usage is { Tested: > 0 } usage && usage.CeUses.Count == trace.Network.ComparatorCount)
                pen = UsagePens[UsageLevel(usage.CeUses[ce.Index], usage.Tested)];
            var low = new Point(ce.X, NetworkLayout.ChannelY(ce.Low));
            var high = new Point(ce.X, NetworkLayout.ChannelY(ce.High));
            dc.DrawLine(pen, low, high);
            dc.DrawEllipse(pen.Brush, null, low, 3, 3);
            dc.DrawEllipse(pen.Brush, null, high, 3, 3);
        }

        var x = Interpolate(geometry.FrameX[from], geometry.FrameX[to], eased);
        dc.DrawLine(PlayheadPen, new Point(x, 35), new Point(x, geometry.Height - 18));
        for (var token = 0; token < trace.Network.Width; token++)
        {
            var channel = Interpolate(trace.TokenChannels[from][token], trace.TokenChannels[to][token], eased);
            var y = NetworkLayout.ChannelY(channel);
            var value = trace.Values[0][token];

            // Separate crossing trajectories so both recorded values remain visible during a swap.
            var direction = Math.Sign(trace.TokenChannels[to][token] - trace.TokenChannels[from][token]);
            var tokenX = x + direction * Math.Sin(Math.PI * fraction) * 11;
            dc.DrawEllipse(valueBrushes[token], new Pen(labelBrushes[token], 1.5), new Point(tokenX, y), 10, 10);
            Text(dc, value.ToString(CultureInfo.InvariantCulture), tokenX, y - 8, labelBrushes[token], 11, true);
        }
        if (trace.IsPreview)
        {
            var edge = geometry.CutoffX;
            dc.DrawLine(ActivePen, new Point(edge, 35), new Point(edge, geometry.Height - 18));
            Text(dc, "Display cutoff — network continues", edge + 16, 42, ActiveBrush, 14);
            Text(dc, $"Showing {trace.DisplayStageCount:N0} of {trace.Network.StageCount:N0} stages", edge + 16, 65, MutedBrush, 12);
            Text(dc, $"and {trace.Comparators.Count:N0} of {trace.Network.ComparatorCount:N0} CEs.", edge + 16, 83, MutedBrush, 12);
            Text(dc, "Testing and edits use the full network.", edge + 16, 105, MutedBrush, 12);
        }
        dc.Pop();
    }

    internal void RenderCompaction(DrawingContext dc, CompactionTransition transition, double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var height = transition.After.Height;
        var edge = Interpolate(transition.Before.CutoffX, transition.After.CutoffX, t);
        dc.PushTransform(new ScaleTransform(Zoom, Zoom));
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, transition.Width, height));
        void Bands(NetworkLayout source, double opacity)
        {
            dc.PushOpacity(opacity);
            for (var i = 0; i < source.Bands.Count; i++)
            {
                var band = source.Bands[i];
                if (i % 2 == 0) dc.DrawRectangle(BandBrush, null, new Rect(band.Left, 30, band.Right - band.Left, height - 40));
                Text(dc, $"{i + 1}", (band.Left + band.Right) / 2, 13, MutedBrush, 12, true);
            }
            dc.Pop();
        }
        Bands(transition.Before, 1 - t);
        Bands(transition.After, t);
        for (var channel = 0; channel < transition.AfterTrace.Network.Width; channel++)
        {
            var y = NetworkLayout.ChannelY(channel);
            dc.DrawLine(channelPens[channel], new Point(30, y), new Point(edge - 20, y));
            Text(dc, channel.ToString(CultureInfo.InvariantCulture), 10, y - 8, MutedBrush, 11);
        }
        foreach (var move in transition.Moves)
        {
            var pen = Usage is not null && transition.Tested > 0 ? UsagePens[UsageLevel(move.Uses, transition.Tested)] : FuturePen;
            var x = move.X(t);
            dc.PushOpacity(move.Opacity(t));
            var low = new Point(x, NetworkLayout.ChannelY(move.Low));
            var high = new Point(x, NetworkLayout.ChannelY(move.High));
            dc.DrawLine(pen, low, high);
            dc.DrawEllipse(pen.Brush, null, low, 3, 3);
            dc.DrawEllipse(pen.Brush, null, high, 3, 3);
            dc.Pop();
        }
        if (transition.BeforeTrace.IsPreview || transition.AfterTrace.IsPreview)
        {
            dc.DrawLine(ActivePen, new Point(edge, 35), new Point(edge, height - 18));
            Text(dc, "Compacting displayed portion…", edge + 16, 42, ActiveBrush, 13);
        }
        dc.Pop();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Compaction is not null || Usage is not { Tested: > 0 } usage || layout is null) { ToolTip = null; return; }
        var point = e.GetPosition(this);
        foreach (var ce in layout.Comparators)
        {
            if (Math.Abs(point.X / Zoom - ce.X) <= 6 && point.Y / Zoom >= NetworkLayout.ChannelY(ce.Low) - 5
                && point.Y / Zoom <= NetworkLayout.ChannelY(ce.High) + 5 && ce.Index < usage.CeUses.Count)
            {
                var count = usage.CeUses[ce.Index];
                var text = $"CE {ce.Index + 1}, channels {ce.Low}–{ce.High}: {count:N0} swaps / {usage.Tested:N0} inputs ({(double)count / usage.Tested:P2})";
                if (!Equals(ToolTip, text)) ToolTip = text;
                return;
            }
        }
        ToolTip = null;
    }

    private static void OnPaletteChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((NetworkDiagram)sender).UpdatePalette();

    private void UpdatePalette()
    {
        if (Trace is not { } trace) return;
        valueBrushes = new Brush[trace.Network.Width];
        labelBrushes = new Brush[trace.Network.Width];
        channelPens = new Pen[trace.Network.Width];
        // Destination positions come from the recorded trace; rendering never sorts values.
        for (var token = 0; token < trace.Network.Width; token++)
        {
            var destination = trace.TokenChannels[^1][token];
            var color = UseZeroOneRule
                ? trace.Values[0][token] == 0 ? Colors.White : Colors.Black
                : GradientColor((double)destination / Math.Max(1, trace.Network.Width - 1));
            var fill = new SolidColorBrush(color);
            fill.Freeze();
            valueBrushes[token] = fill;
            // Choose the opposite light/dark label for readable contrast.
            static double Linear(byte value) => value <= 10 ? value / 3294.6 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
            var luminance = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
            labelBrushes[token] = luminance > 0.179 ? Brushes.Black : Brushes.White;
            channelPens[destination] = new Pen(UseZeroOneRule ? MutedBrush : fill, 3.25);
            channelPens[destination].Freeze();
        }
    }

    private static Color GradientColor(double position)
    {
        // Blue -> cyan -> green -> yellow -> red, without cycling for wider networks.
        Color[] stops = [Color.FromRgb(40, 80, 220), Color.FromRgb(30, 200, 240),
            Color.FromRgb(40, 200, 100), Color.FromRgb(250, 220, 40), Color.FromRgb(235, 55, 45)];
        var scaled = Math.Clamp(position, 0, 1) * (stops.Length - 1);
        var index = Math.Min((int)scaled, stops.Length - 2);
        var fraction = scaled - index;
        var a = stops[index];
        var b = stops[index + 1];
        return Color.FromRgb((byte)Interpolate(a.R, b.R, fraction),
            (byte)Interpolate(a.G, b.G, fraction), (byte)Interpolate(a.B, b.B, fraction));
    }

    private void Text(DrawingContext dc, string value, double x, double y, Brush brush, double size, bool centered = false)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(centered ? x - text.Width / 2 : x, y));
    }

    private static double Interpolate(double from, double to, double fraction) => from + (to - from) * fraction;
    private static Brush Frozen(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DiagramAutomationPeer(this);
    private sealed class DiagramAutomationPeer(NetworkDiagram owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(NetworkDiagram);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetNameCore() => owner.Trace is { } trace ? $"Sorting network, {trace.Network.Label}, {trace.Network.StageCount} stages. Current values are shown below the diagram." : "Sorting network";
    }
}
