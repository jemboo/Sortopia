using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Sortopia.Sorting.Networks;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels.Pages.RandomNetworks;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class RandomNetworkEducationTests
{
    [Theory]
    [InlineData("msce", 8, "160")]
    [InlineData("mssi", 8, "196")]
    [InlineData("msrs", 8, "204")]
    [InlineData("msuf4", 8, "204")]
    [InlineData("msuf6", 12, "474")]
    [InlineData("mssi", 5, "32")]
    public void Size_defaults_use_the_method_specific_99_percent_CE_budget(string method, int width, string count) => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        vm.Method = method;
        vm.ChannelCount = width;
        Assert.Equal(count, vm.CeCountText);
        Assert.Contains($"{count} CEs", vm.SizeHint);
        vm.GenerateCommand.Execute(null);
        Assert.Equal(int.Parse(count), vm.SelectedNetwork.ComparatorCount);
        vm.CeCountText = "11";
        vm.SamplesText = "32";
        Assert.Equal("11", vm.CeCountText); // Testing choices do not erase a manual size.
    });

    [Fact]
    public void Stage_methods_round_up_the_CE_budget_and_validate_it_before_generation() => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        vm.Method = "mssi";
        vm.ChannelCount = 5;
        vm.CeCountText = "11";
        Assert.Contains("6 stages, 12 CEs", vm.SizeHint);
        vm.GenerateCommand.Execute(null);
        Assert.Equal(6, vm.SelectedNetwork.StageCount);
        Assert.Equal(12, vm.SelectedNetwork.ComparatorCount);
        foreach (var invalid in new[] { "", "0", "-1", "0.5", "20001", "999999999999" })
        {
            vm.CeCountText = invalid;
            Assert.False(vm.GenerateCommand.CanExecute(null));
        }
        vm.CeCountText = "20000";
        Assert.True(vm.GenerateCommand.CanExecute(null));
        vm.ChannelCount = 8;
        Assert.Equal("196", vm.CeCountText);
    });

    [Fact]
    public void Four_layouts_preserve_the_original_network_and_report_the_displayed_size() => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        vm.SelectedNetwork = Example();
        var original = vm.SelectedNetwork;
        vm.DisplayMode = 3;
        Assert.Equal(0, vm.DisplayMode);
        Complete(vm.RunTestAsync);
        var usage = vm.Usage;
        Assert.True(vm.CanChooseTestedLayouts);
        foreach (var (mode, ces, stages) in new[] { (0, 3, 2), (1, 3, 2), (2, 2, 2), (3, 2, 1), (0, 3, 2) })
        {
            vm.DisplayMode = mode;
            Assert.Equal(ces, vm.DisplayLayout.TotalComparators);
            Assert.Equal(stages, vm.DisplayLayout.TotalStages);
            Assert.Same(original, vm.SelectedNetwork);
            Assert.Same(usage, vm.Usage);
            Assert.Contains("16 inputs (exhaustive)", vm.ResultsSummary);
        }
        vm.GenerateCommand.Execute(null);
        Assert.Null(vm.Usage);
        Assert.Equal(0, vm.DisplayMode);
        Assert.False(vm.CanChooseTestedLayouts);
    });

    [Fact]
    public void Sampled_results_are_labeled_and_retesting_can_be_cancelled() => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        vm.SelectedNetwork = Example();
        vm.SamplesText = "100";
        Complete(vm.RunTestAsync);
        vm.DisplayMode = 3;
        Assert.Contains("100 inputs (sampled)", vm.ResultsSummary);
        Assert.Contains("Used on tested inputs", vm.ResultsSummary);
        vm.SamplesText = "1000000";
        Complete(async () =>
        {
            var run = vm.RunTestAsync();
            Assert.True(vm.IsTesting);
            Assert.False(vm.GenerateCommand.CanExecute(null));
            Assert.False(vm.CanChooseTestedLayouts);
            Assert.True(vm.CancelTestCommand.CanExecute(null));
            vm.CancelTestCommand.Execute(null);
            await run;
        });
        Assert.Equal("Test cancelled.", vm.Status);
        Assert.Null(vm.Usage);
        Assert.Equal(0, vm.DisplayMode);
        Assert.False(vm.CancelTestCommand.CanExecute(null));
        Assert.True(vm.GenerateCommand.CanExecute(null));
    });

    private static NetworkDefinition Example() => new("partly used", stageSequence.create(new[] {
        stage.create(new[] { ce.create(0, 1) }, 4),
        stage.create(new[] { ce.create(0, 1), ce.create(2, 3) }, 4)
    }, 4));

    private static void Complete(Func<Task> action)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            var task = action();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
                Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

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
