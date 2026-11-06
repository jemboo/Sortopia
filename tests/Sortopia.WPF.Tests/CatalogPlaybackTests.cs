using System.Runtime.ExceptionServices;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels.Pages.Networks;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class CatalogPlaybackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void New_input_rewinds_and_pauses_without_recoloring_comparators(bool binary) => OnSta(() =>
    {
        using var vm = new NetworksPageViewModel(() => { });
        vm.UseZeroOneRule = binary;
        vm.PlayPauseCommand.Execute(null);
        vm.Advance(TimeSpan.FromSeconds(2));
        Assert.Contains(vm.ComparatorStates, state => state != ComparatorState.Unseen);
        var colors = vm.ComparatorStates;
        var trace = vm.Trace;
        vm.NewInputCommand.Execute(null);
        Assert.False(vm.IsPlaying);
        Assert.Equal(0, vm.Position);
        Assert.NotSame(trace, vm.Trace);
        Assert.Same(colors, vm.ComparatorStates);
        if (binary) Assert.All(vm.Trace.Values[0], value => Assert.InRange(value, 0, 1));
        else Assert.Equal(Enumerable.Range(0, vm.SelectedNetwork.Width), vm.Trace.Values[0].Order());
    });

    [Fact]
    public void Only_completed_comparisons_are_colored_and_swaps_remain_across_inputs() => OnSta(() =>
    {
        using var vm = new NetworksPageViewModel(() => { });
        vm.SeekPosition = 0.5;
        Assert.All(vm.ComparatorStates, state => Assert.Equal(ComparatorState.Unseen, state));
        vm.SeekPosition = 1;
        Assert.Equal(ComparatorState.Swapped, vm.ComparatorStates[0]);
        Assert.All(vm.ComparatorStates.Skip(1), state => Assert.Equal(ComparatorState.Unseen, state));
        vm.ReplaceTrace(NetworkTrace.Create(vm.SelectedNetwork, Enumerable.Range(0, vm.SelectedNetwork.Width).ToArray()));
        vm.SeekPosition = vm.StepCount;
        Assert.Equal(ComparatorState.Swapped, vm.ComparatorStates[0]);
        Assert.All(vm.ComparatorStates.Skip(1), state => Assert.Equal(ComparatorState.Compared, state));
    });

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    public void Play_resume_and_replay_preserve_the_selected_input(bool binary, int width) => OnSta(() =>
    {
        using var vm = new NetworksPageViewModel(() => { });
        vm.SelectedNetwork = vm.Networks.First(n => n.Width == width);
        vm.UseZeroOneRule = binary;
        var sorted = Enumerable.Range(0, width).Select(value => binary ? value < width / 2 ? 0 : 1 : value).ToArray();
        foreach (var input in new[] { sorted, sorted.Reverse().ToArray() })
        {
            vm.ReplaceTrace(NetworkTrace.Create(vm.SelectedNetwork, input));
            var trace = vm.Trace;
            var colors = vm.ComparatorStates;
            vm.PlayPauseCommand.Execute(null);
            Assert.True(vm.IsPlaying);
            Assert.Equal(0, vm.Position);
            Assert.Same(trace, vm.Trace);
            Assert.Equal(input, vm.Trace.Values[0]);
            Assert.Same(colors, vm.ComparatorStates);
            vm.Advance(TimeSpan.FromSeconds(0.325));
            vm.PlayPauseCommand.Execute(null);
            Assert.False(vm.IsPlaying);
            Assert.Same(trace, vm.Trace);
            Assert.Equal(0.5, vm.Position, 6);

            vm.PlayPauseCommand.Execute(null);
            Assert.True(vm.IsPlaying);
            Assert.Same(trace, vm.Trace);
            Assert.Equal(0.5, vm.Position, 6);
            vm.Advance(TimeSpan.FromSeconds(100));
            Assert.False(vm.IsPlaying);
            Assert.Equal(vm.StepCount, vm.Position);
            Assert.Equal(sorted, vm.Trace.Values[^1]);

            vm.PlayPauseCommand.Execute(null);
            Assert.True(vm.IsPlaying);
            Assert.Same(trace, vm.Trace);
            Assert.Equal(0, vm.Position);
            vm.Pause();
        }
    });

    [Fact]
    public void Reset_colors_clears_history_without_rewinding_or_changing_input() => OnSta(() =>
    {
        using var vm = new NetworksPageViewModel(() => { });
        vm.PlayPauseCommand.Execute(null);
        vm.Advance(TimeSpan.FromSeconds(1.3));
        var trace = vm.Trace;
        var position = vm.Position;
        vm.ResetColorsCommand.Execute(null);
        Assert.Same(trace, vm.Trace);
        Assert.Equal(position, vm.Position);
        Assert.True(vm.IsPlaying);
        Assert.All(vm.ComparatorStates, state => Assert.Equal(ComparatorState.Unseen, state));
        vm.Advance(TimeSpan.FromSeconds(0.65));
        Assert.All(vm.ComparatorStates.Take(2), state => Assert.Equal(ComparatorState.Unseen, state));
        Assert.NotEqual(ComparatorState.Unseen, vm.ComparatorStates[2]);
        vm.NewInputCommand.Execute(null);
        vm.PlayPauseCommand.Execute(null);
        vm.Advance(TimeSpan.FromSeconds(0.65));
        Assert.NotEqual(ComparatorState.Unseen, vm.ComparatorStates[0]);
    });

    [Fact]
    public void Selecting_another_network_starts_with_fresh_colors_and_fit_zoom() => OnSta(() =>
    {
        using var vm = new NetworksPageViewModel(() => { });
        vm.SeekPosition = vm.StepCount;
        vm.ZoomFactor = 2;
        var notified = false;
        vm.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(vm.CountsSummary);
        vm.SelectedNetwork = vm.Networks.Last();
        Assert.True(notified);
        Assert.Equal(1, vm.ZoomFactor);
        Assert.Equal(vm.SelectedNetwork.ComparatorCount, vm.ComparatorStates.Count);
        Assert.All(vm.ComparatorStates, state => Assert.Equal(ComparatorState.Unseen, state));
    });

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
