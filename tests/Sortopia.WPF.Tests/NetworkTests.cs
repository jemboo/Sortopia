using System.Runtime.ExceptionServices;
using Sortopia.Sorting.Inputs;
using Sortopia.WPF.Models;
using Sortopia.WPF.Rendering;
using Sortopia.WPF.ViewModels;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class NetworkTests
{
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
        using var vm = new NetworksPageViewModel(() => { });
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
        networks.SeekPosition = 3;
        networks.PlayPauseCommand.Execute(null);
        networks.ReturnHomeCommand.Execute(null);
        Assert.Same(home, shell.CurrentPage);
        Assert.False(networks.IsPlaying);
        home.OpenNetworksCommand.Execute(null);
        Assert.Same(networks, shell.CurrentPage);
        Assert.Equal(3, networks.Position);
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
            home.OpenSortableTestsCommand.Execute(null);
            Assert.Same(home, shell.CurrentPage);
            home.OpenProjectFolderCommand.Execute(null);
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
            home.OpenProjectFolderCommand.Execute(null); // Cancel the switch.
            home.OpenSortableTestsCommand.Execute(null);
            Assert.Same(page, shell.CurrentPage);
            page.Back.Execute(null);
            home.OpenProjectFolderCommand.Execute(null);
            page = Assert.IsType<SortableTestsPageViewModel>(shell.CurrentPage);
            Assert.Equal(System.IO.Path.Combine(b, "runs"), page.LibraryPath);
            Assert.Empty(page.Runs);
            Assert.Empty(page.Manifests);
            page.Back.Execute(null);
            home.OpenProjectFolderCommand.Execute(null);
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
