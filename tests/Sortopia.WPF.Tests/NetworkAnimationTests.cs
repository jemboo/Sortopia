using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using Sortopia.Sorting.Networks;
using Sortopia.WPF.Models;
using Sortopia.WPF.Rendering;
using Sortopia.WPF.Rendering.AnimationSchemes;
using Sortopia.WPF.ViewModels.Pages.Networks;
using Sortopia.WPF.Views.Shared.Controls;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class NetworkAnimationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Playhead_never_moves_left_when_successive_CEs_share_a_column(bool spinner, bool byStage) => OnSta(() =>
    {
        var network = new NetworkDefinition("Shared columns", stageSequence.create(new[]
        {
            stage.create(new[] { ce.create(0, 1), ce.create(2, 3), ce.create(4, 5) }, 6),
            stage.create(new[] { ce.create(0, 2), ce.create(1, 4) }, 6),
            stage.create(new[] { ce.create(0, 1), ce.create(2, 3), ce.create(4, 5) }, 6)
        }, 6));
        CeAnimationScheme scheme = spinner ? CeSpinnerScheme.Instance : CeSliderScheme.Instance;
        foreach (var input in new[] { new[] { 5, 4, 3, 2, 1, 0 }, new[] { 0, 1, 3, 2, 4, 5 } })
        {
            var trace = NetworkTrace.Create(network, input);
            var layout = new NetworkLayout(trace, scheme);
            var diagram = new NetworkDiagram { Trace = trace, AnimationScheme = scheme, ByStage = byStage };
            var previousX = diagram.PlayheadX;
            for (var sample = 0; sample <= trace.StepCount(byStage) * 100; sample++)
            {
                diagram.Position = sample / 100d;
                var tokens = Render(diagram);
                var line = Assert.IsType<LineGeometry>(Assert.Single(
                    Flatten(VisualTreeHelper.GetDrawing(diagram)).OfType<GeometryDrawing>(),
                    drawing => drawing.Pen?.Thickness == 1 && drawing.Geometry is LineGeometry path
                        && path.StartPoint.Y == 35 && path.EndPoint.Y == layout.Height - 18).Geometry);
                Assert.Equal(diagram.PlayheadX, line.StartPoint.X, 6);
                Assert.Equal(line.StartPoint.X, line.EndPoint.X);
                Assert.True(line.StartPoint.X >= previousX - 1e-9,
                    $"Playhead moved left from {previousX} to {line.StartPoint.X} at position {diagram.Position}.");
                if (!spinner)
                    Assert.All(tokens, token => Assert.Equal(line.StartPoint.X, token.Center.X, 6));
                previousX = line.StartPoint.X;
            }
        }
    });

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, false)]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, true)]
    public void Two_channel_playback_keeps_each_label_and_only_moves_unsorted_values(int first, int second, bool slider) => OnSta(() =>
    {
        using var vm = new NetworksPageViewModel(() => { });
        vm.SelectedNetwork = vm.Networks.First(network => network.Width == 2);
        vm.ReplaceTrace(NetworkTrace.Create(vm.SelectedNetwork, [first, second]));
        var diagram = new NetworkDiagram { Trace = vm.Trace, AnimationScheme = slider ? CeSliderScheme.Instance : CeSpinnerScheme.Instance };
        var initial = Render(diagram);
        vm.PlayPauseCommand.Execute(null);
        for (var frame = 0; frame <= 100; frame++)
        {
            if (frame > 0) vm.Advance(TimeSpan.FromMilliseconds(6.5));
            diagram.Trace = vm.Trace;
            diagram.Position = vm.Position;
            var tokens = Render(diagram);
            AssertIdentityAndSeparation(initial, tokens, requireSeparation: !slider);
            if (first <= second)
            {
                Assert.Equal(NetworkLayout.ChannelY(0), tokens[0].Center.Y);
                Assert.Equal(NetworkLayout.ChannelY(1), tokens[1].Center.Y);
                Assert.Equal(diagram.PlayheadX, tokens[0].Center.X, 6);
                Assert.Equal(diagram.PlayheadX, tokens[1].Center.X, 6);
            }
        }
        Assert.Equal(NetworkLayout.ChannelY(first > second ? 1 : 0), Render(diagram)[0].Center.Y, 6);
        Assert.Equal(NetworkLayout.ChannelY(first > second ? 0 : 1), Render(diagram)[1].Center.Y, 6);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Long_swaps_keep_labels_visible_and_leave_sorted_values_on_their_channels(bool byStage) => OnSta(() =>
    {
        var comparisons = new[] { ce.create(0, 5), ce.create(1, 4) };
        var network = new NetworkDefinition("Long swaps", stageSequence.create(new[]
        {
            stage.create(comparisons, 6), stage.create(comparisons, 6)
        }, 6));
        var trace = NetworkTrace.Create(network, [5, 4, 2, 3, 1, 0]);
        var diagram = new NetworkDiagram { Trace = trace, ByStage = byStage, AnimationScheme = CeSpinnerScheme.Instance };
        var initial = Render(diagram);
        for (var sample = 0; sample <= trace.StepCount(byStage) * 100; sample++)
        {
            diagram.Position = sample / 100d;
            var tokens = Render(diagram);
            AssertIdentityAndSeparation(initial, tokens);
            Assert.Equal(NetworkLayout.ChannelY(2), tokens[2].Center.Y);
            Assert.Equal(NetworkLayout.ChannelY(3), tokens[3].Center.Y);
            if (diagram.Position >= (byStage ? 1 : 2))
                for (var token = 0; token < tokens.Length; token++)
                    Assert.Equal(NetworkLayout.ChannelY(trace.TokenChannels[^1][token]), tokens[token].Center.Y);
        }
    });

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(6, false)]
    [InlineData(6, true)]
    [InlineData(32, false)]
    [InlineData(32, true)]
    public void Swaps_approach_the_CE_pause_and_rotate_about_its_fixed_midpoint(int width, bool byStage) => OnSta(() =>
    {
        var network = new NetworkDefinition("Circular swap", stageSequence.create(new[]
        {
            stage.create(new[] { ce.create(0, width - 1) }, width)
        }, width));
        var input = Enumerable.Range(0, width).ToArray();
        (input[0], input[^1]) = (input[^1], input[0]);
        var trace = NetworkTrace.Create(network, input);
        var layout = new NetworkLayout(trace, CeSpinnerScheme.Instance);
        var diagram = new NetworkDiagram { Trace = trace, ByStage = byStage, AnimationScheme = CeSpinnerScheme.Instance };
        var initial = Render(diagram);
        var ceX = layout.Comparators[0].X;
        var radius = (NetworkLayout.ChannelY(width - 1) - NetworkLayout.ChannelY(0)) / 2;
        var center = new Point(ceX, (NetworkLayout.ChannelY(0) + NetworkLayout.ChannelY(width - 1)) / 2);

        for (var sample = 0; sample <= 100; sample++)
        {
            diagram.Position = sample / 100d;
            var tokens = Render(diagram);
            AssertIdentityAndSeparation(initial, tokens);
            foreach (var token in tokens)
            {
                Assert.InRange(token.Center.X - 10.75, 30, layout.Width);
                Assert.InRange(token.Center.X + 10.75, 30, layout.Width);
            }
            for (var token = 1; token < width - 1; token++)
                Assert.Equal(NetworkLayout.ChannelY(token), tokens[token].Center.Y);

            if (sample <= 30)
            {
                Assert.Equal(NetworkLayout.ChannelY(0), tokens[0].Center.Y);
                Assert.Equal(NetworkLayout.ChannelY(width - 1), tokens[^1].Center.Y);
                Assert.Equal(tokens[0].Center.X, tokens[^1].Center.X);
                Assert.InRange(tokens[0].Center.X, layout.FrameX[0], ceX);
            }
            if (sample is >= 30 and <= 45)
            {
                Assert.Equal(new Point(ceX, NetworkLayout.ChannelY(0)), tokens[0].Center);
                Assert.Equal(new Point(ceX, NetworkLayout.ChannelY(width - 1)), tokens[^1].Center);
            }
            if (sample is >= 45 and <= 90)
            {
                Assert.Equal(ceX, diagram.PlayheadX, 6);
                Assert.Equal(center.X, (tokens[0].Center.X + tokens[^1].Center.X) / 2, 6);
                Assert.Equal(center.Y, (tokens[0].Center.Y + tokens[^1].Center.Y) / 2, 6);
                Assert.Equal(radius, (tokens[0].Center - center).Length, 6);
                Assert.Equal(radius, (tokens[^1].Center - center).Length, 6);
            }
            if (sample >= 90)
            {
                Assert.Equal(NetworkLayout.ChannelY(width - 1), tokens[0].Center.Y, 6);
                Assert.Equal(NetworkLayout.ChannelY(0), tokens[^1].Center.Y, 6);
            }
        }

        diagram.Position = 0.675;
        var halfway = Render(diagram);
        Assert.Equal(ceX + radius, halfway[0].Center.X, 6);
        Assert.Equal(ceX - radius, halfway[^1].Center.X, 6);
        Assert.Equal(center.Y, halfway[0].Center.Y, 6);
        Assert.Equal(center.Y, halfway[^1].Center.Y, 6);

        var sortedLayout = new NetworkLayout(NetworkTrace.Create(network, Enumerable.Range(0, width).ToArray()), CeSpinnerScheme.Instance);
        Assert.Equal(layout.Width, sortedLayout.Width);
        Assert.Equal(layout.FrameX, sortedLayout.FrameX);
    });

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(6, false)]
    [InlineData(6, true)]
    [InlineData(32, false)]
    [InlineData(32, true)]
    public void Slider_pauses_at_the_CE_then_moves_only_along_its_vertical_axis(int width, bool byStage) => OnSta(() =>
    {
        var network = new NetworkDefinition("Vertical swap", stageSequence.create(new[]
        {
            stage.create(new[] { ce.create(0, width - 1) }, width)
        }, width));
        var input = Enumerable.Range(0, width).ToArray();
        (input[0], input[^1]) = (input[^1], input[0]);
        var trace = NetworkTrace.Create(network, input);
        var layout = new NetworkLayout(trace, CeSliderScheme.Instance);
        var diagram = new NetworkDiagram { Trace = trace, ByStage = byStage, AnimationScheme = CeSliderScheme.Instance };
        var initial = Render(diagram);
        var ceX = layout.Comparators[0].X;
        var top = NetworkLayout.ChannelY(0);
        var bottom = NetworkLayout.ChannelY(width - 1);
        var previous = initial;
        for (var sample = 0; sample <= 100; sample++)
        {
            diagram.Position = sample / 100d;
            var tokens = Render(diagram);
            AssertIdentityAndSeparation(initial, tokens, requireSeparation: false);
            Assert.All(tokens, token => Assert.Equal(diagram.PlayheadX, token.Center.X, 6));
            for (var token = 1; token < width - 1; token++)
                Assert.Equal(NetworkLayout.ChannelY(token), tokens[token].Center.Y);
            Assert.InRange(tokens[0].Center.Y, previous[0].Center.Y, bottom);
            Assert.InRange(tokens[^1].Center.Y, top, previous[^1].Center.Y);
            if (sample <= 45)
            {
                Assert.Equal(top, tokens[0].Center.Y);
                Assert.Equal(bottom, tokens[^1].Center.Y);
            }
            if (sample is >= 30 and <= 90)
                Assert.All(tokens, token => Assert.Equal(ceX, token.Center.X, 6));
            if (sample >= 90)
            {
                Assert.Equal(bottom, tokens[0].Center.Y);
                Assert.Equal(top, tokens[^1].Center.Y);
            }
            previous = tokens;
        }
        diagram.Position = 0.675;
        var crossing = Render(diagram);
        Assert.Equal((top + bottom) / 2, crossing[0].Center.Y, 6);
        Assert.Equal(crossing[0].Center.Y, crossing[^1].Center.Y, 6);
        Assert.Equal(ceX, crossing[0].Center.X, 6);
        Assert.Equal(new NetworkLayout(network).Width, layout.Width);
    });

    [Fact]
    public void Switching_schemes_retains_playback_state_and_refits_the_swap_paths() => OnSta(() =>
    {
        var network = new NetworkDefinition("Scheme selection", stageSequence.create(new[]
        {
            stage.create(new[] { ce.create(0, 31) }, 32)
        }, 32));
        var trace = NetworkTrace.Create(network, Enumerable.Range(0, 32).Reverse().ToArray());
        var diagram = new NetworkDiagram { Trace = trace, Position = 0.675 };
        Assert.IsType<CeSliderScheme>(diagram.AnimationScheme);
        var slider = Render(diagram);
        var available = new Size(double.PositiveInfinity, double.PositiveInfinity);
        diagram.Measure(available);
        var sliderWidth = diagram.DesiredSize.Width;

        diagram.AnimationScheme = CeSpinnerScheme.Instance;
        var spinner = Render(diagram);
        AssertIdentityAndSeparation(slider, spinner);
        Assert.NotEqual(spinner[0].Center.X, spinner[^1].Center.X);
        diagram.Measure(available);
        Assert.True(diagram.DesiredSize.Width > sliderWidth);
        Assert.Same(trace, diagram.Trace);
        Assert.Equal(0.675, diagram.Position);

        diagram.AnimationScheme = CeSliderScheme.Instance;
        var restored = Render(diagram);
        Assert.Equal(slider.Select(token => token.Center), restored.Select(token => token.Center));
        diagram.Measure(available);
        Assert.Equal(sliderWidth, diagram.DesiredSize.Width);
    });

    private sealed record DrawnToken(Point Center, Color Color, ushort[] Label);

    private static DrawnToken[] Render(NetworkDiagram diagram)
    {
        diagram.Measure(new Size(800, 600));
        diagram.Arrange(new Rect(0, 0, 800, 600));
        diagram.UpdateLayout();
        var drawings = Flatten(VisualTreeHelper.GetDrawing(diagram)).ToArray();
        var tokens = new List<DrawnToken>();
        for (var index = 0; index < drawings.Length; index++)
        {
            if (drawings[index] is not GeometryDrawing { Geometry: EllipseGeometry { RadiusX: 10 } circle } shape) continue;
            var label = Assert.IsType<GlyphRunDrawing>(drawings[index + 1]);
            tokens.Add(new(circle.Center, Assert.IsType<SolidColorBrush>(shape.Brush).Color, label.GlyphRun.GlyphIndices.ToArray()));
        }
        Assert.Equal(diagram.Trace!.Network.Width, tokens.Count);
        return tokens.ToArray();
    }

    private static IEnumerable<Drawing> Flatten(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
                foreach (var item in Flatten(child)) yield return item;
        }
        else yield return drawing;
    }

    private static void AssertIdentityAndSeparation(DrawnToken[] initial, DrawnToken[] tokens, bool requireSeparation = true)
    {
        for (var token = 0; token < tokens.Length; token++)
        {
            Assert.Equal(initial[token].Label, tokens[token].Label);
            Assert.Equal(initial[token].Color, tokens[token].Color);
            for (var other = 0; requireSeparation && other < token; other++)
                Assert.True((tokens[token].Center - tokens[other].Center).Length >= 21.5,
                    $"Tokens {token} and {other} overlap at {tokens[token].Center} and {tokens[other].Center}.");
        }
    }

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
