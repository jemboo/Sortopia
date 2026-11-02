using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Sortopia.Models.Sorting;
using Sortopia.Sorting.Catalog;
using Sortopia.WPF.Controls;
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
        using var shell = new MainWindowViewModel(_ => null);
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
        vm.FinishCompaction();
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
        Assert.False(vm.RemoveUnusedCesCommand.CanExecute(null));
        Assert.False(vm.CompactStagesCommand.CanExecute(null));
        CompleteTest(vm);
        Assert.False(vm.CompactStagesCommand.CanExecute(null));
        vm.RemoveUnusedCesCommand.Execute(null);
        Assert.True(vm.CompactStagesCommand.CanExecute(null));
        vm.CompactStagesCommand.Execute(null);
        vm.FinishCompaction();
        Assert.Equal(1, vm.SelectedNetwork.StageCount);
        Assert.False(vm.CompactStagesCommand.CanExecute(null));
        vm.Method = "mssi";
        vm.ChannelCount = 2;
        vm.CountText = "3";
        vm.GenerateCommand.Execute(null);
        vm.SelectedNetwork = new NetworkDefinition("already compact", Sortopia.Sorting.Networks.stageSequence.create(new[] {
            Sortopia.Sorting.Networks.stage.create(new[] { Sortopia.Sorting.Networks.ce.create(0, 1) }, 3),
            Sortopia.Sorting.Networks.stage.create(new[] { Sortopia.Sorting.Networks.ce.create(1, 2) }, 3)
        }, 3));
        Assert.False(vm.RemoveUnusedCesCommand.CanExecute(null));
        Assert.False(vm.CompactStagesCommand.CanExecute(null));
        CompleteTest(vm);
        Assert.False(vm.CompactStagesCommand.CanExecute(null));
        vm.RemoveUnusedCesCommand.Execute(null);
        Assert.True(vm.CompactStagesCommand.CanExecute(null));
        vm.CompactStagesCommand.Execute(null);
        vm.FinishCompaction(); // Already compact is still a completed action.
        Assert.Equal(2, vm.SelectedNetwork.StageCount);
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
    [Fact]
    public void Compaction_transition_matches_duplicate_CEs_and_fades_new_prefix_entries()
    {
        var pairs = new[] { (4, 5), (0, 1), (4, 5), (2, 3) };
        var stages = pairs.Select(p => Sortopia.Sorting.Networks.stage.create(new[] {
            Sortopia.Sorting.Networks.ce.create(p.Item1, p.Item2) }, 6)).ToArray();
        var network = new NetworkDefinition("duplicates", Sortopia.Sorting.Networks.stageSequence.create(stages, 6));
        var compact = NetworkTransforms.Compact(network);
        var uses = new NetworkCheckResult(100, false, null, 0, Array.AsReadOnly(new[] { 9, 7, 2, 5 }));
        var remapped = NetworkTransforms.PreserveUsage(network, compact, uses, false);
        var input = Enumerable.Range(0, 6).Reverse().ToArray();
        var transition = new Sortopia.WPF.Rendering.CompactionTransition(NetworkTrace.CreateForPlayback(network, input), NetworkTrace.CreateForPlayback(compact, input), uses, remapped);
        Assert.Equal(new[] { 9, 7, 2, 5 }, transition.Moves.Select(m => m.Uses));
        Assert.All(transition.Moves, m => { Assert.Equal(m.FromX, m.X(0)); Assert.Equal(m.ToX, m.X(1)); Assert.Equal(1, m.Opacity(.5)); });
        Assert.Contains(transition.Moves, m => m.FromX != m.ToX);
        var longStages = Enumerable.Range(0, 600).Select(i => Sortopia.Sorting.Networks.stage.create(new[] {
            i == 599 ? Sortopia.Sorting.Networks.ce.create(2, 3) : Sortopia.Sorting.Networks.ce.create(0, 1) }, 4)).ToArray();
        var longNetwork = new NetworkDefinition("prefix", Sortopia.Sorting.Networks.stageSequence.create(longStages, 4));
        var prefix = new Sortopia.WPF.Rendering.CompactionTransition(
            NetworkTrace.CreateForPlayback(longNetwork, new[] { 3, 2, 1, 0 }),
            NetworkTrace.CreateForPlayback(NetworkTransforms.Compact(longNetwork), new[] { 3, 2, 1, 0 }), null, null);
        var entering = Assert.Single(prefix.Moves, m => !m.WasVisible);
        Assert.Equal(2, entering.Low);
        Assert.Equal(0, entering.Opacity(0));
        Assert.Equal(.5, entering.Opacity(.5));
        Assert.Equal(1, entering.Opacity(1));
        Assert.True(prefix.Moves.Count <= 2 * NetworkTrace.DisplayCeLimit);
    }

    [Fact]
    public void Animation_locks_controls_and_leaving_finishes_the_committed_compaction() => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        vm.SelectedNetwork = RedundantNetwork();
        CompleteTest(vm);
        vm.RemoveUnusedCesCommand.Execute(null);
        vm.PlayPauseCommand.Execute(null);
        vm.CompactStagesCommand.Execute(null);
        Assert.True(vm.IsCompacting);
        Assert.False(vm.IsPlaying);
        Assert.NotNull(vm.Compaction);
        Assert.False(vm.GenerateCommand.CanExecute(null));
        Assert.False(vm.TestCommand.CanExecute(null));
        Assert.False(vm.NewSeedCommand.CanExecute(null));
        Assert.False(vm.ShuffleCommand.CanExecute(null));
        Assert.False(vm.PlayPauseCommand.CanExecute(null));
        var compacted = vm.SelectedNetwork;
        vm.Leave();
        Assert.False(vm.IsCompacting);
        Assert.Null(vm.Compaction);
        Assert.Same(compacted, vm.SelectedNetwork);
        Assert.True(vm.GenerateCommand.CanExecute(null));
        Assert.False(vm.CompactStagesCommand.CanExecute(null));
    });

    [Fact]
    public void Compaction_frames_render_at_start_middle_and_end() => OnSta(() =>
    {
        var pairs = new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 7), (1, 6), (2, 5), (3, 4) };
        var stages = pairs.Select(p => Sortopia.Sorting.Networks.stage.create(new[] { Sortopia.Sorting.Networks.ce.create(p.Item1, p.Item2) }, 8)).ToArray();
        var network = new NetworkDefinition("render", Sortopia.Sorting.Networks.stageSequence.create(stages, 8));
        var compact = NetworkTransforms.Compact(network);
        var usage = NetworkCheck.Run(network, CancellationToken.None);
        var mapped = NetworkTransforms.PreserveUsage(network, compact, usage, false);
        var input = Enumerable.Range(0, 8).Reverse().ToArray();
        var before = NetworkTrace.CreateForPlayback(network, input);
        var after = NetworkTrace.CreateForPlayback(compact, input);
        var transition = new Sortopia.WPF.Rendering.CompactionTransition(before, after, usage, mapped);
        var diagram = new Sortopia.WPF.Controls.NetworkDiagram { Trace = after, Usage = mapped, Zoom = 1.5 };
        byte[]? last = null;
        foreach (var progress in new[] { 0d, .5, 1d })
        {
            var visual = new System.Windows.Media.DrawingVisual();
            using (var context = visual.RenderOpen()) diagram.RenderCompaction(context, transition, progress);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(700, 450, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[700 * 450 * 4];
            bitmap.CopyPixels(pixels, 700 * 4, 0);
            Assert.Contains(pixels, p => p != 0);
            if (last is not null) Assert.False(last.SequenceEqual(pixels));
            last = pixels;
            if (Environment.GetEnvironmentVariable("SORTOPIA_RENDER_OUTPUT") is { } output)
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(output, $"compaction-{progress:0.0}.png"));
                encoder.Save(stream);
            }
        }
    });
    [Fact]
    public void Compaction_clock_signals_completion() => OnSta(() =>
    {
        var network = RedundantNetwork();
        var compact = NetworkTransforms.Compact(network);
        var input = new[] { 3, 2, 1, 0 };
        var before = NetworkTrace.CreateForPlayback(network, input);
        var after = NetworkTrace.CreateForPlayback(compact, input);
        var diagram = new Sortopia.WPF.Controls.NetworkDiagram { Trace = after };
        var frame = new System.Windows.Threading.DispatcherFrame();
        var completed = false;
        var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timeout.Tick += (_, _) => frame.Continue = false;
        diagram.CompactionCompleted += (_, _) =>
        {
            completed = true;
            diagram.Compaction = null;
            frame.Continue = false;
        };
        diagram.Compaction = new(before, after, null, null);
        timeout.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timeout.Stop(); diagram.Compaction = null; }
        Assert.True(completed);
    });
    [Theory]
    [InlineData("msce", 8, 20)]
    [InlineData("mssi", 7, 3)]
    [InlineData("msrs", 10, 3)]
    [InlineData("msuf4", 16, 3)]
    [InlineData("msuf6", 24, 3)]
    public async Task Search_replays_family_mutations_and_keeps_the_best_score(string family, int width, int size)
    {
        var model = RandomNetwork.generateModel(family, width, size, Guid.Empty);
        var token = TestContext.Current.CancellationToken;
        var start = NetworkSearch.Prepare(family, model, 128, token);
        var mutator = RandomNetwork.createMutator(model, 0.25);
        async Task<SearchCandidate> Run()
        {
            var best = start;
            for (var generation = 1; generation <= 8; generation++)
            {
                var winner = await NetworkSearch.NextGenerationAsync(best, mutator, generation, 4, token);
                if (!ReferenceEquals(winner, best)) Assert.True(winner.Improves(best));
                best = winner;
            }
            return best;
        }
        var run = await Run();
        Assert.Equal(run.Network.Comparators, (await Run()).Network.Comparators);
        Assert.InRange(run.Usage.Failed, 0, start.Usage.Failed);
        Assert.Equal(128, run.Usage.Tested);
        Assert.False(run.Usage.Exhaustive);
        Assert.All(run.Usage.CeUses, count => Assert.True(count > 0));
        var checkedAgain = NetworkCheck.Run(run.Network, 128, token);
        Assert.Equal(checkedAgain.Failed, run.Usage.Failed);
        Assert.Equal(checkedAgain.CeUses, run.Usage.CeUses);
        Assert.Equal(run.Network.StageLengths, NetworkTransforms.Compact(run.Network).StageLengths);
    }

    [Fact]
    public void Selection_orders_failures_first_and_weights_a_stage_above_one_ce()
    {
        var model = RandomNetwork.generateModel("msce", 6, 5, Guid.Empty);
        SearchCandidate Candidate(int failures, params int[] lengths)
        {
            var stages = lengths.Select(length => Sortopia.Sorting.Networks.stage.create(
                Enumerable.Range(0, length).Select(i => Sortopia.Sorting.Networks.ce.create(2 * i, 2 * i + 1)).ToArray(), 6)).ToArray();
            var network = new NetworkDefinition("candidate", Sortopia.Sorting.Networks.stageSequence.create(stages, 6));
            return new(model, network, new NetworkCheckResult(64, true, null, failures, new int[lengths.Sum()]));
        }
        var shallow = Candidate(0, 3, 2);
        var narrow = Candidate(0, 2, 1, 1);
        Assert.True(shallow.Improves(narrow));
        Assert.False(narrow.Improves(shallow));
        Assert.False(shallow.Improves(shallow));
        Assert.False(Candidate(1, 1).Improves(shallow));
    }

    [Fact]
    public void Sgd_run_stop_resume_and_generate_publish_only_completed_generations() => OnSta(() =>
    {
        using var shell = new MainWindowViewModel(_ => throw new InvalidOperationException("Unexpected folder picker"));
        var home = Assert.IsType<HomePageViewModel>(shell.CurrentPage);
        home.OpenRandomNetworksCommand.Execute(null);
        var random = Assert.IsType<RandomNetworksPageViewModel>(shell.CurrentPage);
        var untouched = random.SelectedNetwork;
        random.ReturnHomeCommand.Execute(null);
        home.OpenSgdCommand.Execute(null);
        var vm = Assert.IsType<SgdPageViewModel>(shell.CurrentPage);
        vm.Generator.SamplesText = "invalid";
        Assert.False(vm.RunSgdCommand.CanExecute(null));
        Assert.False(vm.GenerateCommand.CanExecute(null));
        vm.Generator.SamplesText = "all";
        vm.GenerationMilliseconds = 500;
        Assert.Contains("500", vm.SpeedLabel);
        vm.GenerationMilliseconds = 0;
        Assert.Contains("quickly", vm.SpeedLabel);
        // A two-channel sorter is already correct. Evolution still counts generations and retains the tied parent.
        vm.Generator.ChannelCount = 2;
        vm.Generator.CountText = "8";
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Generation) && vm.Generation == 2) vm.StopCommand.Execute(null);
        };
        CompleteRun(vm.RunSgdAsync);
        Assert.Equal(2, vm.Generation);
        Assert.Equal(1, vm.Network!.ComparatorCount);
        Assert.Equal(1, vm.Network.StageCount);
        Assert.Equal(0, vm.Usage!.Failed);
        Assert.Equal("Best: Stages: 1, CE's: 1", vm.BestGraphTitle);
        Assert.Equal("Current: Stages: 1, CE's: 1", vm.CurrentGraphTitle);
        Assert.Equal("0 failures across all 4 binary inputs", vm.Summary);
        Assert.Equal(new[] { 1 }, vm.Usage.CeUses);
        Assert.Equal(1L + 2 * vm.Children, vm.Visits.Counts[new Point(1, 1)]);
        Assert.Equal(new Point(1, 1), vm.Visits.Current);
        var winner = vm.Network;
        vm.GenerationMilliseconds = 30;
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Generation) && vm.Generation == 3) vm.ReturnHomeCommand.Execute(null);
        };
        CompleteRun(vm.RunSgdAsync);
        Assert.Equal(3, vm.Generation);
        Assert.Same(winner, vm.Network);
        Assert.Single(vm.Visits.Counts);
        Assert.Equal(1L + 3 * vm.Children, vm.Visits.Maximum);
        Assert.Same(home, shell.CurrentPage);
        home.OpenSgdCommand.Execute(null);

        long visitsBeforeGenerate = 0;
        CompleteRun(async () =>
        {
            vm.GenerationMilliseconds = 500;
            var previousRun = vm.RunSgdAsync();
            Assert.False(vm.RunSgdCommand.CanExecute(null));
            Assert.True(vm.StopCommand.CanExecute(null));
            vm.Generator.ChannelCount = 7;
            vm.Generator.Method = "mssi";
            vm.Generator.SamplesText = "16";
            visitsBeforeGenerate = vm.Visits.Counts.Values.Sum();
            await vm.GenerateAsync();
            await previousRun;
        });
        Assert.Equal(0, vm.Generation);
        Assert.Equal(7, vm.Network!.Width);
        Assert.Equal(visitsBeforeGenerate + 1, vm.Visits.Counts.Values.Sum());
        Assert.Equal(new Point(vm.Network.ComparatorCount, vm.Network.StageCount), vm.Visits.Current);
        Assert.Equal(16, vm.Usage!.Tested);
        Assert.False(vm.Usage.Exhaustive);
        Assert.Equal("Best: Stages: 1, CE's: 1", vm.BestGraphTitle);
        Assert.Equal($"Current: Stages: {vm.Network.StageCount}, CE's: {vm.Network.ComparatorCount}", vm.CurrentGraphTitle);
        Assert.Equal($"{vm.Usage.Failed:N0} failures across 16 sampled binary inputs", vm.Summary);
        Assert.All(vm.Usage.CeUses, swaps => Assert.True(swaps > 0));
        Assert.True(vm.RunSgdCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));
        Assert.Same(untouched, random.SelectedNetwork);
    });

    [Fact]
    public void Sgd_records_every_evaluated_child_in_order_without_counting_the_winner_twice() => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        // Child two sorts all inputs. The next child plots at a smaller size but fails six inputs.
        vm.Generator.ChannelCount = 4;
        vm.Generator.CountText = "7";
        vm.Generator.SeedText = "15";
        var recipe = vm.Generator.Snapshot();
        var token = TestContext.Current.CancellationToken;
        var parent = NetworkSearch.Prepare(recipe.Label, recipe.CreateModel(), null, token);
        var mutator = RandomNetwork.createMutator(parent.Model, vm.MutationRate);
        var best = parent;
        var expected = new List<Point> { parent.Position };
        var expectedFailures = new List<int> { parent.Usage.Failed };
        string ExpectedTitle() => best.Usage.Failed == 0
            ? $"Best: Stages: {best.Network.StageCount}, CE's: {best.Network.ComparatorCount}"
            : "Best: No zero-failure sorter plotted";
        var expectedBest = new List<string> { ExpectedTitle() };
        for (var i = 0; i < vm.Children; i++)
        {
            var model = Sortopia.Models.Sorting.Simple.SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(mutator, parent.Model, i, 1);
            var child = NetworkSearch.Prepare(recipe.Label, model, null, token);
            expected.Add(child.Position);
            expectedFailures.Add(child.Usage.Failed);
            if (child.Improves(best)) best = child;
            expectedBest.Add(ExpectedTitle());
        }
        var recorded = new List<Point>();
        var recordedBest = new List<string>();
        var recordedSummaries = new List<string>();
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Visits))
            {
                recorded.Add(vm.Visits.Current!.Value);
                recordedBest.Add(vm.BestGraphTitle);
                recordedSummaries.Add(vm.Summary);
            }
            if (change.PropertyName == nameof(vm.Generation) && vm.Generation == 1) vm.Stop();
        };
        CompleteRun(vm.GenerateAsync);
        CompleteRun(vm.RunSgdAsync);
        Assert.Equal(new[] { 1, 1, 0, 6, 1 }, expectedFailures);
        Assert.Equal(expected, recorded);
        Assert.Equal(expectedBest, recordedBest);
        Assert.Equal(expectedFailures.Select(n => $"{n:N0} failures across all {parent.Usage.Tested:N0} binary inputs"), recordedSummaries);
        Assert.Equal(expected[^1], vm.Visits.Current);
        Assert.Equal($"Current: Stages: {expected[^1].Y:0}, CE's: {expected[^1].X:0}", vm.CurrentGraphTitle);
        foreach (var cell in expected.GroupBy(p => p)) Assert.Equal(cell.LongCount(), vm.Visits.Counts[cell.Key]);
        Assert.Equal(expected.Count, vm.Visits.Counts.Values.Sum());
        Assert.Equal(best.Network.Comparators, vm.Network!.Comparators);
    });

    [Fact]
    public void Graph_best_tracks_only_zero_failure_sorters_until_clear() => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Generator.ChannelCount = 4;
        vm.Generator.CountText = "1";
        CompleteRun(vm.GenerateAsync);
        Assert.True(vm.Usage!.Failed > 0);
        Assert.Equal("Best: No zero-failure sorter plotted", vm.BestGraphTitle);
        Assert.Null(vm.BestPosition);

        vm.Generator.CountText = "32";
        vm.Generator.SeedText = "1";
        CompleteRun(vm.GenerateAsync);
        Assert.Equal(0, vm.Usage.Failed);
        Assert.Equal(new Point(6, 5), vm.BestPosition);
        vm.Generator.SeedText = "3";
        CompleteRun(vm.GenerateAsync);
        Assert.Equal(0, vm.Usage.Failed);
        Assert.Equal(new Point(5, 3), vm.BestPosition);
        vm.Generator.CountText = "1";
        CompleteRun(vm.GenerateAsync);
        Assert.True(vm.Usage.Failed > 0);
        Assert.Equal(new Point(1, 1), vm.Visits.Current);
        Assert.Equal(2L, vm.Visits.Counts[new Point(1, 1)]);
        Assert.Equal("Best: Stages: 3, CE's: 5", vm.BestGraphTitle);
        Assert.Equal(new Point(5, 3), vm.BestPosition);
        vm.ClearGraphCommand.Execute(null);
        Assert.Equal("Best: No zero-failure sorter plotted", vm.BestGraphTitle);
        Assert.Null(vm.BestPosition);
        CompleteRun(vm.GenerateAsync);
        Assert.Null(vm.BestPosition);
        Assert.Single(vm.Visits.Counts);
    });

    [Fact]
    public void Best_known_rows_follow_the_channel_picker_and_match_each_catalog_variant() => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        var ids = SorterLib.ids();
        foreach (var width in new[] { 8, 17, 22, 32, 64, 2 })
        {
            vm.Generator.ChannelCount = width;
            Assert.Equal(ids.Where(id => id.ChannelCount == width), vm.BestKnownNetworks.Select(n => n.Id));
            foreach (var known in vm.BestKnownNetworks)
            {
                var stages = SorterDataParse.parseTo2dCeArray(SorterLib.tryGet(known.Id).Value);
                var ces = stages.Sum(stage => stage.Length);
                var name = SorterLibVariant.toString(known.Id.SorterVariant) switch
                {
                    "VariantA" => "A", "VariantB" => "B", "VariantC" => "C", var named => named
                };
                Assert.Equal($"Best Known {name}: Stages: {stages.Length}, CE's: {ces}", known.BestKnownTitle);
                Assert.Equal(new Point(ces, stages.Length), known.Position);
            }
            Assert.Empty(vm.Visits.Counts);
        }
    });

    [Fact]
    public void Clear_and_Stop_apply_between_evaluated_children() => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Generator.ChannelCount = 2;
        CompleteRun(vm.GenerateAsync);
        var original = vm.Network;
        var childrenSeen = 0;
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(vm.Visits) || !vm.Visits.Current.HasValue) return;
            childrenSeen++;
            if (childrenSeen == 1) vm.ClearGraphCommand.Execute(null);
            if (childrenSeen == 3) vm.Stop();
        };
        CompleteRun(vm.RunSgdAsync);
        Assert.Equal(3, childrenSeen);
        Assert.Equal(0, vm.Generation);
        Assert.Same(original, vm.Network);
        Assert.Equal(new Point(1, 1), vm.Visits.Current);
        Assert.Equal(2, vm.Visits.Maximum);
        Assert.Equal(2, vm.Visits.Counts.Values.Sum());
    });

    [Fact]
    public void Seed_sampling_stops_from_Generate_and_keeps_every_draw_until_Clear() => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Generator.ChannelCount = 2;
        vm.Generator.CountText = "8";
        vm.NewSeedCommand.Execute(null);
        var labels = new List<string>();
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.GenerateLabel)) labels.Add(vm.GenerateLabel);
            if (change.PropertyName == nameof(vm.Network) && vm.GeneratedCount == 3)
                vm.GenerateCommand.Execute(null);
        };
        CompleteRun(() => vm.GenerateAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { "0", "1", "2", "3", "Generate" }, labels);
        Assert.Equal(0, vm.GeneratedCount);
        Assert.Equal("Generate", vm.GenerateLabel);
        Assert.Single(vm.Visits.Counts);
        Assert.Equal(3, vm.Visits.Counts[new Point(1, 1)]);
        CompleteRun(vm.GenerateAsync);
        Assert.Equal(4, vm.Visits.Maximum);
        labels.Clear();
        vm.NewSeedCommand.Execute(null);
        CompleteRun(() => vm.GenerateAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { "0", "1", "2", "3", "Generate" }, labels);
        Assert.Equal(7, vm.Visits.Maximum);
        Assert.Equal(0, vm.GeneratedCount);
        var current = vm.Network;
        vm.ClearGraphCommand.Execute(null);
        Assert.Empty(vm.Visits.Counts);
        Assert.Null(vm.Visits.Current);
        Assert.Equal(0, vm.Visits.Maximum);
        Assert.Same(current, vm.Network);
        Assert.True(vm.RunSgdCommand.CanExecute(null));
    });

    [Fact]
    public void Run_SGD_takes_over_the_last_completed_random_seed() => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        NetworkRecipe? seed = null;
        Point? lastDraw = null;
        Task? evolution = null;
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Network) && vm.GeneratedCount == 3)
            {
                seed = vm.Generator.Snapshot();
                lastDraw = vm.Visits.Current;
                evolution = vm.RunSgdAsync();
            }
            if (change.PropertyName == nameof(vm.Generation) && vm.Generation == 12) vm.Stop();
        };
        vm.NewSeedCommand.Execute(null);
        CompleteRun(async () =>
        {
            await vm.GenerateAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(evolution);
            await evolution.WaitAsync(TimeSpan.FromSeconds(10));
        });
        Assert.NotNull(seed);
        var token = TestContext.Current.CancellationToken;
        var parent = NetworkSearch.Prepare(seed.Label, seed.CreateModel(), null, token);
        var mutator = RandomNetwork.createMutator(parent.Model, vm.MutationRate);
        Assert.Equal(parent.Position, lastDraw);
        CompleteRun(async () =>
        {
            for (var g = 1; g <= 12; g++)
                parent = await NetworkSearch.NextGenerationAsync(parent, mutator, g, vm.Children, token);
        });
        Assert.Equal(parent.Network.Comparators, vm.Network!.Comparators);
        Assert.Equal(3L + 12 * vm.Children, vm.Visits.Counts.Values.Sum());
        Assert.Equal(0, vm.GeneratedCount);
        Assert.Equal(12, vm.Generation);
    });

    [Theory]
    [InlineData(400, 176, 60, 12)]
    [InlineData(1000, 176, 20, 55)]
    [InlineData(400, 176, 1_000_000, 12)]
    public void Evolution_plot_renders_unconnected_counts_on_an_equal_scale_lattice(int width, int height, int farX, int farY) => OnSta(() =>
    {
        var plot = new EvolutionPlot { Visits = EvolutionVisits.Empty
            .Record(new(17, 10)).Record(new(18, 10)).Record(new(17, 11))
            .Record(new(17, 10)).Record(new(17, 11)).Record(new(17, 10)).Record(new(farX, farY)) };
        plot.Measure(new Size(width, height));
        plot.Arrange(new Rect(0, 0, width, height));
        plot.UpdateLayout();
        static IEnumerable<GeometryDrawing> Shapes(Drawing drawing)
        {
            if (drawing is GeometryDrawing shape) yield return shape;
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var item in Shapes(child)) yield return item;
        }
        var shapes = Shapes(VisualTreeHelper.GetDrawing(plot)).ToArray();
        var cells = shapes.Where(s => s.Geometry is RectangleGeometry).ToArray();
        Assert.Equal(4, cells.Length);
        Point Center(Color fill)
        {
            var rect = Assert.Single(cells, s => (s.Brush as SolidColorBrush)?.Color == fill).Geometry.Bounds;
            return new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }
        var start = Center(Colors.Red); // Three visits.
        var right = Center(Colors.Blue); // One visit.
        var up = Center(Color.FromRgb(135, 255, 0)); // log(2) / log(3), between green and yellow.
        var far = Center(Colors.HotPink);
        var unit = right.X - start.X;
        Assert.All(cells, cell =>
        {
            Assert.Equal(unit, cell.Geometry.Bounds.Width, 8);
            Assert.Equal(unit, cell.Geometry.Bounds.Height, 8);
        });
        Assert.True(unit > 0);
        Assert.Equal(unit, start.Y - up.Y, 8);
        Assert.Equal(start.X, up.X, 8);
        Assert.Equal(start.Y, right.Y, 8);
        Assert.InRange(far.X, 0, width);
        Assert.InRange(far.Y, 0, height);

        var markers = new HashSet<Point> { start, right, up, far };
        var lines = shapes.Select(s => s.Geometry).OfType<LineGeometry>().ToArray();
        Assert.DoesNotContain(lines,
            line => markers.Contains(line.StartPoint) && markers.Contains(line.EndPoint));
        var xAxis = lines.Where(line => line.StartPoint.Y == line.EndPoint.Y)
            .MaxBy(line => line.EndPoint.X - line.StartPoint.X)!;
        var yAxis = Assert.Single(lines, line => line.EndPoint == xAxis.StartPoint && line.StartPoint.Y < line.EndPoint.Y);
        Assert.Equal(width / 2d, (xAxis.StartPoint.X + xAxis.EndPoint.X) / 2, 6);
        Assert.Equal(5, (markers.Min(p => p.X) - xAxis.StartPoint.X) / unit, 5);
        Assert.Equal(5, (xAxis.EndPoint.X - markers.Max(p => p.X)) / unit, 5);
        Assert.Equal(5, (markers.Min(p => p.Y) - yAxis.StartPoint.Y) / unit, 5);
        Assert.Equal(5, (yAxis.EndPoint.Y - markers.Max(p => p.Y)) / unit, 5);
        var lattice = shapes.Select(s => s.Geometry).OfType<EllipseGeometry>().Where(dot => dot.RadiusX == 1).ToArray();
        Assert.NotEmpty(lattice);
        foreach (var dot in lattice)
        {
            var dx = (dot.Center.X - start.X) / unit;
            var dy = (dot.Center.Y - start.Y) / unit;
            Assert.InRange(Math.Abs(dx - Math.Round(dx)), 0, 0.001);
            Assert.InRange(Math.Abs(dy - Math.Round(dy)), 0, 0.001);
        }

        // Three visits are halfway between one and nine on the log scale.
        var visits = plot.Visits;
        for (var i = 0; i < 8; i++) visits = visits.Record(new(farX, farY));
        plot.Visits = visits.Record(new(17, 11));
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        Assert.Equal(start, Center(Colors.Lime));
        Assert.Equal(far, Center(Colors.Red));
        Assert.Equal(up, Center(Colors.HotPink));

        plot.BestPosition = new Point(17, 10);
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        Assert.Equal(start, Center(Colors.LimeGreen));
        Assert.Equal(up, Center(Colors.HotPink));

        plot.BestPosition = plot.Visits.Current;
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        Assert.Equal(up, Center(Colors.HotPink));
        Assert.Equal(start, Center(Colors.Lime));
        var outline = Assert.Single(cells, s => s.Brush is null && (s.Pen?.Brush as SolidColorBrush)?.Color == Colors.LimeGreen);
        var rect = outline.Geometry.Bounds;
        Assert.Equal(up.X, rect.X + rect.Width / 2, 8);
        Assert.Equal(up.Y, rect.Y + rect.Height / 2, 8);
        Assert.Equal(unit, rect.Width + outline.Pen.Thickness, 8);

        // The cold end follows the observed minimum, including when every cell has been revisited.
        plot.BestPosition = null;
        plot.Visits = plot.Visits.Record(new(18, 10)).Record(new(17, 11));
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        Assert.Equal(right, Center(Colors.Blue)); // Two visits, now the minimum.
        Assert.Equal(far, Center(Colors.Red));

        var uniform = EvolutionVisits.Empty;
        foreach (var point in new Point[] { new(17, 10), new(18, 10), new(17, 11), new(farX, farY) })
            uniform = uniform.Record(point).Record(point);
        plot.Visits = uniform;
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        Assert.Equal(3, cells.Count(c => (c.Brush as SolidColorBrush)?.Color == Colors.Blue));
        Assert.Equal(far, Center(Colors.HotPink));
        Assert.Equal(new[] { Colors.Blue, Colors.Cyan, Colors.Lime, Colors.Yellow, Colors.Red },
            EvolutionPlot.FrequencyLegend.GradientStops.Select(stop => stop.Color));
    });

    private static void CompleteTest(RandomNetworksPageViewModel vm) => CompleteRun(vm.RunTestAsync);

    [Theory]
    [InlineData(4, null)]
    [InlineData(10, null)]
    [InlineData(8, 1)]
    [InlineData(17, 513)]
    [InlineData(64, 1025)]
    public void Bit512_evaluation_matches_ordered_inputs_and_swap_counts(int width, int? samples)
    {
        var token = TestContext.Current.CancellationToken;
        var simd = new SimdNetworkCheck(width, samples, token);
        var network = new NetworkDefinition("comparison", RandomNetwork.generate("msce", width, 12, Guid.Empty));
        var expected = NetworkCheck.Run(network, samples, token);
        var actual = simd.Run(network, token);
        Assert.Equal(expected.Tested, actual.Tested);
        Assert.Equal(expected.Exhaustive, actual.Exhaustive);
        Assert.Equal(expected.Failed, actual.Failed);
        Assert.Equal(expected.Counterexample, actual.Counterexample);
        Assert.Equal(expected.CeUses.ToArray(), actual.CeUses.ToArray());
        Assert.Throws<OperationCanceledException>(() => simd.Run(network, new CancellationToken(true)));
    }

    [Fact]
    public void Sgd_channel_picker_and_evaluator_control_apply_to_the_next_run() => OnSta(() =>
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Sortopia.WPF;component/Styles/Navigation.xaml", UriKind.Relative)
            });
            CheckFirstGenerateFromHome(deferSelection: false);
            CheckFirstGenerateFromHome(deferSelection: true);
            CheckSgdControls();
        }
        finally { app.Shutdown(); }
    });

    private static void CheckFirstGenerateFromHome(bool deferSelection)
    {
        var window = new MainWindow();
        try
        {
            var shell = Assert.IsType<MainWindowViewModel>(window.DataContext);
            var content = Assert.IsType<ContentControl>(window.Content);
            void Layout()
            {
                content.Measure(new Size(1000, 740));
                content.Arrange(new Rect(0, 0, 1000, 740));
                content.UpdateLayout();
            }
            Layout();
            Assert.IsType<HomePageViewModel>(shell.CurrentPage).OpenSgdCommand.Execute(null);
            Layout();
            var presenter = Assert.IsType<ContentPresenter>(VisualTreeHelper.GetChild(content, 0));
            var page = Assert.IsType<Sortopia.WPF.Views.SgdPage>(VisualTreeHelper.GetChild(presenter, 0));
            var vm = Assert.IsType<SgdPageViewModel>(page.DataContext);
            var channels = (ComboBox)LogicalTreeHelper.FindLogicalNode(page, "ChannelPicker");
            var generate = (Button)LogicalTreeHelper.FindLogicalNode(page, "GenerateButton");
            if (deferSelection)
            {
                // Keep the source at its initial eight channels until Generate receives the displayed choice.
                channels.SetBinding(ComboBox.SelectedItemProperty, new Binding("Generator.ChannelCount")
                    { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
            }
            channels.SetCurrentValue(ComboBox.SelectedItemProperty, 16);
            Assert.Equal(16, channels.SelectedItem);
            Assert.Equal(deferSelection ? 8 : 16, vm.Generator.ChannelCount);
            CompleteRun(async () =>
            {
                var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Network)) published.TrySetResult(); };
                var peer = new ButtonAutomationPeer(generate);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
            });
            Assert.Equal(16, vm.Generator.ChannelCount);
            Assert.Equal(16, vm.Network!.Width);
            Assert.True(vm.Usage!.Exhaustive);
            Assert.Equal(65_536, vm.Usage.Tested);
            Assert.StartsWith("16 channels", ((TextBlock)page.FindName("UsageLegendTitle")).Text);
        }
        finally { window.Close(); }
    }

    private static void CheckSgdControls()
    {
        var vm = new SgdPageViewModel(() => { });
        var host = new Sortopia.WPF.Views.SgdPage { DataContext = vm };
        host.Measure(new Size(1000, 800));
        host.Arrange(new Rect(0, 0, 1000, 800));
        var channels = (ComboBox)LogicalTreeHelper.FindLogicalNode(host, "ChannelPicker");
        var methods = (ComboBox)LogicalTreeHelper.FindLogicalNode(host, "MethodPicker");
        var avx = (CheckBox)LogicalTreeHelper.FindLogicalNode(host, "Avx512Picker");
        Assert.False(avx.IsChecked);
        Assert.Equal(System.Runtime.Intrinsics.X86.Avx512F.IsSupported, avx.IsEnabled);
        CompleteRun(vm.GenerateAsync);
        Assert.Equal(8, vm.Network!.Width);
        if (avx.IsEnabled) avx.SetCurrentValue(CheckBox.IsCheckedProperty, true);
        Assert.Equal(avx.IsEnabled, vm.UseAvx512);
        methods.SetCurrentValue(ComboBox.SelectedItemProperty, "msuf4");
        Assert.Equal(8, channels.SelectedItem);
        channels.SetCurrentValue(ComboBox.SelectedItemProperty, 16);
        Assert.Equal(16, vm.Generator.ChannelCount);
        methods.SetCurrentValue(ComboBox.SelectedItemProperty, "msuf6");
        Assert.Equal(12, channels.SelectedItem);
        channels.SetCurrentValue(ComboBox.SelectedItemProperty, 24);
        Assert.Equal(24, vm.Generator.ChannelCount);
        Assert.False(vm.Generator.CanTestAll);
        Assert.Equal("4096", vm.Generator.SamplesText);
        vm.Generator.SamplesText = "513";
        vm.Children = 1;
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Generation) && vm.Generation == 1) vm.Stop();
        };
        CompleteRun(() => vm.RunSgdAsync().WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(24, vm.Network.Width);
        Assert.Equal(513, vm.Usage!.Tested);
        Assert.Equal(1, vm.Generation);
        Assert.Equal(3L, vm.Visits.Counts.Values.Sum());
    }

    private static void CompleteRun(Func<Task> run)
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
        try
        {
            var task = run();
            var frame = new System.Windows.Threading.DispatcherFrame();
            task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
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
