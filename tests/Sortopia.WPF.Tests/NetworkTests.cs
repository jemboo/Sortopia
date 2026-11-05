using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Sortopia.Models.Sorting;
using Sortopia.Sorting.Inputs;
using Sortopia.WPF.Controls;
using Sortopia.WPF.Models;
using Sortopia.WPF.Rendering;
using Sortopia.WPF.ViewModels;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class NetworkTests
{
    [Fact]
    public void Display_modes_preserve_original_comparator_identity_and_filter_usage()
    {
        var ce = Sortopia.Sorting.Networks.ce.create(0, 1);
        var stages = new[] {
            Sortopia.Sorting.Networks.stage.create(new[] { ce }, 4),
            Sortopia.Sorting.Networks.stage.create(new[] { ce }, 4),
            Sortopia.Sorting.Networks.stage.create(new[] { Sortopia.Sorting.Networks.ce.create(2, 3), ce }, 4)
        };
        var network = new NetworkDefinition("Display", Sortopia.Sorting.Networks.stageSequence.create(stages, 4));
        var usage = new NetworkCheckResult(4, true, null, 0, new[] { 0, 2, 0, 1 });
        var all = new NetworkLayout(network, usage, 0);
        var usedStages = new NetworkLayout(network, usage, 1);
        var usedCes = new NetworkLayout(network, usage, 2);
        var compact = new NetworkLayout(network, usage, 3);
        Assert.Equal(new[] { 0, 1, 2, 3 }, all.Comparators.Select(c => c.Index));
        Assert.Equal(new[] { 1, 2, 3 }, usedStages.Comparators.Select(c => c.Index));
        Assert.Equal(2, usedStages.Bands.Count);
        Assert.Equal(new[] { 1, 3 }, usedCes.Comparators.Select(c => c.Index));
        Assert.Equal(new[] { 1, 3 }, compact.Comparators.Select(c => c.Index));
    }

    [Fact]
    public void Preview_limits_use_the_totals_after_applying_each_display_mode()
    {
        var count = NetworkTrace.DisplayStageLimit + 2;
        var network = new NetworkDefinition("long", RandomNetwork.generate("msce", 2, count, Guid.Empty));
        var uses = Enumerable.Repeat(1, count).ToArray();
        uses[0] = 0;
        var usage = new NetworkCheckResult(4, true, null, 0, uses);
        for (var mode = 0; mode < 4; mode++)
        {
            var layout = new NetworkLayout(network, usage, mode);
            Assert.True(layout.IsTruncated);
            Assert.Equal(NetworkTrace.DisplayStageLimit, layout.Bands.Count);
            Assert.Equal(mode == 0 ? count : count - 1, layout.TotalStages);
            Assert.Equal(layout.TotalStages, layout.TotalComparators);
        }
        uses[1] = 0;
        Assert.False(new NetworkLayout(network, usage, 3).IsTruncated);
    }

    [Fact]
    public void Failure_plot_toggle_restores_history_and_records_while_hidden()
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Generator.ChannelCount = 2;
        var model = vm.Generator.Snapshot().CreateModel();
        void Visit(int failures, int size)
        {
            var network = new NetworkDefinition("sample", RandomNetwork.generate("msce", 2, size, Guid.Empty));
            vm.RecordCandidate(new(model, network, new(4, true, null, failures, new int[size])), 0);
        }
        Visit(1, 5);
        Visit(0, 3);
        Visit(2, 3);
        var original = vm.Visits;
        vm.PlotFailures = false;
        Assert.Single(vm.Visits.Counts);
        Assert.Equal(1, vm.Visits.Counts[new System.Windows.Point(3, 3)]);
        Assert.Equal(1, vm.Visits.Maximum);
        vm.PlotFailures = true;
        Assert.Equal(original.Counts.OrderBy(pair => pair.Key.X), vm.Visits.Counts.OrderBy(pair => pair.Key.X));
        vm.PlotFailures = false;
        Visit(3, 8);
        Assert.Single(vm.Visits.Counts);
        vm.PlotFailures = true;
        Assert.Equal(3, vm.Visits.Counts.Count);
        Assert.Equal(new System.Windows.Point(8, 8), vm.Visits.Current);
        vm.PlotFailures = false;
        vm.ClearGraphCommand.Execute(null);
        vm.PlotFailures = true;
        Assert.Empty(vm.Visits.Counts);
    }

    [Fact]
    public void Network_history_navigation_keeps_selection_while_new_entries_arrive()
    {
        var network = CatalogNetwork.LoadAll()[0];
        var first = new NetworkHistoryEntry(network, null, "Initial");
        var second = new NetworkHistoryEntry(network, null, "Improvement");
        var history = new NetworkHistoryViewModel();
        Assert.False(history.PreviousCommand.CanExecute(null));
        Assert.False(history.NextCommand.CanExecute(null));
        history.Replace(new[] { first });
        history.Add(second);
        Assert.Same(second, history.Current);
        Assert.Equal("2 / 2", history.Position);
        history.PreviousCommand.Execute(null);
        Assert.Same(first, history.Current);
        Assert.False(history.PreviousCommand.CanExecute(null));
        var third = new NetworkHistoryEntry(network, null, "Next improvement");
        history.Add(third);
        Assert.Same(first, history.Current);
        Assert.Equal("1 / 3", history.Position);
        history.NextCommand.Execute(null);
        history.NextCommand.Execute(null);
        Assert.Same(third, history.Current);
        Assert.False(history.NextCommand.CanExecute(null));
        history.Replace(new[] { first });
        Assert.Single(history.Entries);
        Assert.Same(first, history.Current);
    }

    [Fact]
    public void Editing_a_history_index_selects_a_saved_network_while_live_results_arrive()
    {
        var history = new NetworkHistoryViewModel();
        var network = CatalogNetwork.LoadAll()[0];
        var saved = Enumerable.Range(1, 12).Select(i => new NetworkHistoryEntry(network, null, $"Network {i}")).ToArray();
        history.Replace(saved);
        var live = new NetworkHistoryEntry(network, null, "Live sample");
        history.UpdateLive(live);
        Assert.Equal("Live", history.IndexText);
        history.IndexText = "1";
        history.UpdateLive(live);
        Assert.Equal("1", history.IndexText);
        history.IndexText = "10";
        history.FinishIndexEdit(commit: true);
        Assert.Same(saved[9], history.Current);
        foreach (var invalid in new[] { "", "0", "13", "abc" })
        {
            history.IndexText = invalid;
            history.FinishIndexEdit(commit: true);
            Assert.Equal("10", history.IndexText);
            Assert.Same(saved[9], history.Current);
        }
        history.Inspect(new Point(network.ComparatorCount, network.StageCount), live);
        history.IndexText = "2";
        history.FinishIndexEdit(commit: false);
        Assert.Equal("Graph", history.IndexText);
        history.Inspect(new Point(0, 0), null);
        history.UpdateLive(live);
        Assert.Null(history.Current);
        Assert.Equal("Graph", history.IndexText);
        Assert.True(history.FollowLiveCommand.CanExecute(null));
        history.IndexText = "12";
        history.FinishIndexEdit(commit: true);
        Assert.Same(saved[^1], history.Current);
        history.FollowLiveCommand.Execute(null);
        Assert.Same(live, history.Current);
        history.Replace([]);
        Assert.False(history.HasEntries);
        Assert.Equal("0", history.IndexText);
    }

    [Fact]
    public void Evolution_history_adds_improvements_and_browsing_does_not_change_parent()
    {
        var vm = new SgdPageViewModel(() => { });
        var recipe = vm.Generator.Snapshot();
        var initial = NetworkSearch.Prepare(recipe.Label, recipe.CreateModel(), 16, CancellationToken.None);
        initial = initial with { Usage = initial.Usage with { Failed = 2 } };
        var improved = initial with { Usage = initial.Usage with { Failed = 1 }, RawUsage = initial.RawUsage with { Failed = 1 } };
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var publish = typeof(SgdPageViewModel).GetMethod("Publish", flags)!;
        publish.Invoke(vm, new object[] { initial, 0, false });
        var activity = typeof(SgdPageViewModel).GetField("activity", flags)!;
        activity.SetValue(vm, Enum.Parse(activity.FieldType, "Evolve"));
        publish.Invoke(vm, new object[] { improved, 1, false });
        Assert.Equal(2, vm.NetworkHistory.Entries.Count);
        vm.NetworkHistory.PreviousCommand.Execute(null);
        Assert.Same(initial.RawUsage, vm.DisplayUsage);
        Assert.Same(improved.RawUsage, vm.Usage);
        Assert.Same(improved, typeof(SgdPageViewModel).GetField("incumbent", flags)!.GetValue(vm));
        publish.Invoke(vm, new object[] { improved, 2, false });
        Assert.Equal(2, vm.NetworkHistory.Entries.Count);
        vm.NetworkHistory.NextCommand.Execute(null);
        Assert.Same(improved.RawUsage, vm.DisplayUsage);
    }

    [Fact]
    public void Graph_selection_keeps_latest_success_and_failure_without_adding_history()
    {
        var vm = new SgdPageViewModel(() => { });
        var recipe = vm.Generator.Snapshot();
        var candidate = NetworkSearch.Prepare(recipe.Label, recipe.CreateModel(), 16, CancellationToken.None);
        SearchCandidate Snapshot(string label, int failures) => candidate with
        {
            RawNetwork = new NetworkDefinition(label, candidate.RawNetwork.Sequence),
            Usage = candidate.Usage with { Failed = failures },
            RawUsage = candidate.RawUsage with { Failed = failures }
        };
        void Add(SearchCandidate entry) => vm.RecordCandidate(entry, 1);
        var success = Snapshot("success", 0);
        var failure = Snapshot("failure", 1);
        var latestSuccess = Snapshot("latest success", 0);
        var latestFailure = Snapshot("latest failure", 2);
        Add(success); Add(failure); Add(latestSuccess); Add(latestFailure);
        vm.ShowGraphNetwork(candidate.Position);
        Assert.Same(latestFailure.RawNetwork, vm.Network);
        Assert.Equal(candidate.Position, vm.SelectedNetworkPosition);
        Assert.Equal(candidate.Position, vm.SelectedAnnotation.VisiblePosition);
        Assert.Equal("Graph", vm.NetworkHistory.IndexText);
        Assert.Empty(vm.NetworkHistory.Entries);
        vm.PlotFailures = false;
        Assert.Same(latestSuccess.RawNetwork, vm.Network);
        vm.PlotFailures = true;
        Assert.Same(latestFailure.RawNetwork, vm.Network);
        foreach (var name in new[] { "ParentSuccess", "ParentFailure" })
            Assert.Equal(1, ((GraphNetworkBuffer)typeof(SgdPageViewModel).GetProperty(name)!.GetValue(vm)!).Count);
        vm.NetworkHistory.Add(new(success.RawNetwork, success.RawUsage, "initial"));
        vm.NetworkHistory.Add(new(failure.RawNetwork, failure.RawUsage, "improvement"));
        vm.Stop();
        Assert.Equal(2, vm.NetworkHistory.Entries.Count);
        vm.NetworkHistory.PreviousCommand.Execute(null);
        Assert.Same(success.RawNetwork, vm.Network);
        vm.ClearGraphCommand.Execute(null);
        Assert.Empty(vm.NetworkHistory.Entries);
        Assert.Null(vm.SelectedNetworkPosition);
        Assert.Empty(vm.Visits.Counts);
        vm.ShowGraphNetwork(candidate.Position);
        Assert.Null(vm.Network);
        vm.PlotFailures = false;
        vm.PlotFailures = true;
        Assert.Empty(vm.Visits.Counts);
    }

    [Fact]
    public void Selected_graph_point_follows_history_navigation_and_clear()
    {
        var vm = new SgdPageViewModel(() => { });
        var network = CatalogNetwork.LoadAll()[0];
        var first = new System.Windows.Point(10, 4);
        var second = new System.Windows.Point(8, 3);
        vm.NetworkHistory.Add(new(network, null, "Initial", first));
        vm.NetworkHistory.Add(new(network, null, "Improvement", second));
        Assert.Equal(second, vm.SelectedNetworkPosition);
        Assert.Equal(second, vm.SelectedAnnotation.VisiblePosition);
        vm.SelectedAnnotation.IsShown = false;
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
        vm.NetworkHistory.PreviousCommand.Execute(null);
        Assert.Equal(first, vm.SelectedNetworkPosition);
        Assert.Equal(first, vm.SelectedAnnotation.Position);
        Assert.Null(vm.SelectedAnnotation.VisiblePosition);
        vm.SelectedAnnotation.IsShown = true;
        Assert.Equal(first, vm.SelectedAnnotation.VisiblePosition);
        Assert.Contains(nameof(vm.SelectedNetworkPosition), notifications);
        vm.NetworkHistory.NextCommand.Execute(null);
        Assert.Equal(second, vm.SelectedNetworkPosition);
        Assert.Equal(second, vm.SelectedAnnotation.VisiblePosition);
        vm.ClearGraphCommand.Execute(null);
        Assert.Null(vm.SelectedNetworkPosition);
        Assert.False(vm.SelectedAnnotation.HasSorter);
    }

    [Fact]
    public void Four_graph_buffers_filter_independently_and_retain_latest_snapshots_while_hidden()
    {
        var vm = new SgdPageViewModel(() => { });
        var recipe = vm.Generator.Snapshot();
        var candidate = NetworkSearch.Prepare(recipe.Label, recipe.CreateModel(), 16, CancellationToken.None);
        var buffers = new[] { vm.ParentSuccess, vm.ParentFailure, vm.ChildSuccess, vm.ChildFailure };
        var latest = new SearchCandidate[4];
        void Add(int i, int number)
        {
            var failures = buffers[i].Successful ? 0 : 2;
            latest[i] = candidate with {
                RawNetwork = new NetworkDefinition($"buffer {i}, record {number}", candidate.RawNetwork.Sequence),
                Usage = candidate.Usage with { Failed = failures },
                RawUsage = candidate.RawUsage with { Failed = failures }
            };
            vm.RecordCandidate(latest[i], number, buffers[i].IsChild);
        }
        for (var i = 0; i < 4; i++) { Add(i, 1); Add(i, 2); }
        Assert.All(buffers, buffer => { Assert.Equal(2, buffer.TotalVisits); Assert.Equal(1, buffer.UniqueCount); Assert.Equal(1, buffer.Count); });
        for (var mask = 0; mask < 16; mask++)
        {
            for (var i = 0; i < 4; i++) buffers[i].IsVisible = (mask & (1 << i)) != 0;
            var visible = Enumerable.Range(0, 4).Where(i => buffers[i].IsVisible).ToArray();
            Assert.Equal(2L * visible.Length, vm.Visits.Counts.Values.Sum());
            Assert.Equal(visible.Length == 0 ? 0L : 1L, vm.PlotData.Counts.Values.Sum());
            if (visible.Length > 0)
            {
                Assert.Equal(candidate.Position, vm.Visits.Current);
                vm.ShowGraphNetwork(candidate.Position);
                var preferred = visible.Where(i => !buffers[i].IsChild).DefaultIfEmpty(visible[^1]).Last();
                Assert.Same(latest[preferred].RawNetwork, vm.Network);
                Assert.Empty(vm.NetworkHistory.Entries);
            }
            Assert.Equal(visible.Any(i => buffers[i].Successful), vm.BestPosition.HasValue);
        }
        foreach (var buffer in buffers) buffer.IsVisible = false;
        for (var i = 0; i < 4; i++) Add(i, 3);
        Assert.Empty(vm.Visits.Counts);
        for (var i = 0; i < 4; i++)
        {
            buffers[i].IsVisible = true;
            Assert.Equal(3L * (i + 1), vm.Visits.Counts.Values.Sum());
        }
        Assert.All(buffers, buffer => Assert.Equal(1, buffer.Count));
        vm.ClearGraphCommand.Execute(null);
        Assert.All(buffers, buffer => { Assert.Equal(0, buffer.TotalVisits); Assert.Equal(0, buffer.UniqueCount); Assert.Equal(0, buffer.Count); });
        Assert.Empty(vm.Visits.Counts);
    }

    [Fact]
    public void Category_uniqueness_counts_distinct_networks_at_same_point_and_scale_uses_visible_buffers()
    {
        var vm = new SgdPageViewModel(() => { });
        var recipe = vm.Generator.Snapshot();
        var model = recipe.CreateModel();
        var a = new NetworkDefinition("a", Sortopia.Sorting.Networks.stageSequence.create(new[] {
            Sortopia.Sorting.Networks.stage.create(new[] { Sortopia.Sorting.Networks.ce.create(0, 1) }, 4) }, 4));
        var b = new NetworkDefinition("b", Sortopia.Sorting.Networks.stageSequence.create(new[] {
            Sortopia.Sorting.Networks.stage.create(new[] { Sortopia.Sorting.Networks.ce.create(2, 3) }, 4) }, 4));
        var c = new NetworkDefinition("c", Sortopia.Sorting.Networks.stageSequence.create(new[] {
            Sortopia.Sorting.Networks.stage.create(new[] { Sortopia.Sorting.Networks.ce.create(0, 1) }, 4),
            Sortopia.Sorting.Networks.stage.create(new[] { Sortopia.Sorting.Networks.ce.create(1, 2) }, 4) }, 4));
        void Add(NetworkDefinition network, int failures, bool child) =>
            vm.RecordCandidate(new(model, network, new(16, true, null, failures, Enumerable.Repeat(1, network.ComparatorCount).ToArray())), 1, child);
        for (var i = 0; i < 5; i++) Add(a, 1, false);
        Add(b, 1, false);
        Add(b, 1, true);
        Add(c, 0, false); Add(c, 0, false);
        Assert.Equal(2, vm.ParentFailure.UniqueCount);
        Assert.Equal(1, vm.ParentFailure.Count);
        Assert.Equal(1, vm.ChildFailure.UniqueCount);
        Assert.Equal(1, vm.ParentSuccess.UniqueCount);
        Assert.Equal(7, vm.Visits.Maximum);
        Assert.Equal(2, vm.PlotData.Maximum); // b was recorded by both parent and child.
        Assert.Equal(1, vm.PlotData.Minimum);
        var bounds = vm.AllBufferBounds;
        vm.ShowVisitStats = true;
        Assert.Equal(7, vm.PlotData.Maximum);
        Assert.Equal(2, vm.PlotData.Minimum);
        vm.ShowVisitStats = false;
        Assert.Equal(bounds, vm.AllBufferBounds);
        vm.ParentFailure.IsVisible = false;
        Assert.Equal(2, vm.Visits.Maximum);
        Assert.Equal(1, vm.Visits.Counts[new System.Windows.Point(1, 1)]);
        Assert.False(vm.PlotData.HasColorRange);
        Add(a, 1, false);
        Assert.Equal(2, vm.ParentFailure.UniqueCount);
        Assert.Equal(2, vm.Visits.Maximum);
        Assert.Equal(1, vm.PlotData.Maximum);
        vm.ChildFailure.IsVisible = false;
        Assert.Single(vm.Visits.Counts);
        vm.ParentFailure.IsVisible = true;
        Assert.Equal(7, vm.Visits.Maximum);
        Assert.Equal(2, vm.PlotData.Maximum);
        vm.ClearGraphCommand.Execute(null);
        Assert.Equal(0, vm.ParentFailure.UniqueCount);
        Assert.Equal(0, vm.ChildFailure.UniqueCount);
        Assert.Equal(0, vm.ParentSuccess.UniqueCount);
        Assert.True(vm.AllBufferBounds.IsEmpty);
        Assert.Empty(vm.PlotData.Counts);
        Assert.Equal(0, vm.PlotData.Minimum);

        Add(a, 1, false);
        vm.ChildSuccess.IsVisible = false;
        Add(c, 0, true);
        Assert.Single(vm.Visits.Counts);
        Assert.Equal(new System.Windows.Rect(1, 1, 1, 1), vm.AllBufferBounds);
        vm.ParentFailure.IsVisible = false;
        Assert.Empty(vm.Visits.Counts);
        Assert.Equal(new System.Windows.Rect(1, 1, 1, 1), vm.AllBufferBounds);
        vm.ClearGroupCommand.Execute(vm.SecondGroup);
        Assert.Equal(new System.Windows.Rect(1, 1, 0, 0), vm.AllBufferBounds);
        vm.GenerationStepCommand.Execute(null);
        Assert.True(vm.AllBufferBounds.IsEmpty);
    }

    public static IEnumerable<object[]> CatalogEntries => CatalogNetwork.LoadAll().Select((_, index) => new object[] { index });

    [Theory]
    [MemberData(nameof(CatalogEntries))]
    public void Every_catalog_entry_has_correct_snapshots_and_unambiguous_geometry(int catalogIndex)
    {
        var network = CatalogNetwork.LoadAll()[catalogIndex];
        foreach (var input in new[] { Enumerable.Range(0, network.Width).Reverse().ToArray(), Enumerable.Range(0, network.Width).Select(i => i % 3).ToArray() })
        {
            var original = (int[])input.Clone();
            var trace = NetworkTrace.Create(network, input);
            Assert.Equal(original, input);
            Assert.Equal(network.ComparatorCount + 1, trace.Values.Count);
            Assert.Equal(original, trace.Values[0]);
            Assert.Equal(original.Order(), trace.Values[^1]);
            Assert.Equal(network.StageCount + 1, trace.StageBoundaries.Count);
            Assert.Equal(network.ComparatorCount, trace.StageBoundaries[^1]);
            for (var frame = 0; frame < trace.Values.Count; frame++)
            {
                Assert.Equal(Enumerable.Range(0, network.Width), trace.TokenChannels[frame].Order());
                for (var token = 0; token < network.Width; token++)
                    Assert.Equal(original[token], trace.Values[frame][trace.TokenChannels[frame][token]]);
            }
            var uses = new int[network.ComparatorCount];
            var byStage = sortableIntArray.create(input, network.Width, network.Width).SortByStagesWithHistory(network.Sequence, uses);
            for (var stage = 0; stage < byStage.Length; stage++)
                Assert.Equal(byStage[stage].Values, trace.Values[trace.StageBoundaries[stage + 1]]);
            Assert.Equal(uses, trace.SwapCounts);

            var layout = new NetworkLayout(trace);
            Assert.Equal(network.ComparatorCount, layout.Comparators.Count);
            Assert.Equal(network.StageCount, layout.Bands.Count);
            Assert.True(layout.FrameX.Zip(layout.FrameX.Skip(1)).All(pair => pair.First <= pair.Second));
            foreach (var column in layout.Comparators.GroupBy(ce => (ce.StageIndex, ce.X)))
            {
                var entries = column.ToArray();
                for (var i = 0; i < entries.Length; i++)
                    for (var j = i + 1; j < entries.Length; j++)
                        Assert.True(entries[i].High < entries[j].Low || entries[j].High < entries[i].Low);
            }
        }
    }

    [Fact]
    public void Trace_owns_its_input_and_exposes_read_only_frames()
    {
        var network = CatalogNetwork.LoadAll()[0];
        int[] input = [1, 0];
        var trace = NetworkTrace.Create(network, input);
        input[0] = 999;
        Assert.Equal(1, trace.Values[0][0]);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)trace.Values[0])[0] = 999);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)trace.TokenChannels[0])[0] = 999);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)trace.SwapCounts)[0] = 999);
    }

    [Fact]
    public void Playback_seek_mode_changes_and_replay_only_read_the_existing_trace() => OnSta(() =>
    {
        using var vm = new NetworkPlaybackViewModel(() => { }, CatalogNetwork.LoadAll().First(n => n.Width == 8));
        var trace = vm.Trace;
        Assert.False(vm.PreviousCommand.CanExecute(null));
        vm.PlayPauseCommand.Execute(null);
        vm.Advance(TimeSpan.FromSeconds(0.325));
        Assert.Equal(0.5, vm.Position, 6);
        vm.Pause();
        vm.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0.5, vm.Position, 6);
        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.Position);
        vm.PreviousCommand.Execute(null);
        Assert.Equal(0, vm.Position);
        vm.StepMode = 1;
        Assert.Equal(vm.SelectedNetwork.StageCount, vm.StepCount);
        vm.SeekPosition = vm.StepCount;
        Assert.False(vm.NextCommand.CanExecute(null));
        vm.PlayPauseCommand.Execute(null);
        Assert.Equal(0, vm.Position);
        vm.Advance(TimeSpan.FromSeconds(100));
        Assert.Equal(vm.StepCount, vm.Position);
        Assert.False(vm.IsPlaying);
        vm.SeekPosition = -5;
        Assert.Equal(0, vm.Position);
        Assert.Same(trace, vm.Trace);
        vm.Zoom = 0.6;
        vm.Speed = 2;
        Assert.Same(trace, vm.Trace);
    });

    [Fact]
    public void Changing_input_or_network_rebuilds_and_pauses_the_trace() => OnSta(() =>
    {
        using var vm = new NetworksPageViewModel(() => { });
        var before = vm.Trace;
        vm.PlayPauseCommand.Execute(null);
        vm.ShuffleCommand.Execute(null);
        Assert.False(vm.IsPlaying);
        Assert.Equal(0, vm.Position);
        Assert.NotSame(before, vm.Trace);
        Assert.Equal(Enumerable.Range(0, vm.SelectedNetwork.Width), vm.Trace.Values[0].Order());
        vm.SelectedNetwork = vm.Networks.Last();
        Assert.Equal(32, vm.Trace.Network.Width);
        Assert.Equal(0, vm.Position);
        vm.ReverseCommand.Execute(null);
        Assert.Equal(Enumerable.Range(0, 32).Reverse(), vm.Trace.Values[0]);
    });

    [Fact]
    public void Navigation_keeps_selection_and_position_but_pauses_playback() => OnSta(() =>
    {
        using var shell = new MainWindowViewModel(_ => null);
        var home = Assert.IsType<HomePageViewModel>(shell.CurrentPage);
        home.OpenNetworksCommand.Execute(null);
        var networks = Assert.IsType<NetworksPageViewModel>(shell.CurrentPage);
        networks.PlayPauseCommand.Execute(null);
        networks.Advance(TimeSpan.FromSeconds(0.325));
        networks.ReturnHomeCommand.Execute(null);
        Assert.Same(home, shell.CurrentPage);
        Assert.False(networks.IsPlaying);
        home.OpenNetworksCommand.Execute(null);
        Assert.Same(networks, shell.CurrentPage);
        Assert.Equal(0.5, networks.Position, 6);
        shell.Dispose();
        networks.PlayPauseCommand.Execute(null);
        Assert.False(networks.IsPlaying);
    });

    [Fact]
    public void Binary_mode_resets_playback_and_applies_to_all_input_commands_and_networks() => OnSta(() =>
    {
        using var vm = new NetworksPageViewModel(() => { });
        vm.SeekPosition = 3;
        vm.PlayPauseCommand.Execute(null);
        vm.UseZeroOneRule = true;
        Assert.False(vm.IsPlaying);
        Assert.Equal(0, vm.Position);
        Assert.Equal(new[] { 1, 1, 1, 1, 0, 0, 0, 0 }, vm.Trace.Values[0]);
        foreach (var network in vm.Networks)
        {
            vm.SelectedNetwork = network;
            vm.ShuffleCommand.Execute(null);
            Assert.All(vm.Trace.Values[0], value => Assert.InRange(value, 0, 1));
            Assert.Equal(vm.Trace.Values[0].Order(), vm.Trace.Values[^1]);
            vm.ReverseCommand.Execute(null);
            Assert.Equal(vm.Trace.Values[0].OrderDescending(), vm.Trace.Values[0]);
            Assert.Contains($"CE: {network.ComparatorCount}", vm.NetworkSummary);
            Assert.Contains($"Stages: {network.StageCount}", vm.NetworkSummary);
        }
        vm.UseZeroOneRule = false;
        Assert.Equal(Enumerable.Range(0, vm.SelectedNetwork.Width).Reverse(), vm.Trace.Values[0]);
        vm.ShuffleCommand.Execute(null);
        Assert.Equal(Enumerable.Range(0, vm.SelectedNetwork.Width), vm.Trace.Values[0].Order());
    });

    [Fact]
    public void Parameter_array_slider_tracks_the_selected_cell_and_updates_its_color() => OnSta(() =>
    {
        double[,] values = { { -2, -1, 0 }, { 0, 1, 2 } };
        string[] rows = ["First", "Second"];
        string[] columns = ["A", "B", "C"];
        var parameters = new ParameterArrayViewModel(values, -2, 2, rows, columns);
        Assert.Throws<ArgumentException>(() => new ParameterArrayViewModel(values, rowHeaders: ["First"]));
        Assert.Throws<ArgumentException>(() => new ParameterArrayViewModel(values, columnHeaders: ["A", "B"]));
        var editor = new ParamEditor2d { DataContext = parameters };
        editor.Measure(new Size(280, 240));
        editor.Arrange(new Rect(0, 0, 280, 240));
        editor.UpdateLayout();
        var cells = (ListBox)editor.FindName("Cells");
        var slider = (Slider)editor.FindName("ValueSlider");
        var valueEditor = (StackPanel)editor.FindName("ValueEditor");
        Assert.Equal(Visibility.Collapsed, valueEditor.Visibility);
        Button ButtonAt(int index) => Assert.IsType<Button>(
            VisualTreeHelper.GetChild(cells.ItemContainerGenerator.ContainerFromIndex(index), 0));
        void Select(Button button)
        {
            var item = (ListBoxItem)ItemsControl.ContainerFromElement(cells, button);
            var peer = new ListBoxItemAutomationPeer(item.DataContext, new ListBoxAutomationPeer(cells));
            ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)!).Select();
            editor.UpdateLayout();
        }

        var first = ButtonAt(0);
        var last = ButtonAt(5);
        var buttonWidth = first.ActualWidth;
        Assert.Equal(buttonWidth, last.ActualWidth);
        var rowLabels = (ItemsControl)editor.FindName("RowLabels");
        var columnLabels = (ItemsControl)editor.FindName("ColumnLabels");
        Assert.Equal(rows, rowLabels.Items.Cast<string>());
        Assert.Equal(columns, columnLabels.Items.Cast<string>());
        Point Center(FrameworkElement element) => element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), editor);
        Assert.Equal(Center(last).X, Center((FrameworkElement)columnLabels.ItemContainerGenerator.ContainerFromIndex(2)).X, 5);
        Assert.Equal(Center(last).Y, Center((FrameworkElement)rowLabels.ItemContainerGenerator.ContainerFromIndex(1)).Y, 5);
        Assert.Equal("Second, C", parameters.Cells[5].Label);
        Assert.Equal(ButtonAt(2).TranslatePoint(new Point(), editor).X, last.TranslatePoint(new Point(), editor).X);
        Assert.True(last.TranslatePoint(new Point(), editor).Y > first.TranslatePoint(new Point(), editor).Y);
        Assert.Equal(Colors.Blue, ((SolidColorBrush)first.Background).Color);
        Assert.Equal(Colors.Red, ((SolidColorBrush)last.Background).Color);
        Assert.Equal("-2", Assert.IsType<TextBlock>(first.Content).Text);
        Assert.Same(Brushes.White, first.Foreground);
        Assert.Same(Brushes.Black, last.Foreground);
        Select(last);
        Assert.Equal(Visibility.Visible, valueEditor.Visibility);
        Assert.Equal(2, slider.Value);
        slider.SetCurrentValue(Slider.ValueProperty, 0.0);
        Assert.Equal(0, values[1, 2]);
        Assert.Equal(Colors.Lime, ((SolidColorBrush)last.Background).Color);
        Select(first);
        Assert.DoesNotContain(parameters.Cells[5], cells.SelectedItems.Cast<ParameterCell>());
        Assert.Equal(-2, slider.Value);
        slider.SetCurrentValue(Slider.ValueProperty, -1.0);
        Assert.Equal(-1, values[0, 0]);
        Assert.Equal(Colors.Cyan, ((SolidColorBrush)first.Background).Color);
        Assert.Same(Brushes.Black, first.Foreground);
        slider.SetCurrentValue(Slider.ValueProperty, -1.23456);
        editor.UpdateLayout();
        var valueText = Assert.IsType<TextBlock>(first.Content);
        Assert.Equal("-1.23", valueText.Text);
        Assert.Equal(-1.23456, values[0, 0]); // Formatting never rounds the backing value.
        Assert.Equal(buttonWidth, first.ActualWidth);
        Assert.Equal(buttonWidth, last.ActualWidth);
        slider.SetCurrentValue(Slider.ValueProperty, -1.23456e-105);
        editor.UpdateLayout();
        Assert.Equal("-1.23E-105", valueText.Text);
        Assert.Equal(buttonWidth, first.ActualWidth);
        Assert.True(valueText.ActualWidth <= first.ActualWidth - 16);
        slider.SetCurrentValue(Slider.ValueProperty, -1.23456);
        Select(last);
        Assert.Equal(0, slider.Value);

        foreach (var width in new[] { 900.0, 180.0 })
        {
            editor.Measure(new Size(width, 240));
            editor.Arrange(new Rect(0, 0, width, 240));
            editor.UpdateLayout();
            var viewport = (ScrollViewer)editor.FindName("ArrayViewport");
            Assert.Equal(viewport.ActualWidth, valueEditor.ActualWidth, 5);
            Assert.InRange(slider.ActualWidth, 1, 220);
            var bounds = slider.TransformToAncestor(valueEditor).TransformBounds(new Rect(slider.RenderSize));
            Assert.True(bounds.Left >= 0 && bounds.Right <= valueEditor.ActualWidth);
        }

        Select(first);
        editor.DataContext = new ParameterArrayViewModel(new double[0, 0]);
        editor.UpdateLayout();
        Assert.Empty(cells.Items);
        Assert.Equal(Visibility.Collapsed, valueEditor.Visibility);
        Assert.Equal(-1.23456, values[0, 0]);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parameter_multi_selection_copies_the_first_value_and_edits_only_the_selected_group(bool logarithmic) => OnSta(() =>
    {
        var values = Enumerable.Range(1, 6).Select(i => logarithmic
            ? (IParameterValue)new MutationParameter($"Value {i}", i / 10.0)
            : new GenerationWeight($"Value {i}") { Value = i / 10.0 }).ToArray();
        var parameters = new ParameterArrayViewModel(["First", "Second"], ["A", "B", "C"],
            (row, column) => values[row * 3 + column], logarithmic ? -3 : 0, logarithmic ? 0 : 1);
        var editor = new ParamEditor2d { DataContext = parameters };
        editor.Measure(new Size(400, 240));
        editor.Arrange(new Rect(0, 0, 400, 240));
        editor.UpdateLayout();
        var cells = (ListBox)editor.FindName("Cells");
        var slider = (Slider)editor.FindName("ValueSlider");

        // Clicking an earlier array position must not replace the original selection's value.
        editor.SelectCell(parameters.Cells[4], extend: false);
        editor.SelectCell(parameters.Cells[1], extend: true);
        editor.UpdateLayout();
        Assert.Same(parameters.Cells[4], editor.FirstSelected);
        Assert.Equal(0.5, values[1].Value);
        Assert.Equal(0.3, values[2].Value);
        Assert.Equal(2, cells.SelectedItems.Count);
        Assert.Contains("2 selected", editor.SelectionLabel);
        foreach (var index in new[] { 1, 4 })
        {
            var button = (Button)VisualTreeHelper.GetChild(cells.ItemContainerGenerator.ContainerFromIndex(index), 0);
            Assert.Same(Brushes.White, ((Border)button.Template.FindName("Outline", button)).BorderBrush);
        }
        slider.SetCurrentValue(Slider.ValueProperty, logarithmic ? -2.0 : 0.25);
        var firstValue = logarithmic ? 0.01 : 0.25;
        Assert.Equal(firstValue, values[1].Value, 12);
        Assert.Equal(values[4].Value, values[1].Value);
        editor.SelectCell(parameters.Cells[5], extend: true);
        Assert.Equal(values[4].Value, values[5].Value);
        editor.SelectCell(parameters.Cells[1], extend: true); // Adding an existing member is harmless.
        Assert.Equal(3, cells.SelectedItems.Count);

        // Removing the anchor promotes the next selection without changing any backing value.
        cells.SelectedItems.Remove(parameters.Cells[4]);
        Assert.Same(parameters.Cells[1], editor.FirstSelected);
        slider.SetCurrentValue(Slider.ValueProperty, logarithmic ? -1.0 : 0.75);
        Assert.Equal(logarithmic ? 0.1 : 0.75, values[1].Value, 12);
        Assert.Equal(values[1].Value, values[5].Value);
        Assert.Equal(firstValue, values[4].Value, 12);

        editor.SelectCell(parameters.Cells[2], extend: false);
        Assert.Single(cells.SelectedItems);
        Assert.Equal(0.3, values[2].Value);
        Assert.Equal(logarithmic ? Math.Log10(0.3) : 0.3, slider.Value, 12);
        slider.SetCurrentValue(Slider.ValueProperty, logarithmic ? 0.0 : 0.9);
        Assert.Equal(logarithmic ? 1 : 0.9, values[2].Value);
        Assert.Equal(logarithmic ? 0.1 : 0.75, values[1].Value, 12);

        if (logarithmic) slider.SetCurrentValue(Slider.ValueProperty, -2.0);
        editor.SelectCell(parameters.Cells[0], extend: true);
        var originalValues = values.Select(value => value.Value).ToArray();
        double[,] nextValues = { { 0.1, 0.2 } };
        var next = new ParameterArrayViewModel(nextValues);
        editor.DataContext = next;
        editor.UpdateLayout();
        Assert.Empty(cells.SelectedItems);
        Assert.Null(editor.FirstSelected);
        Assert.Equal(Visibility.Collapsed, ((StackPanel)editor.FindName("ValueEditor")).Visibility);
        editor.SelectCell(next.Cells[0], extend: false);
        editor.SelectCell(next.Cells[1], extend: true);
        slider.SetCurrentValue(Slider.ValueProperty, 0.4);
        Assert.Equal(0.4, nextValues[0, 0]);
        Assert.Equal(0.4, nextValues[0, 1]);
        Assert.Equal(originalValues, values.Select(value => value.Value));
    });

    [Fact]
    public void Opening_and_switching_projects_scopes_runs_and_manifests_to_the_selected_folder() => OnSta(() =>
    {
        var temp = System.IO.Directory.CreateTempSubdirectory("Sortopia-projects-");
        try
        {
            var a = temp.CreateSubdirectory("A").FullName;
            var b = temp.CreateSubdirectory("B").FullName;
            var selections = new Queue<string?>([null, a, null, b, a]);
            using var shell = new MainWindowViewModel(_ => selections.Dequeue());
            var home = Assert.IsType<HomePageViewModel>(shell.CurrentPage);
            home.OpenSandboxCommand.Execute(null);
            var sandbox = Assert.IsType<SandboxPageViewModel>(shell.CurrentPage);
            sandbox.OpenSortableTestsCommand.Execute(null);
            Assert.Same(sandbox, shell.CurrentPage);
            sandbox.OpenProjectFolderCommand.Execute(null);
            var page = Assert.IsType<SortableTestsPageViewModel>(shell.CurrentPage);
            Assert.Equal(System.IO.Path.Combine(a, "runs"), page.LibraryPath);
            var library = new Runs.RunLibrary(page.LibraryPath);
            var run = library.Execute("A dataset", [new Runs.MergeTests(4, 2)], true, TestContext.Current.CancellationToken);
            var manifest = Assert.Single(library.Manifests(run.Id));
            var runDir = System.IO.Path.Combine(page.LibraryPath, run.Id.ToString("N"));
            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(runDir, "manifests", $"{manifest.Id:N}.json")));
            var result = Assert.Single(Storage.ProjectFileStore.FromRunsFolder(page.LibraryPath).ListResults(run.Id));
            Assert.Equal(run.Outputs[0].ArtifactId, result.DataId);
            page.Back.Execute(null);
            home.OpenSandboxCommand.Execute(null);
            sandbox.OpenProjectFolderCommand.Execute(null); // Cancel the switch.
            sandbox.OpenSortableTestsCommand.Execute(null);
            Assert.Same(page, shell.CurrentPage);
            page.Back.Execute(null);
            home.OpenSandboxCommand.Execute(null);
            sandbox.OpenProjectFolderCommand.Execute(null);
            page = Assert.IsType<SortableTestsPageViewModel>(shell.CurrentPage);
            Assert.Equal(System.IO.Path.Combine(b, "runs"), page.LibraryPath);
            Assert.Empty(page.Runs);
            Assert.Empty(page.Manifests);
            page.Back.Execute(null);
            home.OpenSandboxCommand.Execute(null);
            sandbox.OpenProjectFolderCommand.Execute(null);
            page = Assert.IsType<SortableTestsPageViewModel>(shell.CurrentPage);
            Assert.Equal(run.Id, Assert.Single(page.Runs).Id);
            Assert.Equal(manifest.Id, Assert.Single(page.Manifests).Id);
        }
        finally { temp.Delete(recursive: true); }
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
