using System.Runtime.ExceptionServices;
using Sortopia.Models.Sorting;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class RandomNetworkTests
{
    [Theory]
    [InlineData("msce", 7, 19)]
    [InlineData("mssi", 7, 5)]
    [InlineData("msrs", 10, 5)]
    [InlineData("msuf4", 16, 5)]
    [InlineData("msuf6", 24, 5)]
    public void Generated_networks_preserve_requested_size_and_replay(string kind, int width, int size)
    {
        var id = Guid.Parse("8741ece5-b169-45ba-8556-c87329db496d");
        var first = new NetworkDefinition(kind, RandomNetwork.generate(kind, width, size, id));
        var second = new NetworkDefinition(kind, RandomNetwork.generate(kind, width, size, id));
        Assert.Equal(width, first.Width);
        Assert.Equal(first.Comparators, second.Comparators);
        if (kind == "msce") Assert.Equal(size, first.ComparatorCount);
        else
        {
            Assert.Equal(size, first.StageCount);
            Assert.All(first.StageLengths, length => Assert.Equal(width / 2, length));
        }
        foreach (var stage in first.Sequence.Stages)
        {
            var endpoints = stage.Ces.SelectMany(ce => new[] { ce.LowChannel, ce.HighChannel }).ToArray();
            Assert.Equal(endpoints.Length, endpoints.Distinct().Count());
            Assert.All(endpoints, endpoint => Assert.InRange(endpoint, 0, width - 1));
        }
        var trace = NetworkTrace.Create(first, Enumerable.Range(0, width).Reverse().ToArray());
        Assert.Equal(first.ComparatorCount + 1, trace.Values.Count);
    }

    [Theory]
    [InlineData("msrs", 7)]
    [InlineData("msuf4", 12)]
    [InlineData("msuf4", 2)]
    [InlineData("msuf6", 6)]
    [InlineData("msuf6", 18)]
    [InlineData("unknown", 8)]
    public void Invalid_method_widths_are_rejected(string kind, int width)
    {
        Assert.False(RandomNetwork.supportsWidth(kind, width));
        Assert.Throws<ArgumentException>(() => RandomNetwork.generate(kind, width, 3, Guid.NewGuid()));
    }

    [Fact]
    public void Checks_distinguish_proof_samples_and_counterexamples()
    {
        var small = CatalogNetwork.LoadAll().First(n => n.Width == 8);
        var verified = NetworkCheck.Run(small, CancellationToken.None);
        Assert.True(verified.Exhaustive);
        Assert.Equal(256, verified.Tested);
        Assert.Null(verified.Counterexample);
        var large = CatalogNetwork.LoadAll().First(n => n.Width == 32);
        var sampled = NetworkCheck.Run(large, CancellationToken.None);
        Assert.False(sampled.Exhaustive);
        Assert.Equal(4096, sampled.Tested);
        Assert.Null(sampled.Counterexample);
        var incomplete = new NetworkDefinition("one CE", RandomNetwork.generate("msce", 8, 1, Guid.NewGuid()));
        var failure = NetworkCheck.Run(incomplete, CancellationToken.None);
        Assert.NotNull(failure.Counterexample);
        var output = NetworkTrace.Create(incomplete, failure.Counterexample!).Values[^1];
        Assert.False(output.SequenceEqual(output.Order()));
        Assert.Throws<OperationCanceledException>(() => NetworkCheck.Run(small, new CancellationToken(true)));
    }

    [Fact]
    public void Controls_enforce_method_constraints_without_changing_current_network() => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        var initial = vm.Trace;
        vm.Method = "msuf6";
        Assert.Equal(new[] { 12, 24, 48 }, vm.ChannelCounts);
        Assert.Equal(12, vm.ChannelCount);
        Assert.Same(initial, vm.Trace);
        foreach (var invalid in new[] { "", "0", "-1", "1.5", "10001", "text" })
        {
            vm.CountText = invalid;
            Assert.False(vm.GenerateCommand.CanExecute(null));
        }
        vm.CountText = "3";
        vm.GenerateCommand.Execute(null);
        Assert.Equal(12, vm.Trace.Network.Width);
        Assert.Equal(3, vm.Trace.Network.StageCount);
        vm.Method = "msce";
        vm.CountText = "11";
        vm.GenerateCommand.Execute(null);
        Assert.Equal(11, vm.Trace.Network.ComparatorCount);
        vm.Method = "mssi";
        vm.ChannelCount = 5;
        vm.CountText = "4";
        vm.GenerateCommand.Execute(null);
        Assert.Equal(4, vm.Trace.Network.StageCount);
        Assert.Equal(8, vm.Trace.Network.ComparatorCount);
    });

    [Fact]
    public void Random_navigation_preserves_both_pages_independently() => OnSta(() =>
    {
        using var shell = new MainWindowViewModel();
        var home = Assert.IsType<HomePageViewModel>(shell.CurrentPage);
        home.OpenNetworksCommand.Execute(null);
        var catalog = Assert.IsType<NetworksPageViewModel>(shell.CurrentPage);
        catalog.SeekPosition = 3;
        catalog.ReturnHomeCommand.Execute(null);
        home.OpenRandomNetworksCommand.Execute(null);
        var random = Assert.IsType<RandomNetworksPageViewModel>(shell.CurrentPage);
        random.SeekPosition = 2;
        random.PlayPauseCommand.Execute(null);
        random.ReturnHomeCommand.Execute(null);
        Assert.Same(home, shell.CurrentPage);
        Assert.False(random.IsPlaying);
        home.OpenNetworksCommand.Execute(null);
        Assert.Same(catalog, shell.CurrentPage);
        Assert.Equal(3, catalog.Position);
        catalog.ReturnHomeCommand.Execute(null);
        home.OpenRandomNetworksCommand.Execute(null);
        Assert.Same(random, shell.CurrentPage);
        Assert.Equal(2, random.Position);
    });

    [Fact]
    public void SplitMix_floats_cover_unit_interval_and_preserve_random_stream()
    {
        var floats = new Sortopia.Core.randomSplitMix64(42UL);
        var integers = new Sortopia.Core.randomSplitMix64(42UL);
        var buckets = new int[10];
        for (var i = 0; i < 10000; i++)
        {
            var value = floats.NextFloat;
            Assert.True(value >= 0 && value < 1);
            Assert.Equal((integers.NextULong >> 11) / 9007199254740992d, value);
            buckets[(int)(value * 10)]++;
        }
        Assert.All(buckets, count => Assert.InRange(count, 800, 1200));
        Assert.Equal(integers.ByteCount, floats.ByteCount);
    }

    [Theory]
    [InlineData("msce", 16)]
    [InlineData("mssi", 16)]
    [InlineData("msrs", 16)]
    [InlineData("msuf4", 16)]
    [InlineData("msuf6", 24)]
    public void Seed_controls_replay_and_generator_variety(string method, int width) => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        vm.Method = method;
        vm.ChannelCount = width;
        vm.CountText = "32";
        vm.SeedText = "42";
        vm.GenerateCommand.Execute(null);
        var first = vm.SelectedNetwork.Comparators.ToArray();
        vm.GenerateCommand.Execute(null);
        Assert.Equal(first, vm.SelectedNetwork.Comparators);
        Assert.Contains("seed 42", vm.GenerationLabel);
        vm.SeedText = "43";
        vm.GenerateCommand.Execute(null);
        Assert.False(first.SequenceEqual(vm.SelectedNetwork.Comparators));
        if (method != "msce")
        {
            var stages = vm.SelectedNetwork.Sequence.Stages.Select(s => string.Join(";", s.Ces.Select(c => $"{c.LowChannel},{c.HighChannel}")));
            Assert.True(stages.Distinct().Count() > 3);
        }
        foreach (var invalid in new[] { "", "-1", "2147483648", "1.5", "abc" })
        {
            vm.SeedText = invalid;
            Assert.False(vm.GenerateCommand.CanExecute(null));
        }
        vm.NewSeedCommand.Execute(null);
        Assert.True(vm.GenerateCommand.CanExecute(null));
    });
    [Fact]
    public void Exhaustive_testing_reaches_18_channels_and_rejects_19()
    {
        var network = new NetworkDefinition("18", RandomNetwork.generate("msce", 18, 1, Guid.Empty));
        var result = NetworkCheck.Run(network, CancellationToken.None);
        Assert.True(result.Exhaustive);
        Assert.Equal(1 << 18, result.Tested);
        Assert.True(result.Failed > 0);
        Assert.Equal(1 << 16, result.CeUses.Single());
        var large = new NetworkDefinition("19", RandomNetwork.generate("msce", 19, 1, Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkCheck.Run(large, null, CancellationToken.None));
        Assert.False(NetworkCheck.Run(large, CancellationToken.None).Exhaustive);
    }

    [Fact]
    public void Usage_counts_match_every_input_including_after_first_failure()
    {
        var network = new NetworkDefinition("short", RandomNetwork.generate("msce", 4, 4, Guid.Empty));
        var expected = new int[network.ComparatorCount];
        var failures = 0;
        for (var bits = 0; bits < 16; bits++)
        {
            var trace = NetworkTrace.Create(network, Enumerable.Range(0, 4).Select(i => (bits >> i) & 1).ToArray());
            for (var i = 0; i < expected.Length; i++) expected[i] += trace.SwapCounts[i];
            if (!trace.Values[^1].SequenceEqual(trace.Values[^1].Order())) failures++;
        }
        var result = NetworkCheck.Run(network, null, CancellationToken.None);
        Assert.Equal(16, result.Tested);
        Assert.Equal(failures, result.Failed);
        Assert.Equal(expected, result.CeUses);
        var sampled = NetworkCheck.Run(network, 123, CancellationToken.None);
        Assert.Equal(123, sampled.Tested);
        Assert.False(sampled.Exhaustive);
        Assert.Equal(sampled.CeUses, NetworkCheck.Run(network, 123, CancellationToken.None).CeUses);
        Assert.All(sampled.CeUses, n => Assert.InRange(n, 0, 123));
        foreach (var count in new[] { 0, -1, NetworkCheck.MaxSamples + 1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => NetworkCheck.Run(network, count, CancellationToken.None));
    }

    [Fact]
    public void Test_controls_validate_against_generated_network() => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        Assert.Equal("all", vm.SamplesText);
        Assert.True(vm.CanTestAll);
        vm.ChannelCount = 20;
        Assert.True(vm.CanTestAll); // The displayed network is still eight channels.
        vm.GenerateCommand.Execute(null);
        Assert.False(vm.CanTestAll);
        Assert.Equal("4096", vm.SamplesText);
        vm.SamplesText = "all";
        Assert.False(vm.TestCommand.CanExecute(null));
        vm.SamplesText = "17";
        Assert.True(vm.TestCommand.CanExecute(null));
        foreach (var invalid in new[] { "", "0", "-1", "1.5", "1000001", "text" })
        {
            vm.SamplesText = invalid;
            Assert.False(vm.TestCommand.CanExecute(null));
        }
        vm.ChannelCount = 18;
        vm.GenerateCommand.Execute(null);
        vm.SamplesText = "ALL";
        Assert.True(vm.TestCommand.CanExecute(null));
    });

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(30, 2)]
    [InlineData(31, 3)]
    [InlineData(100, 3)]
    [InlineData(300, 4)]
    [InlineData(1000, 5)]
    [InlineData(3000, 6)]
    [InlineData(10000, 7)]
    [InlineData(30000, 8)]
    [InlineData(100000, 9)]
    [InlineData(300000, 10)]
    [InlineData(300001, 11)]
    [InlineData(1000000, 11)]
    public void Usage_colors_have_explicit_percentage_boundaries(int uses, int level) =>
        Assert.Equal(level, Sortopia.WPF.Controls.NetworkDiagram.UsageLevel(uses, 1000000));
    private static NetworkDefinition RedundantNetwork()
    {
        var ce01 = Sortopia.Sorting.Networks.ce.create(0, 1);
        var ce23 = Sortopia.Sorting.Networks.ce.create(2, 3);
        var stages = new[] { ce01, ce01, ce23 }.Select(c => Sortopia.Sorting.Networks.stage.create(new[] { c }, 4)).ToArray();
        return new("redundant", Sortopia.Sorting.Networks.stageSequence.create(stages, 4));
    }

    [Fact]
    public void Removing_unused_CEs_and_compacting_preserve_outputs()
    {
        var original = RedundantNetwork();
        var result = NetworkCheck.Run(original, CancellationToken.None);
        Assert.Equal(new[] { 4, 0, 4 }, result.CeUses);
        var pruned = NetworkTransforms.RemoveUnusedCes(original, result);
        Assert.Equal(2, pruned.StageCount);
        Assert.Equal(2, pruned.ComparatorCount);
        var compacted = NetworkTransforms.Compact(pruned);
        Assert.Equal(1, compacted.StageCount);
        Assert.Equal(2, compacted.ComparatorCount);
        for (var i = 0; i < 16; i++)
        {
            var input = Enumerable.Range(0, 4).Select(wire => (i >> wire) & 1).ToArray();
            Assert.Equal(NetworkTrace.Create(original, input).Values[^1], NetworkTrace.Create(compacted, input).Values[^1]);
        }
        var partial = new NetworkDefinition("partly used", Sortopia.Sorting.Networks.stageSequence.create(new[] {
            Sortopia.Sorting.Networks.stage.create(new[] { original.Comparators[0] }, 4),
            Sortopia.Sorting.Networks.stage.create(new[] { original.Comparators[1], original.Comparators[2] }, 4)
        }, 4));
        var retained = NetworkTransforms.RemoveUnusedCes(partial, NetworkCheck.Run(partial, CancellationToken.None));
        Assert.Equal(2, retained.StageCount);
        Assert.Equal(2, retained.ComparatorCount); // Remove unused CEs even inside a used stage.
        Assert.Single(retained.Sequence.Stages[1].Ces);
        var packed = NetworkTransforms.Compact(retained);
        Assert.Equal(1, packed.StageCount);
        for (var bits = 0; bits < 16; bits++)
        {
            var input = Enumerable.Range(0, 4).Select(wire => (bits >> wire) & 1).ToArray();
            Assert.Equal(NetworkTrace.Create(partial, input).Values[^1], NetworkTrace.Create(packed, input).Values[^1]);
        }
        var unused = new NetworkCheckResult(1, false, null, 0, Array.AsReadOnly(new int[3]));
        var empty = NetworkTransforms.RemoveUnusedCes(original, unused);
        Assert.Equal(0, empty.StageCount);
        Assert.Equal(0, empty.ComparatorCount);
        Assert.Single(NetworkTrace.Create(empty, new[] { 3, 2, 1, 0 }).Values);
    }

    [Fact]
    public void Stage_actions_preserve_usage_after_edit() => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        Assert.False(vm.RemoveUnusedCesCommand.CanExecute(null));
        var source = RedundantNetwork();
        // Neither whole stage is unused, but the second stage contains one unused CE.
        vm.SelectedNetwork = new NetworkDefinition("partly used", Sortopia.Sorting.Networks.stageSequence.create(new[] {
            Sortopia.Sorting.Networks.stage.create(new[] { source.Comparators[0] }, 4),
            Sortopia.Sorting.Networks.stage.create(new[] { source.Comparators[1], source.Comparators[2] }, 4)
        }, 4));
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
        try
        {
            var task = vm.RunTestAsync();
            var frame = new System.Windows.Threading.DispatcherFrame();
            task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        Assert.Equal(1, vm.UnusedCeCount);
        Assert.True(vm.RemoveUnusedCesCommand.CanExecute(null));
        var input = vm.Trace.Values[0].ToArray();
        vm.RemoveUnusedCesCommand.Execute(null);
        Assert.NotNull(vm.Usage);
        Assert.Equal(new[] { 4, 4 }, vm.Usage.CeUses);
        Assert.Equal(16, vm.Usage.Tested);
        Assert.Equal(2, vm.SelectedNetwork.StageCount);
        Assert.False(vm.RemoveUnusedCesCommand.CanExecute(null));
        Assert.Equal(input, vm.Trace.Values[0]);
        Assert.True(vm.CompactStagesCommand.CanExecute(null));
        vm.CompactStagesCommand.Execute(null);
        Assert.Equal(1, vm.SelectedNetwork.StageCount);
        Assert.Equal(new[] { 4, 4 }, vm.Usage!.CeUses);
        Assert.Same(vm.Usage, vm.DisplayedUsage);
        Assert.False(vm.CompactStagesCommand.CanExecute(null));
        Assert.Equal(input, vm.Trace.Values[0]);
    });
    [Fact]
    public void Usage_follows_reordered_and_repeated_comparator_occurrences()
    {
        var pairs = new[] { (4, 5), (0, 1), (4, 5), (2, 3) };
        var stages = pairs.Select(p => Sortopia.Sorting.Networks.stage.create(new[] {
            Sortopia.Sorting.Networks.ce.create(p.Item1, p.Item2) }, 6)).ToArray();
        var network = new NetworkDefinition("reordered", Sortopia.Sorting.Networks.stageSequence.create(stages, 6));
        var usage = new NetworkCheckResult(100, false, null, 0, Array.AsReadOnly(new[] { 9, 7, 2, 5 }));
        var compacted = NetworkTransforms.Compact(network);
        var mapped = NetworkTransforms.PreserveUsage(network, compacted, usage, false)!;
        Assert.Equal(new[] { 7, 5, 9, 2 }, mapped.CeUses);
        Assert.Equal(100, mapped.Tested);
        Assert.False(mapped.Exhaustive);
        var withUnused = usage with { CeUses = Array.AsReadOnly(new[] { 0, 7, 2, 5 }) };
        var pruned = NetworkTransforms.RemoveUnusedCes(network, withUnused);
        var remaining = NetworkTransforms.PreserveUsage(network, pruned, withUnused, true)!;
        Assert.Equal(new[] { 7, 2, 5 }, remaining.CeUses);
        Assert.Equal(new[] { 7, 5, 2 }, NetworkTransforms.PreserveUsage(pruned, NetworkTransforms.Compact(pruned), remaining, false)!.CeUses);
        Assert.Null(NetworkTransforms.PreserveUsage(network, compacted, null, false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(123)]
    public void Preserved_usage_matches_retesting_the_same_inputs(int? samples)
    {
        var network = new NetworkDefinition("random", RandomNetwork.generate("msce", 8, 60, Guid.Empty));
        var usage = NetworkCheck.Run(network, samples, CancellationToken.None);
        var pruned = NetworkTransforms.RemoveUnusedCes(network, usage);
        var kept = NetworkTransforms.PreserveUsage(network, pruned, usage, true)!;
        var compacted = NetworkTransforms.Compact(pruned);
        var mapped = NetworkTransforms.PreserveUsage(pruned, compacted, kept, false)!;
        var checkedAgain = NetworkCheck.Run(compacted, samples, CancellationToken.None);
        Assert.Equal(checkedAgain.CeUses, mapped.CeUses);
        Assert.Equal(checkedAgain.Failed, mapped.Failed);
        Assert.Equal(checkedAgain.Tested, mapped.Tested);
        Assert.Equal(checkedAgain.Counterexample, mapped.Counterexample);
    }
    [Fact]
    public void Large_generation_limits_allocate_only_bounded_playback_history() => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        vm.CountText = "100000";
        vm.GenerateCommand.Execute(null);
        Assert.Equal(100000, vm.SelectedNetwork.ComparatorCount);
        Assert.True(vm.IsDiagramAvailable);
        Assert.True(vm.Trace.IsPreview);
        Assert.Same(vm.Trace, vm.DisplayTrace);
        Assert.InRange(vm.Trace.Values.Count, 2, NetworkTrace.DisplayCeLimit + 1);
        Assert.InRange(vm.Trace.Comparators.Count, 1, NetworkTrace.DisplayCeLimit);
        Assert.InRange(vm.StepCount, 1, NetworkTrace.DisplayCeLimit);
        Assert.True(vm.PlayPauseCommand.CanExecute(null));
        vm.UseZeroOneRule = true;
        vm.ShuffleCommand.Execute(null);
        Assert.InRange(vm.Trace.Values.Count, 2, NetworkTrace.DisplayCeLimit + 1);
        vm.CountText = "1000000";
        Assert.True(vm.GenerateCommand.CanExecute(null));
        vm.CountText = "1000001";
        Assert.False(vm.GenerateCommand.CanExecute(null));
        vm.Method = "msrs";
        vm.CountText = "10000";
        vm.GenerateCommand.Execute(null);
        Assert.Equal(10000, vm.SelectedNetwork.StageCount);
        Assert.True(vm.IsDiagramAvailable);
        vm.CountText = "10001";
        Assert.False(vm.GenerateCommand.CanExecute(null));
    });

    [Fact]
    public void Long_network_can_be_tested_pruned_and_displayed_with_bounded_history()
    {
        var network = new NetworkDefinition("long", RandomNetwork.generate("msce", 2, 6000, Guid.Empty));
        var preview = NetworkTrace.CreateForPlayback(network, new[] { 1, 0 });
        Assert.True(preview.IsPreview);
        Assert.Equal(NetworkTrace.DisplayStageLimit + 1, preview.Values.Count);
        Assert.Equal(preview.Values.Count, preview.TokenChannels.Count);
        Assert.Equal(NetworkTrace.DisplayStageLimit, preview.FrameIndex(5000, true));
        var layout = new Sortopia.WPF.Rendering.NetworkLayout(preview);
        Assert.Equal(NetworkTrace.DisplayStageLimit, layout.Bands.Count);
        Assert.Equal(preview.Comparators.Count, layout.Comparators.Count);
        Assert.True(layout.Width > layout.CutoffX);
        Assert.Equal(new[] { 0, 1 }, preview.Values[^1]);
        var result = NetworkCheck.Run(network, CancellationToken.None);
        Assert.Equal(4, result.Tested);
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.CeUses[0]);
        Assert.All(result.CeUses.Skip(1), n => Assert.Equal(0, n));
        var pruned = NetworkTransforms.RemoveUnusedCes(network, result);
        var retained = NetworkTransforms.PreserveUsage(network, pruned, result, true);
        var playback = NetworkTrace.CreateForPlayback(pruned, new[] { 1, 0 });
        Assert.False(playback.IsPreview);
        Assert.Equal(2, playback.Values.Count);
        Assert.Equal(new[] { 1 }, retained!.CeUses);
        Assert.Equal(new[] { 0, 1 }, playback.Values[^1]);
        var stages = new NetworkDefinition("many stages", RandomNetwork.generate("msce", 2, 513, Guid.Empty));
        Assert.True(NetworkTrace.CreateForPlayback(stages, new[] { 1, 0 }).IsPreview);
    }
    [Fact]
    public void Compact_button_disables_after_use_until_network_changes() => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        vm.SelectedNetwork = RedundantNetwork();
        Assert.True(vm.CompactStagesCommand.CanExecute(null));
        vm.CompactStagesCommand.Execute(null);
        Assert.Equal(2, vm.SelectedNetwork.StageCount);
        Assert.False(vm.CompactStagesCommand.CanExecute(null));
        vm.Method = "mssi";
        vm.ChannelCount = 2;
        vm.CountText = "3";
        vm.GenerateCommand.Execute(null);
        Assert.True(vm.CompactStagesCommand.CanExecute(null));
        vm.CompactStagesCommand.Execute(null); // Already compact is still a completed action.
        Assert.Equal(3, vm.SelectedNetwork.StageCount);
        Assert.False(vm.CompactStagesCommand.CanExecute(null));
    });

    [Fact]
    public void Prefix_respects_CE_cap_at_whole_stage_boundary()
    {
        var network = new NetworkDefinition("wide", RandomNetwork.generate("mssi", 64, 200, Guid.Empty));
        var trace = NetworkTrace.CreateForPlayback(network, Enumerable.Range(0, 64).Reverse().ToArray());
        Assert.True(trace.IsPreview);
        Assert.Equal(4096, trace.Comparators.Count);
        Assert.Equal(128, trace.DisplayStageCount);
        Assert.Same(network, trace.Network);
        Assert.Equal(6400, trace.Network.ComparatorCount);
        var full = NetworkTrace.Create(network, Enumerable.Range(0, 64).Reverse().ToArray());
        Assert.Equal(full.Values[4096], trace.Values[^1]);
        Assert.Equal(full.SwapCounts.Take(4096), trace.SwapCounts);
    }
    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
