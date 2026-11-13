using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Sortopia.Models.Sorting;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels;
using Sortopia.WPF.ViewModels.Pages.Home;
using Sortopia.WPF.ViewModels.Pages.Networks;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.ViewModels.Pages.RandomNetworks;
using Sortopia.WPF.ViewModels.Pages.Sgd;
using Sortopia.WPF.ViewModels.Shared.Controls;
using Sortopia.WPF.Views;
using Sortopia.WPF.Views.Shared.Controls;
using Xunit;
using parameterValueType = Sortopia.Runs.parameterValueType;

namespace Sortopia.WPF.Tests;

public sealed class RandomNetworkTests
{
    [Fact]
    public async Task A_replaced_SGD_operation_cannot_finish_or_cancel_the_new_run()
    {
        var operation = new SgdOperation();
        var finishOld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishNew = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default, newToken = default;
        var oldRun = operation.RunAsync(SgdActivity.Sample, async token => { oldToken = token; await finishOld.Task; });
        var newRun = operation.RunAsync(SgdActivity.Evolve, async token => { newToken = token; await finishNew.Task; });
        Assert.True(oldToken.IsCancellationRequested);
        Assert.False(operation.IsCurrent(oldToken));
        finishOld.SetResult();
        await oldRun;
        Assert.Equal(SgdActivity.Evolve, operation.Activity);
        Assert.True(operation.IsCurrent(newToken));
        Assert.True(operation.CanStop);
        operation.Stop();
        Assert.True(newToken.IsCancellationRequested);
        finishNew.SetResult();
        await newRun;
        Assert.True(operation.IsIdle);
        Assert.False(operation.CanStop);
    }

    [Fact]
    public void Sgd_pages_keep_independent_settings_histories_and_the_explicit_parent() => OnSta(() =>
    {
        var workflow = new SgdPageViewModel(() => { });
        var setup = workflow.Setup;
        var sampling = workflow.Sampling;
        var evolution = workflow.Evolution;
        Assert.Same(setup, workflow.CurrentPage);
        setup.Generator.ChannelCount = 4;
        setup.Generator.CountText = "4";
        setup.ContinueCommand.Execute(null);
        Assert.Same(sampling, workflow.CurrentPage);
        CompleteRun(sampling.GenerateAsync);
        var selected = sampling.NetworkHistory.Current!;
        sampling.DisplayMode = 1;
        sampling.ShowVisitStats = true;
        sampling.EvolveSelectionCommand.Execute(null);
        Assert.Same(evolution, workflow.CurrentPage);
        Assert.Same(selected.Sample, evolution.Parent);
        Assert.Same(selected.Sample!.Model, evolution.Parent!.Model);
        Assert.Same(selected.Usage, evolution.Usage);
        Assert.NotSame(sampling.NetworkHistory, evolution.NetworkHistory);
        Assert.NotSame(sampling.FirstGroup, evolution.FirstGroup);

        evolution.DisplayMode = 2;
        evolution.Mutation.OverallRate.Value = 0.25;
        evolution.Children = 1;
        evolution.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(evolution.Generation) && evolution.Generation == 1) workflow.Stop();
        };
        CompleteRun(evolution.RunSgdAsync);
        Assert.Equal(1, evolution.Generation);
        var parent = evolution.Parent;
        var history = evolution.NetworkHistory.Entries.ToArray();
        var visits = evolution.Visits;

        workflow.SamplingStepCommand.Execute(null);
        Assert.Same(sampling, workflow.CurrentPage);
        Assert.Same(selected, sampling.NetworkHistory.Current);
        Assert.Equal(1, sampling.DisplayMode);
        Assert.True(sampling.ShowVisitStats);
        sampling.SampleToB = true;
        CompleteRun(sampling.GenerateAsync);
        Assert.Same(parent, evolution.Parent);
        Assert.Equal(history, evolution.NetworkHistory.Entries);
        Assert.Same(visits, evolution.Visits);

        workflow.SetupStepCommand.Execute(null);
        Assert.Same(setup, workflow.CurrentPage);
        Assert.Equal(4, setup.Generator.ChannelCount);
        workflow.EvolutionStepCommand.Execute(null);
        Assert.Same(evolution, workflow.CurrentPage);
        Assert.Same(parent, evolution.Parent);
        Assert.Equal(1, evolution.Generation);
        Assert.Equal(2, evolution.DisplayMode);
        Assert.False(evolution.ShowVisitStats);
        Assert.Equal(0.25, evolution.Mutation.OverallRate.Value);
    });

    [Fact]
    public void Invalid_setup_blocks_navigation_without_erasing_the_sample() => OnSta(() =>
    {
        var workflow = new SgdPageViewModel(() => { });
        workflow.Setup.Generator.ChannelCount = 2;
        workflow.Setup.ContinueCommand.Execute(null);
        CompleteRun(workflow.Sampling.GenerateAsync);
        var sample = workflow.Sampling.NetworkHistory.Current;
        workflow.SetupStepCommand.Execute(null);
        workflow.Setup.Generator.SamplesText = "invalid";
        Assert.False(workflow.Setup.ContinueCommand.CanExecute(null));
        Assert.False(workflow.SamplingStepCommand.CanExecute(null));
        Assert.False(workflow.EvolutionStepCommand.CanExecute(null));
        Assert.False(workflow.Sampling.GenerateCommand.CanExecute(null));
        workflow.SamplingStepCommand.Execute(null);
        Assert.Same(workflow.Setup, workflow.CurrentPage);
        Assert.Same(sample, workflow.Sampling.NetworkHistory.Current);
        workflow.Setup.Generator.SamplesText = "all";
        workflow.Setup.ContinueCommand.Execute(null);
        Assert.Same(workflow.Sampling, workflow.CurrentPage);
        Assert.Same(sample, workflow.Sampling.NetworkHistory.Current);
    });

    [Fact]
    public void Setup_refreshes_the_editable_stage_default_when_the_sorting_problem_changes()
    {
        var settings = new SgdPageViewModel(() => { }).Setup.Generator;
        Assert.Equal("40", settings.CountText);
        settings.Method = "mssi";
        Assert.Equal("49", settings.CountText);
        settings.ChannelCount = 10;
        Assert.Equal("62", settings.CountText);
        Assert.Contains("estimate", settings.StageDefaultDescription);
        settings.CountText = "7";
        settings.SeedText = "123";
        Assert.Equal(7, settings.Snapshot().Size);
        settings.SamplesText = "";
        Assert.Equal("7", settings.CountText);
        settings.SamplesText = "32";
        Assert.Equal("62", settings.CountText);
        settings.CountText = "9";
        settings.SamplesText = "all";
        Assert.Equal("62", settings.CountText);
        settings.ChannelCount = 24;
        Assert.Equal("4096", settings.SamplesText);
        Assert.Equal("215", settings.CountText);
        Assert.Contains("measured", settings.StageDefaultDescription);
    }

    [Fact]
    public void Generation_step_uses_distinct_seed_and_unfolding_distributions()
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Setup.Generator.Method = "msuf6";
        vm.Setup.Generator.ChannelCount = 24;
        vm.Setup.Generator.CountText = "5";
        var groups = vm.Sampling.GenerationOptions.Distributions;
        Assert.Equal(new[] { 7, 3, 3 }, groups.Select(g => g.Weights.Length));
        var tables = vm.Sampling.GenerationOptions.WeightTables;
        Assert.Equal(new[] { "Seed" }, tables[0].RowHeaders);
        Assert.Equal(new[] { "Unfold 6 → 12", "Unfold 12 → 24" }, tables[1].RowHeaders);
        Assert.Equal(new[] { "Ortho", "Para", "SR" }, tables[1].ColumnHeaders);
        foreach (var cell in tables.SelectMany(table => table.Cells)) cell.SliderValue = 0;
        Assert.False(vm.Sampling.GenerateCommand.CanExecute(null));
        tables[0].Cells[5].SliderValue = 1; // Para 4 seed
        tables[1].Cells[1].SliderValue = 0.5; // First unfolding is entirely para
        tables[1].Cells[3].SliderValue = 1; // Second unfolding is entirely ortho
        Assert.True(vm.Sampling.GenerateCommand.CanExecute(null));
        var recipe = vm.Setup.Generator.Snapshot();
        var model = Assert.IsType<Sortopia.Models.Sorting.Simple.simpleSorterModel.Msuf6>(recipe.CreateModel());
        Assert.Equal(5, model.Item.TwoOrbitUnfolder6s.Length);
        Assert.All(model.Item.TwoOrbitUnfolder6s, stage =>
        {
            Assert.True(stage.Seed6TwoOrbitType.IsPara4);
            Assert.All(stage.TwoOrbitUnfolderSteps[0].TwoOrbitPairTypes, pair => Assert.True(pair.IsPara));
            Assert.All(stage.TwoOrbitUnfolderSteps[1].TwoOrbitPairTypes, pair => Assert.True(pair.IsOrtho));
        });
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(8, 2)]
    public void Small_UF4_widths_generate_and_evolve_with_the_available_unfolding_steps(int width, int distributions)
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Setup.Generator.Method = "msuf4";
        vm.Setup.Generator.ChannelCount = width;
        Assert.Equal(distributions, vm.Sampling.GenerationOptions.Distributions.Count);
        var model = vm.Setup.Generator.Snapshot().CreateModel();
        var child = Sortopia.Models.Sorting.Simple.SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(
            vm.Evolution.Mutation.Create(model), model, 0, 1);
        Assert.Equal(width, RandomNetwork.toStagesWithPrefix(child).ChannelCount);
    }

    [Theory]
    [InlineData("msce", 4, 1)]
    [InlineData("mssi", 3, 2)]
    [InlineData("msrs", 4, 1)]
    [InlineData("msuf4", 1, 2)]
    [InlineData("msuf6", 1, 1)]
    public void Mutation_controls_match_family_parameters(string method, int count, int variants)
    {
        var settings = new SorterMutationSettings();
        settings.SelectMethod(method);
        Assert.Equal(count, settings.Parameters.Count);
        Assert.Equal(variants, settings.Variants.Length);
        Assert.Equal(method == "msce", settings.CanExcludeSelf);
        Assert.All(settings.Parameters, parameter => Assert.InRange(parameter.Value, 0.001, 1));
    }

    [Fact]
    public void Mutation_slider_spans_three_equal_decades()
    {
        var parameter = new MutationParameter("Rate", 0.1);
        Assert.Equal(-1, parameter.LogValue);
        parameter.Value = 0;
        Assert.Equal(0.001, parameter.Value);
        Assert.Equal(-3, parameter.LogValue);
        parameter.LogValue = -2;
        Assert.Equal(0.01, parameter.Value);
        parameter.LogValue = -1.5;
        Assert.Equal(Math.Sqrt(0.001), parameter.Value, 12);
        parameter.LogValue = 0;
        Assert.Equal(1, parameter.Value);
    }

    [Fact]
    public void Custom_ce_mutation_uses_native_insertion_parameters()
    {
        var model = RandomNetwork.generateModel("msce", 8, 12, Guid.NewGuid());
        var settings = new SorterMutationSettings();
        settings.SelectMethod("msce");
        settings.Parameters[0].Value = 1;
        settings.Parameters[1].LogValue = -2;
        settings.Parameters[2].LogValue = -1;
        var mutator = settings.Create(model);
        var expected = Sortopia.Models.Sorting.Simple.SimpleSorterModelMutator.getMsceModelMutator(
            Sortopia.Core.rngFactory.SmxFactory, true, 1, 0.01, 0.1, 0.001, Sortopia.Models.Sorting.mutatorVariant.V1);
        Assert.Equal(expected, mutator);
        var mutant = Sortopia.Models.Sorting.Simple.SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(mutator, model, 0, 1);
        var replay = Sortopia.Models.Sorting.Simple.SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(expected, model, 0, 1);
        Assert.Equal(new NetworkDefinition("expected", RandomNetwork.toStagesWithPrefix(replay)).Comparators,
            new NetworkDefinition("child", RandomNetwork.toStagesWithPrefix(mutant)).Comparators);
    }

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
    public void Generate_uses_fresh_replayable_seeds_and_generator_variety(string method, int width) => OnSta(() =>
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        vm.Method = method;
        vm.ChannelCount = width;
        vm.CountText = "32";
        vm.GenerateCommand.Execute(null);
        var first = vm.SelectedNetwork.Comparators.ToArray();
        var seed = vm.SeedText;
        var replay = new NetworkDefinition("replay", RandomNetwork.toStagesWithPrefix(vm.Generator.Snapshot().CreateModel()));
        Assert.Equal(first, replay.Comparators);
        Assert.Contains($"seed {seed}", vm.GenerationLabel);
        vm.GenerateCommand.Execute(null);
        Assert.NotEqual(seed, vm.SeedText);
        Assert.False(first.SequenceEqual(vm.SelectedNetwork.Comparators));
        if (method != "msce")
        {
            var stages = vm.SelectedNetwork.Sequence.Stages.Select(s => string.Join(";", s.Ces.Select(c => $"{c.LowChannel},{c.HighChannel}")));
            Assert.True(stages.Distinct().Count() > 3);
        }
        foreach (var invalid in new[] { "", "-1", "2147483648", "1.5", "abc" })
        {
            vm.SeedText = invalid;
            Assert.True(vm.GenerateCommand.CanExecute(null));
            vm.GenerateCommand.Execute(null);
            Assert.True(int.TryParse(vm.SeedText, out var fresh) && fresh >= 0);
        }
        vm.NewSeedCommand.Execute(null);
        Assert.True(vm.GenerateCommand.CanExecute(null));
    });
    [Theory]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    public void Exhaustive_testing_reaches_20_channels_and_rejects_21(int width)
    {
        var network = new NetworkDefinition("exhaustive", RandomNetwork.generate("msce", width, 1, Guid.Empty));
        var result = NetworkCheck.Run(network, CancellationToken.None);
        Assert.True(result.Exhaustive);
        Assert.Equal(1 << width, result.Tested);
        Assert.True(result.Failed > 0);
        Assert.Equal(1 << (width - 2), result.CeUses.Single());
        var large = new NetworkDefinition("21", RandomNetwork.generate("msce", 21, 1, Guid.Empty));
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
        Assert.True(vm.CanTestAll);
        Assert.Equal("all", vm.SamplesText);
        Assert.True(vm.TestCommand.CanExecute(null));
        vm.ChannelCount = 21;
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
        vm.ChannelCount = 20;
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
        Assert.Equal(level, Sortopia.WPF.Views.Shared.Controls.NetworkDiagram.UsageLevel(uses, 1000000));
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
        var diagram = new Sortopia.WPF.Views.Shared.Controls.NetworkDiagram { Trace = after, Usage = mapped, Zoom = 1.5 };
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
    public void Switching_display_modes_releases_the_old_width_when_unloaded() => OnSta(() =>
    {
        var network = new NetworkDefinition("long", RandomNetwork.generate("msce", 2, 60, Guid.Empty));
        var usage = NetworkCheck.Run(network, CancellationToken.None);
        var diagram = new NetworkDiagram { StaticNetwork = network, Usage = usage };
        var unconstrained = new Size(double.PositiveInfinity, double.PositiveInfinity);
        diagram.Measure(unconstrained);
        var fullWidth = diagram.DesiredSize.Width;
        diagram.DisplayMode = 3;
        diagram.Measure(unconstrained);
        var compactWidth = diagram.DesiredSize.Width;
        Assert.True(compactWidth < fullWidth);

        using var host = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Network display test")
            { Width = 320, Height = 160, WindowStyle = 0 });
        var panel = new Canvas();
        panel.Children.Add(diagram);
        host.RootVisual = panel;
        void FlushLayout() => CompleteRun(async () =>
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle));
        FlushLayout();
        Assert.True(diagram.IsLoaded);
        diagram.DisplayMode = 0;
        diagram.DisplayMode = 3;
        diagram.Measure(unconstrained);
        Assert.Equal(SystemParameters.ClientAreaAnimation ? fullWidth : compactWidth, diagram.DesiredSize.Width);
        var unloaded = new TaskCompletionSource();
        diagram.Unloaded += (_, _) => unloaded.TrySetResult();
        panel.Children.Remove(diagram);
        CompleteRun(() => unloaded.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(diagram.IsLoaded);
        panel.Children.Add(diagram);
        FlushLayout();
        Assert.True(diagram.IsLoaded);
        diagram.Measure(unconstrained);
        Assert.Equal(compactWidth, diagram.DesiredSize.Width);
    });

    [Fact]
    public void Compaction_clock_signals_completion() => OnSta(() =>
    {
        var network = RedundantNetwork();
        var compact = NetworkTransforms.Compact(network);
        var input = new[] { 3, 2, 1, 0 };
        var before = NetworkTrace.CreateForPlayback(network, input);
        var after = NetworkTrace.CreateForPlayback(compact, input);
        var diagram = new Sortopia.WPF.Views.Shared.Controls.NetworkDiagram { Trace = after };
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
        vm.Setup.Generator.SamplesText = "invalid";
        Assert.False(vm.Evolution.RunSgdCommand.CanExecute(null));
        Assert.False(vm.Sampling.GenerateCommand.CanExecute(null));
        vm.Setup.Generator.SamplesText = "all";
        vm.Evolution.GenerationMilliseconds = 500;
        Assert.Contains("500", vm.Evolution.SpeedLabel);
        vm.Evolution.GenerationMilliseconds = 0;
        Assert.Contains("quickly", vm.Evolution.SpeedLabel);
        // A two-channel sorter is already correct. Evolution still counts generations and retains the tied parent.
        vm.Setup.Generator.ChannelCount = 2;
        vm.Setup.Generator.CountText = "8";
        Assert.False(vm.Evolution.RunSgdCommand.CanExecute(null));
        CompleteRun(vm.Sampling.GenerateAsync);
        vm.Evolution.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Evolution.Generation) && vm.Evolution.Generation == 2) vm.Stop();
        };
        CompleteRun(() => Evolve(vm));
        Assert.Equal(2, vm.Evolution.Generation);
        Assert.Equal(8, Results(vm).Network!.StageCount);
        Assert.Equal(Results(vm).Network!.ComparatorCount, CurrentUsage(vm)!.CeUses.Count);
        Assert.Equal(0, CurrentUsage(vm)!.Failed);
        Assert.Equal("Best: [1, 1]", Results(vm).BestAnnotation.Label);
        Assert.Equal("Current: [1, 1]", Results(vm).CurrentAnnotation.Label);
        Assert.Equal("0 failures across all 4 binary inputs", Results(vm).Summary);
        Assert.Equal(1, CurrentUsage(vm)!.CeUses.Sum());
        Assert.Equal(1L + 2 * (1 + vm.Evolution.Children), Results(vm).Visits.Counts[new Point(1, 1)]);
        Assert.Equal(new Point(1, 1), Results(vm).Visits.Current);
        var winner = Results(vm).Network;
        vm.Evolution.GenerationMilliseconds = 30;
        vm.Evolution.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Evolution.Generation) && vm.Evolution.Generation == 3) vm.ReturnHomeCommand.Execute(null);
        };
        CompleteRun(() => Evolve(vm));
        Assert.Equal(3, vm.Evolution.Generation);
        Assert.Same(winner, Results(vm).Network);
        Assert.Single(Results(vm).Visits.Counts);
        Assert.Equal(1L + 3 * (1 + vm.Evolution.Children), Results(vm).Visits.Maximum);
        Assert.Same(home, shell.CurrentPage);
        home.OpenSgdCommand.Execute(null);

        CompleteRun(async () =>
        {
            vm.Evolution.GenerationMilliseconds = 500;
            var previousRun = Evolve(vm);
            Assert.False(vm.Evolution.RunSgdCommand.CanExecute(null));
            Assert.True(vm.Evolution.StopCommand.CanExecute(null));
            vm.Setup.Generator.ChannelCount = 7;
            vm.Setup.Generator.Method = "mssi";
            vm.Setup.Generator.SamplesText = "16";
            await vm.Sampling.GenerateAsync();
            await previousRun;
        });
        vm.SamplingStepCommand.Execute(null);
        Assert.Equal(3, vm.Evolution.Generation);
        Assert.Same(winner, vm.Evolution.Network);
        Assert.Equal(7, Results(vm).Network!.Width);
        Assert.Equal(2, Results(vm).Visits.Counts.Values.Sum());
        var packed = NetworkTransforms.Compact(NetworkTransforms.RemoveUnusedCes(Results(vm).Network!, CurrentUsage(vm)!));
        Assert.Equal(new Point(packed.ComparatorCount, packed.StageCount), Results(vm).Visits.Current);
        Assert.Equal(16, CurrentUsage(vm)!.Tested);
        Assert.False(CurrentUsage(vm)!.Exhaustive);
        Assert.Equal("Best: [1, 1]", Results(vm).BestAnnotation.Label);
        Assert.Equal($"Current: [{packed.ComparatorCount}, {packed.StageCount}]", Results(vm).CurrentAnnotation.Label);
        Assert.Equal($"{CurrentUsage(vm)!.Failed:N0} failures across 16 sampled binary inputs", Results(vm).Summary);
        Assert.Equal(Results(vm).Network!.ComparatorCount, CurrentUsage(vm)!.CeUses.Count);
        Assert.True(vm.Evolution.RunSgdCommand.CanExecute(null));
        Assert.False(vm.Evolution.StopCommand.CanExecute(null));
        Assert.Same(untouched, random.SelectedNetwork);
    });

    [Fact]
    public void Sgd_records_every_child_and_selected_parent_in_independent_buffers() => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        // Child two sorts all inputs. The next child plots at a smaller size but fails two inputs.
        vm.Setup.Generator.ChannelCount = 4;
        vm.Setup.Generator.CountText = "4";
        vm.Setup.Generator.SeedText = "44";
        var recipe = vm.Setup.Generator.Snapshot();
        var token = TestContext.Current.CancellationToken;
        var parent = NetworkSearch.Prepare(recipe.Label, recipe.CreateModel(), null, token);
        var mutator = vm.Evolution.Mutation.Create(parent.Model);
        var best = parent;
        var expected = new List<Point> { parent.Position };
        var expectedFailures = new List<int> { parent.Usage.Failed };
        string ExpectedTitle() => best.Usage.Failed == 0
            ? $"Best: [{best.Network.ComparatorCount}, {best.Network.StageCount}]"
            : "Best";
        var expectedBest = new List<string> { ExpectedTitle() };
        for (var i = 0; i < vm.Evolution.Children; i++)
        {
            var model = Sortopia.Models.Sorting.Simple.SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(mutator, parent.Model, i, 1);
            var child = NetworkSearch.Prepare(recipe.Label, model, null, token);
            expected.Add(child.Position);
            expectedFailures.Add(child.Usage.Failed);
            if (child.Improves(best)) best = child;
            expectedBest.Add(ExpectedTitle());
        }
        expected.Add(best.Position);
        expectedFailures.Add(best.Usage.Failed);
        expectedBest.Add(ExpectedTitle());
        var recorded = new List<Point>();
        var recordedBest = new List<string>();
        var recordedSummaries = new List<string>();
        CompleteRun(vm.Sampling.GenerateAsync);
        vm.Evolution.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(SgdResultsViewModel.Visits) && vm.Evolution.Visits.Current.HasValue)
            {
                recorded.Add(vm.Evolution.Visits.Current!.Value);
                recordedBest.Add(vm.Evolution.BestAnnotation.Label);
                recordedSummaries.Add(vm.Evolution.Summary);
            }
            if (change.PropertyName == nameof(vm.Evolution.Generation) && vm.Evolution.Generation == 1) vm.Stop();
        };
        CompleteRun(() => Evolve(vm));
        Assert.Equal(new[] { 1, 1, 0, 2, 1, 0 }, expectedFailures);
        Assert.Equal(expected, recorded);
        Assert.Equal(expectedBest, recordedBest);
        Assert.Equal(expectedFailures.Select(n => $"{n:N0} failures across all {parent.Usage.Tested:N0} binary inputs"), recordedSummaries);
        Assert.Equal(expected[^1], vm.Evolution.Visits.Current);
        Assert.Equal($"Current: [{expected[^1].X:0}, {expected[^1].Y:0}]", vm.Evolution.CurrentAnnotation.Label);
        foreach (var cell in expected.GroupBy(p => p)) Assert.Equal(cell.LongCount(), vm.Evolution.Visits.Counts[cell.Key]);
        Assert.Equal(expected.Count, vm.Evolution.Visits.Counts.Values.Sum());
        Assert.Equal(best.RawNetwork.Comparators, vm.Evolution.Network!.Comparators);
        Assert.Equal(2, vm.Evolution.ParentSuccess.TotalVisits + vm.Evolution.ParentFailure.TotalVisits);
        Assert.Equal(vm.Evolution.Children, vm.Evolution.ChildSuccess.TotalVisits + vm.Evolution.ChildFailure.TotalVisits);
    });

    [Fact]
    public void Graph_best_tracks_only_zero_failure_sorters_until_clear() => OnSta(() =>
    {
        var workflow = new SgdPageViewModel(() => { });
        var vm = workflow.Sampling;
        vm.Generator.ChannelCount = 4;
        vm.Generator.CountText = "1";
        vm.Generator.SeedText = "3";
        CompleteRun(vm.GenerateAsync);
        Assert.True(vm.Usage!.Failed > 0);
        Assert.False(vm.BestAnnotation.HasSorter);
        Assert.Null(vm.BestPosition);

        vm.Generator.CountText = "16";
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
        Assert.Equal("Best: [5, 3]", vm.BestAnnotation.Label);
        Assert.Equal(new Point(5, 3), vm.BestPosition);
        vm.ClearGraphCommand.Execute(null);
        Assert.False(vm.BestAnnotation.HasSorter);
        Assert.Null(vm.BestPosition);
        CompleteRun(vm.GenerateAsync);
        Assert.Null(vm.BestPosition);
        Assert.Single(vm.Visits.Counts);
    });

    [Fact]
    public void Best_known_rows_follow_the_channel_picker_and_match_each_catalog_variant() => OnSta(() =>
    {
        var workflow = new SgdPageViewModel(() => { });
        var vm = workflow.Sampling;
        var ids = SorterLib.ids();
        foreach (var width in new[] { 8, 17, 22, 32, 64, 2 })
        {
            vm.Generator.ChannelCount = width;
            Assert.Equal(ids.Where(id => id.ChannelCount == width), vm.BestKnownNetworks.Select(n => n.Id));
            Assert.Equal(vm.BestKnownNetworks.Count, vm.BestKnownNetworks.Select(n => n.VariantBrush).Distinct().Count());
            foreach (var known in vm.BestKnownNetworks)
            {
                var stages = SorterDataParse.parseTo2dCeArray(SorterLib.tryGet(known.Id).Value);
                var ces = stages.Sum(stage => stage.Length);
                var name = SorterLibVariant.toString(known.Id.SorterVariant).Replace("Variant", "Variant ");
                Assert.Equal($"{name} · {stages.Length} stages · {ces} CEs", known.BestKnownTitle);
                Assert.Equal(new Point(ces, stages.Length), known.Position);
            }
            Assert.Empty(vm.Visits.Counts);
        }
    });

    [Fact]
    public void Clear_and_Stop_apply_between_evaluated_children() => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Setup.Generator.ChannelCount = 2;
        CompleteRun(vm.Sampling.GenerateAsync);
        var original = vm.Sampling.Network;
        var childrenSeen = 0;
        vm.Evolution.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(SgdResultsViewModel.Visits) || !vm.Evolution.Visits.Current.HasValue) return;
            childrenSeen++;
            if (childrenSeen == 1) vm.Evolution.ClearGraphCommand.Execute(null);
            if (childrenSeen == 3) vm.Stop();
        };
        CompleteRun(() => Evolve(vm));
        Assert.Equal(3, childrenSeen);
        Assert.Equal(0, vm.Evolution.Generation);
        Assert.Same(original, vm.Evolution.Network);
        Assert.Equal(new Point(1, 1), vm.Evolution.Visits.Current);
        Assert.Equal(2, vm.Evolution.Visits.Maximum);
        Assert.Equal(2, vm.Evolution.Visits.Counts.Values.Sum());
    });

    [Fact]
    public void Generation_counts_completed_sorters_across_sampling_and_single_draws_until_clear() => OnSta(() =>
    {
        var workflow = new SgdPageViewModel(() => { });
        var vm = workflow.Sampling;
        vm.Generator.ChannelCount = 2;
        vm.Generator.CountText = "8";
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Network) && vm.GeneratedCount == 3)
                vm.StopCommand.Execute(null);
        };
        CompleteRun(() => vm.BatchAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("3 / 0", vm.GenerationOutcomes);
        Assert.Equal(0, vm.GeneratedCount);
        Assert.Single(vm.Visits.Counts);
        Assert.Equal(3, vm.Visits.Counts[new Point(1, 1)]);
        vm.Generator.SeedText = "invalid";
        Assert.True(vm.GenerateCommand.CanExecute(null)); // Make one replaces the invalid seed.
        Assert.False(vm.BatchCommand.CanExecute(null));
        vm.NewSeedCommand.Execute(null);
        var seed = int.Parse(vm.Generator.SeedText);
        Assert.Equal("3 / 0", vm.GenerationOutcomes);
        CompleteRun(() => vm.GenerateAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(seed, vm.GenerationResult!.Seed);
        Assert.Equal(4, vm.Visits.Maximum);
        Assert.Equal("4 / 0", vm.GenerationOutcomes);
        CompleteRun(() => vm.BatchAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("7 / 0", vm.GenerationOutcomes);
        Assert.Equal(7, vm.Visits.Maximum);
        Assert.Equal(0, vm.GeneratedCount);
        vm.Generator.ChannelCount = 8;
        vm.Generator.CountText = "1";
        CompleteRun(vm.GenerateAsync);
        Assert.True(vm.Usage!.Failed > 1);
        Assert.Equal("7 / 1", vm.GenerationOutcomes);
        vm.ClearGraphCommand.Execute(null);
        Assert.Equal("0 / 0", vm.GenerationOutcomes);
        Assert.Empty(vm.Visits.Counts);
        Assert.Null(vm.Visits.Current);
        Assert.Equal(0, vm.Visits.Maximum);
        Assert.Null(vm.Network);
        Assert.Null(vm.DisplayUsage);
        Assert.True(workflow.EvolutionStepCommand.CanExecute(null));
    });

    [Fact]
    public void Make_one_chooses_a_fresh_seed_for_each_single_draw_even_if_the_seed_editor_is_invalid() => OnSta(() =>
    {
        var workflow = new SgdPageViewModel(() => { });
        var vm = workflow.Sampling;
        vm.Generator.ChannelCount = 2;
        vm.Generator.CountText = "8";
        vm.Generator.SeedText = "73";
        for (var draw = 1; draw <= 3; draw++)
        {
            if (draw == 3) vm.Generator.SeedText = "invalid";
            var previousSeed = vm.Generator.SeedText;
            Assert.True(vm.GenerateCommand.CanExecute(null));
            CompleteRun(async () =>
            {
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var started = false;
                void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
                {
                    if (change.PropertyName != nameof(vm.CanEdit)) return;
                    if (!vm.CanEdit) started = true;
                    else if (started) completed.TrySetResult();
                }
                vm.PropertyChanged += Changed;
                try
                {
                    vm.GenerateCommand.Execute(null);
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally { vm.PropertyChanged -= Changed; }
            });
            Assert.NotEqual(previousSeed, vm.Generator.SeedText);
            Assert.Equal(int.Parse(vm.Generator.SeedText), vm.GenerationResult!.Seed);
            Assert.Equal($"{draw} / 0", vm.GenerationOutcomes);
            Assert.False(vm.StopCommand.CanExecute(null));
        }
    });

    [Fact]
    public void Sampling_clicks_prefer_visible_A_and_summary_follows_the_visible_draw() => OnSta(() =>
    {
        var workflow = new SgdPageViewModel(() => { });
        var vm = workflow.Sampling;
        vm.Generator.ChannelCount = 4;
        vm.Generator.CountText = "1";
        vm.Generator.SeedText = "3";
        CompleteRun(vm.GenerateAsync);
        var a = vm.Network;
        var aFailures = vm.Usage!.Failed;
        Assert.True(aFailures > 0);
        vm.SampleToB = true;
        vm.Generator.ChannelCount = 2;
        vm.Generator.CountText = "8";
        vm.Generator.SamplesText = "32";
        CompleteRun(vm.GenerateAsync);
        var b = vm.Network;
        var point = new Point(1, 1);
        Assert.Equal(point, vm.Visits.Current);
        foreach (var annotation in vm.Annotations.Skip(3)) annotation.IsShown = false;
        vm.ShowGraphNetwork(point);
        Assert.Same(a, vm.Network);
        Assert.Equal("0 failures across 32 sampled binary inputs", vm.Summary);
        vm.FirstGroup.Failure.IsVisible = false;
        Assert.Same(b, vm.Network);
        vm.FirstGroup.Failure.IsVisible = true;
        Assert.Same(a, vm.Network);
        vm.SecondGroup.Success.IsVisible = false;
        Assert.Equal($"{aFailures} failures across all 16 binary inputs", vm.Summary);
        vm.ClearGroupCommand.Execute(vm.FirstGroup);
        Assert.Empty(vm.Summary);
        Assert.Null(vm.Network);
        Assert.Null(vm.SelectedNetworkPosition);
        Assert.False(vm.EvolveSelectionCommand.CanExecute(null));
        Assert.Equal(point, vm.GraphSelectionPosition);
        vm.SecondGroup.Success.IsVisible = true;
        Assert.Same(b, vm.Network);
        Assert.Equal(point, vm.GraphSelectionPosition);
    });

    [Fact]
    public void Clearing_the_selected_sampling_buffer_leaves_the_viewer_empty_until_another_selection_or_draw() => OnSta(() =>
    {
        var workflow = new SgdPageViewModel(() => { });
        var vm = workflow.Sampling;
        vm.Generator.ChannelCount = 2;
        workflow.SamplingStepCommand.Execute(null);
        CompleteRun(vm.GenerateAsync);
        vm.SampleToB = true;
        CompleteRun(vm.GenerateAsync);
        var b = vm.Network;
        vm.NetworkHistory.PreviousCommand.Execute(null);
        vm.ClearGroupCommand.Execute(vm.FirstGroup);
        Assert.Null(vm.Network);
        Assert.Null(vm.DisplayUsage);
        Assert.Null(vm.SelectedNetworkPosition);
        Assert.Empty(vm.DisplayedNetworkDescription);
        Assert.False(vm.EvolveSelectionCommand.CanExecute(null));
        Assert.Single(vm.NetworkHistory.Entries);
        vm.NetworkHistory.NextCommand.Execute(null);
        Assert.Same(b, vm.Network);
        vm.NetworkHistory.FollowLiveCommand.Execute(null);
        vm.ClearGroupCommand.Execute(vm.FirstGroup);
        Assert.Same(b, vm.Network);
        vm.ClearGroupCommand.Execute(vm.SecondGroup);
        Assert.Null(vm.Network);
        CompleteRun(vm.GenerateAsync);
        Assert.NotNull(vm.Network);
        Assert.True(vm.EvolveSelectionCommand.CanExecute(null));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_graph_selection_keeps_its_coordinate_across_buffer_visibility_changes(bool evolution) => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Setup.Generator.ChannelCount = 2;
        CompleteRun(vm.Sampling.GenerateAsync);
        if (evolution) vm.Sampling.EvolveSelectionCommand.Execute(null);
        foreach (var annotation in Results(vm).Annotations.Skip(3)) annotation.IsShown = false;
        var network = Results(vm).Network;
        var occupied = Results(vm).SelectedNetworkPosition!.Value;
        var empty = new Point(occupied.X + 1, occupied.Y + 1);
        Results(vm).SelectGraphPointCommand.Execute(empty);
        void AssertEmpty(Point position)
        {
            Assert.Equal(position, Results(vm).GraphSelectionPosition);
            Assert.Equal(position, Results(vm).NetworkHistory.GraphPosition);
            Assert.Equal("Graph", Results(vm).NetworkHistory.IndexText);
            Assert.Null(Results(vm).Network);
            Assert.Null(Results(vm).DisplayUsage);
            Assert.Null(Results(vm).SelectedNetworkPosition);
            Assert.Empty(Results(vm).DisplayedNetworkDescription);
            Assert.False(Results(vm).SelectedAnnotation.HasSorter);
            if (!evolution) Assert.False(vm.Sampling.EvolveSelectionCommand.CanExecute(null));
            Assert.True(Results(vm).NetworkHistory.FollowLiveCommand.CanExecute(null));
        }
        AssertEmpty(empty);
        foreach (var group in new[] { Results(vm).FirstGroup, Results(vm).SecondGroup })
            foreach (var buffer in new[] { group.Success, group.Failure })
            {
                buffer.IsVisible = false;
                AssertEmpty(empty);
                buffer.IsVisible = true;
                AssertEmpty(empty);
            }
        Results(vm).NetworkHistory.FollowLiveCommand.Execute(null);
        Assert.Same(network, Results(vm).Network);
        Assert.Null(Results(vm).NetworkHistory.GraphPosition);
        Results(vm).FirstGroup.Success.IsVisible = false;
        Results(vm).ShowGraphNetwork(occupied);
        AssertEmpty(occupied);
        Results(vm).FirstGroup.Success.IsVisible = true;
        Assert.Same(network, Results(vm).Network);
        Assert.Equal(occupied, Results(vm).GraphSelectionPosition);
        Assert.True(Results(vm).SelectedAnnotation.HasSorter);
        Results(vm).FirstGroup.Success.IsVisible = false;
        AssertEmpty(occupied);
        Results(vm).NetworkHistory.IndexText = "1";
        Results(vm).NetworkHistory.FinishIndexEdit(commit: true);
        Assert.Same(network, Results(vm).Network);
        Assert.Null(Results(vm).NetworkHistory.GraphPosition);
    });

    [Fact]
    public void Continuous_sampling_follows_new_draws_and_keeps_inspections_until_released() => OnSta(() =>
    {
        var workflow = new SgdPageViewModel(() => { });
        var vm = workflow.Sampling;
        vm.Generator.ChannelCount = 2;
        vm.Generator.CountText = "8";
        NetworkDefinition? first = null, inspected = null;
        var draws = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(vm.GeneratedCount) || vm.GeneratedCount == 0) return;
            draws++;
            var history = vm.NetworkHistory;
            switch (draws)
            {
                case 1:
                    first = vm.Network;
                    break;
                case 2:
                    Assert.NotSame(first, vm.Network);
                    Assert.Same(vm.Usage, vm.DisplayUsage);
                    vm.ShowGraphNetwork(vm.Visits.Current!.Value);
                    inspected = vm.Network;
                    break;
                case 3:
                    Assert.Same(inspected, vm.Network);
                    Assert.NotSame(vm.Usage, vm.DisplayUsage);
                    history.PreviousCommand.Execute(null);
                    Assert.Same(first, vm.Network);
                    break;
                case 4:
                    Assert.Same(first, vm.Network);
                    history.FollowLiveCommand.Execute(null);
                    Assert.Same(vm.Usage, vm.DisplayUsage);
                    vm.ShowGraphNetwork(vm.Visits.Current!.Value);
                    break;
                case 5:
                    Assert.NotSame(vm.Usage, vm.DisplayUsage);
                    Assert.Single(history.Entries);
                    history.FollowLiveCommand.Execute(null);
                    Assert.Same(vm.Usage, vm.DisplayUsage);
                    workflow.Stop();
                    break;
            }
        };
        CompleteRun(() => vm.BatchAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.DoesNotContain("could not finish", vm.Status);
        Assert.Equal(5, draws);
        Assert.Same(vm.Usage, vm.DisplayUsage);
    });

    [Fact]
    public void Evolve_uses_the_inspected_sample_even_as_a_batch_generates_newer_sorters() => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Setup.Generator.Method = "mssi";
        vm.Setup.Generator.ChannelCount = 4;
        vm.Setup.Generator.CountText = "4";
        vm.SamplingStepCommand.Execute(null);
        Assert.False(vm.Sampling.EvolveSelectionCommand.CanExecute(null));
        CompleteRun(vm.Sampling.GenerateAsync);
        var selected = Results(vm).NetworkHistory.Current!;
        var recipe = vm.Setup.Generator.Snapshot();

        vm.Sampling.SampleToB = true;
        vm.Setup.Generator.Method = "msce";
        vm.Setup.Generator.ChannelCount = 2;
        CompleteRun(vm.Sampling.GenerateAsync);
        vm.Sampling.EvolveSelectionCommand.Execute(null);
        Results(vm).ShowGraphNetwork(Results(vm).SelectedNetworkPosition!.Value);
        vm.SamplingStepCommand.Execute(null);
        Results(vm).NetworkHistory.PreviousCommand.Execute(null);
        Assert.Same(selected.Network, Results(vm).Network);
        Results(vm).ShowGraphNetwork(selected.Position!.Value);
        Assert.Same(selected.Network, Results(vm).Network);

        CompleteRun(async () =>
        {
            var newerDraws = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.Sampling.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.Sampling.GeneratedCount) && vm.Sampling.GeneratedCount == 2) newerDraws.TrySetResult();
            };
            var batch = vm.Sampling.BatchAsync();
            try
            {
                await newerDraws.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(2, vm.Sampling.GenerationResult!.Channels);
                Assert.True(vm.Sampling.EvolveSelectionCommand.CanExecute(null));
                vm.Sampling.EvolveSelectionCommand.Execute(null);
                await batch.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { vm.Stop(); }
        });

        Assert.Equal(3, (int)vm.CurrentStep);
        Assert.Same(selected.Network, Results(vm).Network);
        Assert.Same(selected.Usage, CurrentUsage(vm));
        Assert.Equal(selected.Position, Results(vm).SelectedNetworkPosition);
        Assert.StartsWith("Parents", Results(vm).DisplayedNetworkDescription);
        Assert.Equal("mssi", vm.Evolution.Mutation.Method);
        Assert.True(vm.Evolution.RunSgdCommand.CanExecute(null));
        Assert.False(vm.Evolution.StopCommand.CanExecute(null));
        Assert.Equal(0, vm.Evolution.Generation);
        var token = TestContext.Current.CancellationToken;
        var parent = NetworkSearch.Prepare(recipe.Label, recipe.CreateModel(), null, token);
        vm.Evolution.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Evolution.Generation) && vm.Evolution.Generation == 1) vm.Stop(); };
        CompleteRun(async () =>
        {
            var expected = await NetworkSearch.NextGenerationAsync(parent, vm.Evolution.Mutation.Create(parent.Model), 1, vm.Evolution.Children, token);
            await Evolve(vm).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, vm.Evolution.Generation);
            Assert.Equal(expected.RawNetwork.Comparators, Results(vm).Network!.Comparators);
        });
    });

    [Fact]
    public void Run_SGD_takes_over_the_last_completed_random_seed() => OnSta(() =>
    {
        var vm = new SgdPageViewModel(() => { });
        NetworkRecipe? seed = null;
        Point? lastDraw = null;
        Task? evolution = null;
        vm.Sampling.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(SgdResultsViewModel.Network) && vm.Sampling.GeneratedCount == 3)
            {
                seed = vm.Setup.Generator.Snapshot();
                lastDraw = Results(vm).Visits.Current;
                vm.EvolutionStepCommand.Execute(null);
                evolution = ResumeAfterSampling();
            }

        };
        vm.Evolution.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Evolution.Generation) && vm.Evolution.Generation == 12) vm.Stop();
        };
        async Task ResumeAfterSampling()
        {
            while (vm.Sampling.IsRunning) await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await vm.Evolution.RunSgdAsync();
        }
        CompleteRun(async () =>
        {
            await vm.Sampling.BatchAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(evolution);
            await evolution.WaitAsync(TimeSpan.FromSeconds(10));
        });
        Assert.NotNull(seed);
        var token = TestContext.Current.CancellationToken;
        var parent = NetworkSearch.Prepare(seed.Label, seed.CreateModel(), null, token);
        var mutator = vm.Evolution.Mutation.Create(parent.Model);
        Assert.Equal(parent.Position, lastDraw);
        CompleteRun(async () =>
        {
            for (var g = 1; g <= 12; g++)
                parent = await NetworkSearch.NextGenerationAsync(parent, mutator, g, vm.Evolution.Children, token);
        });
        Assert.Equal(parent.RawNetwork.Comparators, Results(vm).Network!.Comparators);
        Assert.Equal(1L + 12 * (1 + vm.Evolution.Children), Results(vm).Visits.Counts.Values.Sum());
        Assert.Equal(0, vm.Sampling.GeneratedCount);
        Assert.Equal(12, vm.Evolution.Generation);
    });

    private static void CheckGraphPaneResizing(Sortopia.WPF.Views.Pages.Sgd.SgdPage page)
    {
        var controls = (Viewbox)FindSgdElement(page, "GraphControlsViewport");
        var border = Assert.IsType<Border>(controls.Child);
        var grid = (Grid)controls.Parent;
        var splitter = grid.Children.OfType<GridSplitter>().Single();
        var plotViewport = (Viewbox)FindSgdElement(page, "PlotViewport");
        var plot = Assert.IsType<EvolutionPlot>(plotViewport.Child);
        var plotColumn = grid.ColumnDefinitions[Grid.GetColumn(plotViewport)];
        var legend = (Viewbox)FindSgdElement(page, "UsageLegendViewport");
        var toolbar = (Grid)legend.Parent;
        var layoutViewport = (Viewbox)FindSgdElement(page, "NetworkLayoutViewport");
        var layouts = (Sortopia.WPF.Views.Shared.Controls.StageLayoutSelector)layoutViewport.Child;
        var buttons = ((WrapPanel)layouts.Content).Children.OfType<RadioButton>().ToArray();
        void Layout(double width)
        {
            page.Measure(new Size(width, 800));
            page.Arrange(new Rect(0, 0, width, 800));
            page.UpdateLayout();
            var plotBounds = plot.TransformToAncestor(grid).TransformBounds(new Rect(plot.RenderSize));
            Assert.True(plotBounds.Left >= plotColumn.Offset - 0.01);
            Assert.True(plotBounds.Right <= plotColumn.Offset + plotColumn.ActualWidth + 0.01);
            Assert.InRange(plotBounds.Height, 1, grid.ActualHeight + 0.01);
            var lastButton = buttons.Last();
            var buttonBounds = lastButton.TransformToAncestor(toolbar).TransformBounds(new Rect(lastButton.RenderSize));
            Assert.True(buttonBounds.Right <= toolbar.ColumnDefinitions[0].ActualWidth + 0.01);
            var bufferPanel = (Border)FindSgdElement(page, "GraphBuffersPanel");
            var annotations = (ItemsControl)FindSgdElement(page, "AnnotationToggles");
            Assert.Equal(new Thickness(4), bufferPanel.Padding);
            Assert.Equal(bufferPanel.ActualWidth, annotations.MaxWidth, 5);
            Assert.True(annotations.ActualWidth <= bufferPanel.ActualWidth + 0.01);
        }
        void CheckLegend()
        {
            var bounds = legend.TransformToAncestor(toolbar).TransformBounds(new Rect(legend.RenderSize));
            var slot = toolbar.ColumnDefinitions[1];
            Assert.True(bounds.Width > 0);
            Assert.InRange(Math.Abs(slot.Offset + slot.ActualWidth / 2 - (bounds.Left + bounds.Width / 2)), 0, 0.5);
            Assert.True(bounds.Left >= slot.Offset + 15 - 0.5);
            Assert.True(bounds.Right <= slot.Offset + slot.ActualWidth - 15 + 0.5);
        }

        Layout(1000);
        CheckLegend();
        var initialPlotWidth = plotColumn.ActualWidth;
        var initialBounds = border.TransformToAncestor(grid).TransformBounds(new Rect(border.RenderSize));
        var shrinkBy = grid.ColumnDefinitions[0].ActualWidth - (initialBounds.Width / 2 + controls.Margin.Left + controls.Margin.Right);
        var peer = new GridSplitterAutomationPeer(splitter);
        var transform = (ITransformProvider)peer.GetPattern(PatternInterface.Transform)!;
        transform.Move(-shrinkBy, 0);
        page.UpdateLayout();
        var bounds = border.TransformToAncestor(grid).TransformBounds(new Rect(border.RenderSize));
        Assert.True(plotColumn.ActualWidth > initialPlotWidth);
        Assert.InRange(bounds.Width, 1, initialBounds.Width - 1);
        Assert.InRange(bounds.Left, controls.Margin.Left - 0.51, grid.ColumnDefinitions[0].ActualWidth);
        Assert.True(bounds.Right <= grid.ColumnDefinitions[0].ActualWidth - controls.Margin.Right + 0.51);
        Assert.True(bounds.Top >= controls.Margin.Top - 0.51 && bounds.Bottom <= grid.ActualHeight - controls.Margin.Bottom + 0.51);
        Assert.Equal(border.ActualWidth / border.ActualHeight, bounds.Width / bounds.Height, 5);
        Layout(640);
        CheckLegend();
        var diagram = (NetworkDiagram)FindSgdElement(page, "SwitchDiagram");
        foreach (var button in buttons)
        {
            button.IsChecked = true;
            Assert.Equal(Array.IndexOf(buttons, button), diagram.DisplayMode);
        }
        Layout(480);
        var navigation = (Viewbox)FindSgdElement(page, "NetworkNavigationViewport");
        var row = (StackPanel)navigation.Child;
        var slot = toolbar.ColumnDefinitions[Grid.GetColumn(navigation)];
        var index = (TextBox)FindSgdElement(page, "NetworkIndex");
        var centerY = index.TranslatePoint(new Point(0, index.ActualHeight / 2), toolbar).Y;
        var previousRight = slot.Offset;
        foreach (FrameworkElement control in row.Children)
        {
            var rect = control.TransformToAncestor(toolbar).TransformBounds(new Rect(control.RenderSize));
            Assert.True(rect.Width > 0 && rect.Left >= previousRight - 0.01);
            Assert.Equal(centerY, rect.Top + rect.Height / 2, 5);
            previousRight = rect.Right;
        }
        Assert.True(previousRight <= toolbar.ActualWidth + 0.01);
        Layout(1000);
        transform.Move(shrinkBy, 0);
        page.UpdateLayout();
    }

    private static void CheckGraphBufferTiles()
    {
        var vm = new SgdPageViewModel(() => { });
        var page = new Sortopia.WPF.Views.Pages.Sgd.SgdPage { DataContext = vm };
        page.Measure(new Size(1000, 800));
        page.Arrange(new Rect(0, 0, 1000, 800));
        CompleteRun(async () => await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle));
        Assert.Same(vm.Setup, vm.CurrentPage);
        vm.Setup.Generator.ChannelCount = 2;
        vm.SamplingStepCommand.Execute(null);
        page.UpdateLayout();
        Assert.False(vm.Sampling.ShowResultPanes);
        CompleteRun(vm.Sampling.GenerateAsync);
        page.UpdateLayout();
        var population = (System.Windows.Controls.Primitives.ToggleButton)FindSgdElement(page, "StatsPopulationToggle");
        var measure = (System.Windows.Controls.Primitives.ToggleButton)FindSgdElement(page, "StatsMeasureToggle");
        Assert.Equal("Unique", population.Content);
        Assert.Equal("CEs", measure.Content);
        static void Toggle(System.Windows.Controls.Primitives.ToggleButton button, bool value) =>
            button.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, value);
        static IEnumerable<TextBlock> Labels(DependencyObject visual)
        {
            if (visual is TextBlock label) { yield return label; yield break; }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
                foreach (var text in Labels(VisualTreeHelper.GetChild(visual, i))) yield return text;
        }
        var names = new[] { "ParentSuccessToggle", "ParentFailureToggle", "ChildSuccessToggle", "ChildFailureToggle" };
        var viewer = (DockPanel)FindSgdElement(page, "NetworkViewer");
        var graph = (Border)FindSgdElement(page, "ResultsGraph");
        Assert.Equal(Visibility.Visible, viewer.Visibility);
        Assert.Equal(Visibility.Visible, graph.Visibility);
        CheckGraphPaneResizing(page);
        var annotations = (ItemsControl)FindSgdElement(page, "AnnotationToggles");
        System.Windows.Controls.Primitives.ToggleButton AnnotationButton(SorterAnnotation item)
        {
            page.UpdateLayout();
            var container = annotations.ItemContainerGenerator.ContainerFromItem(item);
            return Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(VisualTreeHelper.GetChild(container, 0));
        }
        var annotatedPlot = (EvolutionPlot)((Viewbox)FindSgdElement(page, "PlotViewport")).Child;
        var currentButton = AnnotationButton(Results(vm).CurrentAnnotation);
        var bestButton = AnnotationButton(Results(vm).BestAnnotation);
        var selectedButton = AnnotationButton(Results(vm).SelectedAnnotation);
        Assert.True(selectedButton.IsEnabled);
        Assert.Equal("Selected: [1, 1]", selectedButton.Content);
        Toggle(selectedButton, false);
        Assert.Null(annotatedPlot.SelectedPosition);
        Assert.NotNull(Results(vm).Network);
        Toggle(selectedButton, true);
        Assert.Equal(Results(vm).SelectedNetworkPosition, annotatedPlot.SelectedPosition);
        Assert.True(currentButton.IsEnabled);
        Assert.True(bestButton.IsEnabled);
        Assert.Equal("Current: [1, 1]", currentButton.Content);
        Assert.All(Labels(currentButton), label => Assert.Same(Results(vm).CurrentAnnotation.Brush, label.Foreground));
        Toggle(currentButton, false);
        Toggle(bestButton, false);
        Assert.False(annotatedPlot.ShowCurrent);
        Assert.Null(annotatedPlot.BestPosition);
        Toggle(currentButton, true);
        Toggle(bestButton, true);
        Assert.True(annotatedPlot.ShowCurrent);
        Assert.Equal(Results(vm).Visits.Current, annotatedPlot.BestPosition);
        var variantButton = AnnotationButton(Results(vm).Annotations.Single(item => item.Name == "Variant A"));
        Toggle(variantButton, false);
        Assert.Empty(annotatedPlot.BestKnownNetworks);
        Toggle(variantButton, true);
        Assert.Single(annotatedPlot.BestKnownNetworks);
        Results(vm).ClearGroupCommand.Execute(Results(vm).FirstGroup);
        Assert.False(selectedButton.IsEnabled);
        Assert.Null(annotatedPlot.SelectedPosition);
        vm.EvolutionStepCommand.Execute(null);
        vm.Evolution.ClearGraphCommand.Execute(null);
        page.UpdateLayout();
        annotations = (ItemsControl)FindSgdElement(page, "AnnotationToggles");
        annotatedPlot = (EvolutionPlot)((Viewbox)FindSgdElement(page, "PlotViewport")).Child;
        population = (System.Windows.Controls.Primitives.ToggleButton)FindSgdElement(page, "StatsPopulationToggle");
        measure = (System.Windows.Controls.Primitives.ToggleButton)FindSgdElement(page, "StatsMeasureToggle");
        viewer = (DockPanel)FindSgdElement(page, "NetworkViewer");
        graph = (Border)FindSgdElement(page, "ResultsGraph");
        Assert.True(AnnotationButton(Results(vm).SelectedAnnotation).IsEnabled);
        Assert.Equal(new Point(1, 1), annotatedPlot.SelectedPosition); // Viewer shows the Evolution parent.
        Assert.False(AnnotationButton(Results(vm).CurrentAnnotation).IsEnabled);
        Assert.False(AnnotationButton(Results(vm).BestAnnotation).IsEnabled);
        var buffers = new[] { vm.Evolution.ParentSuccess, vm.Evolution.ParentFailure, vm.Evolution.ChildSuccess, vm.Evolution.ChildFailure };
        for (var i = 0; i < names.Length; i++)
        {
            var tile = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(FindSgdElement(page, names[i]));
            Assert.True(tile.IsChecked);
            var network = CatalogNetwork.LoadAll()[0];
            var entry = new NetworkHistoryEntry(network, null, "identity");
            var fingerprint = SorterFingerprint.Create(network);
            var score = new SorterScore(buffers[i].Successful ? 0 : 1, 2, 4);
            page.UpdateLayout();
            var initialTileSize = tile.RenderSize;
            var statisticsPanel = (Border)FindSgdElement(page, "GraphBuffersPanel");
            var initialPanelSize = statisticsPanel.RenderSize;
            void CheckStats(int count, string min, string max, string mean, string sd)
            {
                page.UpdateLayout();
                Assert.Equal(initialTileSize, tile.RenderSize);
                Assert.Equal(initialPanelSize, statisticsPanel.RenderSize);
                var labels = Labels(tile).ToArray();
                Assert.Equal(new[] { $"{(buffers[i].Successful ? "✓" : "×")} {count}", "min: ", min, "max: ", max, "mean: ", mean, "sd: ", sd }, labels.Select(label => label.Text));
                double RightEdge(TextBlock label) => label.TranslatePoint(new Point(label.ActualWidth, 0), tile).X;
                Assert.Equal(RightEdge(labels[2]), RightEdge(labels[6]), 4); // min and mean
                Assert.Equal(RightEdge(labels[4]), RightEdge(labels[8]), 4); // max and sd
                foreach (var index in new[] { 2, 4, 6, 8 })
                {
                    var label = labels[index - 1];
                    var value = labels[index];
                    Assert.True(value.TranslatePoint(new Point(), tile).X > RightEdge(label));
                    var field = Assert.IsType<Viewbox>(value.Parent);
                    var valueBounds = value.TransformToAncestor(field).TransformBounds(new Rect(value.RenderSize));
                    Assert.True(valueBounds.Width <= field.ActualWidth + 0.01);
                }
            }
            CheckStats(0, "—", "—", "—", "—");
            Assert.InRange(tile.ActualHeight, 1, 63);
            buffers[i].Record(score, 1, entry, fingerprint);
            CheckStats(1, "4", "4", "4", "0");
            buffers[i].Record(score, 2, entry, fingerprint);
            Toggle(tile, false);
            Assert.False(buffers[i].IsVisible);
            for (var j = i + 1; j < buffers.Length; j++) Assert.True(buffers[j].IsVisible);
            buffers[i].Record(score with { StageCount = 4, ComparatorCount = 10 }, 3, entry, new SorterFingerprint(0, 1));
            CheckStats(2, "4", "10", "7", "3");
            Toggle(population, true);
            CheckStats(3, "4", "10", "6", "2.83");
            Toggle(measure, true);
            CheckStats(3, "2", "4", "2.67", "0.94");
            Toggle(population, false);
            CheckStats(2, "2", "4", "3", "1");
            Toggle(measure, false);
            buffers[i].IsVisible = true;
            Assert.True(tile.IsChecked);
            vm.Evolution.ClearGroupCommand.Execute(i < 2 ? vm.Evolution.FirstGroup : vm.Evolution.SecondGroup);
            CheckStats(0, "—", "—", "—", "—");
            Toggle(population, true);
            CheckStats(0, "—", "—", "—", "—");
            Toggle(population, false);
            buffers[i].Record(score with { StageCount = 10_000, ComparatorCount = 1_000_000 }, 4, entry, fingerprint);
            CheckStats(1, "1,000,000", "1,000,000", "1,000,000", "0");
            vm.Evolution.ClearGroupCommand.Execute(i < 2 ? vm.Evolution.FirstGroup : vm.Evolution.SecondGroup);
            CheckStats(0, "—", "—", "—", "—");
        }

        vm.Evolution.ClearGraphCommand.Execute(null);
        vm.SetupStepCommand.Execute(null);
        page.UpdateLayout();
        Assert.Same(vm.Setup, vm.CurrentPage);
        vm.SamplingStepCommand.Execute(null);
        page.UpdateLayout();
        annotations = (ItemsControl)FindSgdElement(page, "AnnotationToggles");
        annotatedPlot = (EvolutionPlot)((Viewbox)FindSgdElement(page, "PlotViewport")).Child;
        population = (System.Windows.Controls.Primitives.ToggleButton)FindSgdElement(page, "StatsPopulationToggle");
        measure = (System.Windows.Controls.Primitives.ToggleButton)FindSgdElement(page, "StatsMeasureToggle");
        viewer = (DockPanel)FindSgdElement(page, "NetworkViewer");
        graph = (Border)FindSgdElement(page, "ResultsGraph");
        var destination = (System.Windows.Controls.Primitives.ToggleButton)FindSgdElement(page, "SamplingDestination");
        var clearFirst = (Button)FindSgdElement(page, "ClearFirstGroup");
        var clearSecond = (Button)FindSgdElement(page, "ClearSecondGroup");
        Assert.Equal(3, Grid.GetColumn(clearFirst));
        Assert.Equal(3, Grid.GetColumn(clearSecond));
        Assert.Equal(1, Grid.GetRow(clearFirst));
        Assert.Equal(2, Grid.GetRow(clearSecond));
        void Clear(Button button) => button.Command.Execute(button.CommandParameter);
        Assert.Equal("Clear A", ((TextBlock)clearFirst.Content).Text);
        Assert.Equal("Buffer A", destination.Content);
        page.UpdateLayout();
        var tileSurface = (Border)destination.Template.FindName("Tile", destination);
        var bufferAColor = ((SolidColorBrush)tileSurface.Background).Color;
        Assert.Equal(1, destination.Opacity);
        CompleteRun(vm.Sampling.GenerateAsync);
        Assert.Equal(Visibility.Visible, viewer.Visibility);
        Assert.Equal(Visibility.Visible, graph.Visibility);
        vm.Setup.Generator.ChannelCount = 4;
        vm.Setup.Generator.CountText = "1";
        vm.Setup.Generator.SeedText = "3";
        CompleteRun(vm.Sampling.GenerateAsync);
        Assert.Equal(1, Results(vm).FirstGroup.Failure.UniqueCount);
        vm.Setup.Generator.ChannelCount = 2;
        vm.Setup.Generator.CountText = "8";
        destination.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        Assert.Equal("Buffer B", destination.Content);
        Assert.Equal(bufferAColor, ((SolidColorBrush)tileSurface.Background).Color);
        Assert.Equal(1, destination.Opacity);
        vm.Sampling.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(vm.Sampling.GeneratedCount)) return;
            if (vm.Sampling.GeneratedCount == 1) vm.Sampling.SampleToB = false; // The current run still belongs to B.
            if (vm.Sampling.GeneratedCount == 2) vm.Stop();
        };
        CompleteRun(() => vm.Sampling.BatchAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, Results(vm).FirstGroup.Success.TotalVisits);
        Assert.Equal(2, Results(vm).SecondGroup.Success.TotalVisits);
        var bSuccess = (System.Windows.Controls.Primitives.ToggleButton)FindSgdElement(page, "ChildSuccessToggle");
        var plot = (EvolutionPlot)((Viewbox)FindSgdElement(page, "PlotViewport")).Child;
        var legend = (TextBlock)FindSgdElement(page, "PlotLegendTitle");
        var maximum = (TextBlock)FindSgdElement(page, "PlotLegendMax");
        var scale = (System.Windows.Shapes.Rectangle)FindSgdElement(page, "PlotLegendScale");
        var bufferPanel = (Border)FindSgdElement(page, "GraphBuffersPanel");
        Toggle(population, true);
        Toggle(measure, true);
        page.UpdateLayout();
        Assert.Equal("Visits", population.Content);
        Assert.Equal("Visits per point (log scale)", legend.Text);
        Assert.True(scale.ActualWidth > 0);
        Assert.Equal(bufferPanel.ActualWidth - 8, scale.ActualWidth, 5);
        Assert.Equal("Max 4", maximum.Text);
        Assert.Same(Results(vm).Visits, plot.Visits);
        Assert.True(plot.FillHighlightMarkers);
        Assert.Equal("Stages", measure.Content);
        Assert.Equal(new[] { "✓ 2", "min: ", "1", "max: ", "1", "mean: ", "1", "sd: ", "0" }, Labels(bSuccess).Select(label => label.Text));
        Toggle(population, false);
        page.UpdateLayout();
        Assert.Equal("✓ 1", Labels(bSuccess).First().Text);
        Assert.Equal("Unique sorters per point (log scale)", legend.Text);
        Assert.Equal("Max 2", maximum.Text); // The width-two sorter occurs in both A and B.
        Assert.Same(Results(vm).PlotData, plot.Visits);
        Assert.False(plot.FillHighlightMarkers);
        Clear(clearFirst);
        Assert.Equal("Max 1", maximum.Text);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(scale.Fill).Color);
        Assert.Equal(0, Results(vm).FirstGroup.Success.UniqueCount);
        Assert.Equal(0, Results(vm).FirstGroup.Failure.UniqueCount);
        Assert.Equal(2, Results(vm).Visits.Counts.Values.Sum());
        Assert.Single(Results(vm).NetworkHistory.Entries);

        vm.EvolutionStepCommand.Execute(null);
        page.UpdateLayout();
        clearFirst = (Button)FindSgdElement(page, "ClearFirstGroup");
        clearSecond = (Button)FindSgdElement(page, "ClearSecondGroup");
        Assert.Equal("Clear Parents", ((TextBlock)clearFirst.Content).Text);
        Assert.Empty(Results(vm).Visits.Counts);
        vm.Evolution.Children = 1;
        vm.Evolution.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Evolution.Generation) && vm.Evolution.Generation == 1) vm.Stop(); };
        CompleteRun(() => Evolve(vm).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, vm.Evolution.ParentSuccess.TotalVisits);
        Assert.Equal(1, vm.Evolution.ChildSuccess.TotalVisits);
        Clear(clearSecond);
        Assert.Equal(2, Results(vm).Visits.Counts.Values.Sum());
        Assert.Single(Results(vm).NetworkHistory.Entries);
        Clear(clearFirst);
        Assert.Empty(Results(vm).NetworkHistory.Entries);
        Assert.Empty(Results(vm).Visits.Counts);
        vm.SamplingStepCommand.Execute(null);
        page.UpdateLayout();
        clearSecond = (Button)FindSgdElement(page, "ClearSecondGroup");
        maximum = (TextBlock)FindSgdElement(page, "PlotLegendMax");
        scale = (System.Windows.Shapes.Rectangle)FindSgdElement(page, "PlotLegendScale");
        Assert.Equal("Clear B", ((TextBlock)clearSecond.Content).Text);
        Assert.Equal("Max 1", maximum.Text);
        Assert.Equal(2, Results(vm).SecondGroup.Success.TotalVisits);
        Clear(clearSecond);
        Assert.Empty(Results(vm).Visits.Counts);
        Assert.Equal("Max 0", maximum.Text);
        Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(scale.Fill).Color);
    }

    [Fact]
    public void Evolution_plot_outlines_selected_point_without_changing_current_fill() => OnSta(() =>
    {
        var selected = new Point(17, 10);
        var plot = new EvolutionPlot { Visits = EvolutionVisits.Empty.Record(selected), SelectedPosition = selected };
        plot.Measure(new Size(400, 176));
        plot.Arrange(new Rect(0, 0, 400, 176));
        plot.UpdateLayout();
        static IEnumerable<GeometryDrawing> Shapes(Drawing drawing)
        {
            if (drawing is GeometryDrawing shape) yield return shape;
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var item in Shapes(child)) yield return item;
        }
        var shapes = Shapes(VisualTreeHelper.GetDrawing(plot)).ToArray();
        var outline = Assert.Single(shapes, shape => shape.Geometry is RectangleGeometry
            && shape.Pen?.Brush == Brushes.White);
        Assert.Null(outline.Brush);
        var fill = Assert.Single(shapes, shape => shape.Geometry is RectangleGeometry && shape.Brush == Brushes.HotPink);
        Assert.Equal(10, fill.Geometry.Bounds.Width);
        Assert.Equal(fill.Geometry.Bounds.X + fill.Geometry.Bounds.Width / 2, outline.Geometry.Bounds.X + outline.Geometry.Bounds.Width / 2, 8);
        var bounds = outline.Geometry.Bounds;
        Assert.Equal(selected, plot.PointAt(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)));

        plot.FillHighlightMarkers = false;
        plot.BestPosition = selected;
        plot.Visits = plot.Visits.Record(new Point(18, 10)).Record(selected);
        plot.UpdateLayout();
        var cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        var hot = Assert.Single(cells, s => (s.Brush as SolidColorBrush)?.Color == Colors.Red);
        Assert.Single(cells, s => (s.Brush as SolidColorBrush)?.Color == Colors.Blue);
        Assert.Equal(selected, plot.PointAt(new Point(hot.Bounds.X + hot.Bounds.Width / 2, hot.Bounds.Y + hot.Bounds.Height / 2)));
        Assert.Contains(cells, s => s.Pen?.Brush == Brushes.HotPink && s.Brush is null);
        Assert.Contains(cells, s => s.Pen?.Brush == Brushes.LimeGreen && s.Brush is null);
        Assert.Contains(cells, s => s.Pen?.Brush == Brushes.White && s.Brush is null);

        var coincident = CatalogNetwork.LoadForWidth(8).Where(network => network.Position == new Point(19, 6)).ToArray();
        Assert.Equal(2, coincident.Length);
        plot.BestKnownNetworks = coincident;
        plot.BestPosition = coincident[0].Position;
        plot.UpdateLayout();
        var catalogMarkers = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Pen?.DashStyle == DashStyles.Dash).ToArray();
        Assert.Equal(coincident.Select(n => n.VariantBrush), catalogMarkers.Select(s => s.Pen.Brush));
        Assert.Equal(catalogMarkers[0].Geometry.Bounds, catalogMarkers[1].Geometry.Bounds);
        Assert.All(catalogMarkers, marker => Assert.Equal(10, marker.Bounds.Width, 8));
        plot.BestKnownNetworks = [];
        plot.BestPosition = null;
        plot.ShowCurrent = false;
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        Assert.DoesNotContain(cells, s => s.Pen?.DashStyle == DashStyles.Dash || s.Pen?.Brush == Brushes.HotPink || s.Pen?.Brush == Brushes.LimeGreen);
        Assert.Contains(cells, s => (s.Brush as SolidColorBrush)?.Color == Colors.Red);
    });

    [Fact]
    public void Evolution_plot_hit_tests_empty_cells_without_moving_the_lattice() => OnSta(() =>
    {
        var plot = new EvolutionPlot { Visits = EvolutionVisits.Empty.Record(new Point(17, 10)) };
        plot.Measure(new Size(400, 176));
        plot.Arrange(new Rect(0, 0, 400, 176));
        plot.UpdateLayout();
        // Offset from both the occupied center and the background lattice dots.
        var click = new Point(215, 88);
        var selected = plot.PointAt(click);
        Assert.NotNull(selected);
        Assert.DoesNotContain(selected.Value, plot.Visits.Counts.Keys);
        Assert.Same(plot, VisualTreeHelper.HitTest(plot, click)?.VisualHit);
        plot.SelectedPosition = selected;
        plot.UpdateLayout();
        Assert.Equal(selected, plot.PointAt(click));
        Assert.Null(plot.PointAt(new Point(-1, -1)));
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
        Assert.Equal(5, cells.Length); // Four frequency cells and the current annotation.
        IEnumerable<Point> Centers(Color fill) => cells.Where(s => (s.Brush as SolidColorBrush)?.Color == fill).Select(s =>
        {
            var rect = s.Geometry.Bounds;
            return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        });
        void AssertColorAt(Point point, Color fill) => Assert.Contains(Centers(fill), center => (center - point).Length < 1e-8);
        var start = Centers(Colors.Red).Single(); // Three visits.
        Assert.Equal(new Point(17, 10), plot.PointAt(start));
        Assert.Null(plot.PointAt(new Point(-1, -1)));
        var right = Centers(Colors.Blue).Single(center => plot.PointAt(center) == new Point(18, 10)); // One visit.
        var up = Centers(Color.FromRgb(135, 255, 0)).Single(); // log(2) / log(3), between green and yellow.
        var far = Centers(Colors.HotPink).Single();
        var unit = right.X - start.X;
        Assert.All(cells.Where(cell => cell.Brush != Brushes.HotPink), cell =>
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
        AssertColorAt(start, Colors.Lime);
        AssertColorAt(far, Colors.Red);
        AssertColorAt(up, Colors.HotPink);

        plot.BestPosition = new Point(17, 10);
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        AssertColorAt(start, Colors.LimeGreen);
        AssertColorAt(up, Colors.HotPink);

        plot.BestPosition = plot.Visits.Current;
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        AssertColorAt(up, Colors.HotPink);
        AssertColorAt(start, Colors.Lime);
        var outline = Assert.Single(cells, s => s.Brush is null && (s.Pen?.Brush as SolidColorBrush)?.Color == Colors.LimeGreen);
        var rect = outline.Geometry.Bounds;
        Assert.Equal(up.X, rect.X + rect.Width / 2, 8);
        Assert.Equal(up.Y, rect.Y + rect.Height / 2, 8);
        Assert.Equal(10, rect.Width + outline.Pen.Thickness, 8);

        // The cold end follows the observed minimum, including when every cell has been revisited.
        plot.BestPosition = null;
        plot.Visits = plot.Visits.Record(new(18, 10)).Record(new(17, 11));
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        AssertColorAt(right, Colors.Blue); // Two visits, now the minimum.
        AssertColorAt(far, Colors.Red);

        var uniform = EvolutionVisits.Empty;
        foreach (var point in new Point[] { new(17, 10), new(18, 10), new(17, 11), new(farX, farY) })
            uniform = uniform.Record(point).Record(point);
        plot.Visits = uniform;
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        Assert.Equal(4, cells.Count(c => (c.Brush as SolidColorBrush)?.Color == Colors.Blue));
        AssertColorAt(far, Colors.HotPink);
        Assert.Equal(new[] { Colors.Blue, Colors.Cyan, Colors.Lime, Colors.Yellow, Colors.Red },
            EvolutionPlot.FrequencyLegend.GradientStops.Select(stop => stop.Color));

        // Filtering out the extreme point changes the heatmap, but retains the full-data lattice.
        plot.AllBufferBounds = uniform.Bounds;
        plot.Visits = EvolutionVisits.Empty.Record(new(17, 10)).Record(new(17, 11));
        plot.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        Assert.Equal(3, cells.Length);
        AssertColorAt(start, Colors.Red); // The maximum visible count is one.
        AssertColorAt(up, Colors.HotPink);
        Assert.Equal(unit, cells[0].Geometry.Bounds.Width, 8);
        Assert.Equal(new Point(farX, farY), plot.PointAt(far));

        plot.Visits = EvolutionVisits.Empty.Record(new(17, 10)).Record(new(farX, farY)).Record(new(farX, farY));
        var viewport = new Viewbox { Child = plot };
        viewport.Measure(new Size(220, 120));
        viewport.Arrange(new Rect(0, 0, 220, 120));
        viewport.UpdateLayout();
        cells = Shapes(VisualTreeHelper.GetDrawing(plot)).Where(s => s.Geometry is RectangleGeometry).ToArray();
        foreach (var (color, point) in new[] { (Colors.Blue, new Point(17, 10)), (Colors.HotPink, new Point(farX, farY)) })
        {
            var localCenter = Centers(color).Single();
            var click = plot.TranslatePoint(localCenter, viewport);
            Assert.InRange(click.X, 0, 220);
            Assert.InRange(click.Y, 0, 120);
            Assert.Equal(point, plot.PointAt(viewport.TranslatePoint(click, plot)));
        }
    });

    private static void CompleteTest(RandomNetworksPageViewModel vm) => CompleteRun(vm.RunTestAsync);

    [Theory]
    [InlineData(4, null)]
    [InlineData(10, null)]
    [InlineData(8, 1)]
    [InlineData(17, 513)]
    [InlineData(64, 1025)]
    public void Bit512_evaluation_matches_failure_and_swap_counts_without_counterexamples(int width, int? samples)
    {
        var token = TestContext.Current.CancellationToken;
        var inputs = new PreparedNetworkCheck(width, samples, [], useSimd: true, token);
        var network = new NetworkDefinition("comparison", RandomNetwork.generate("msce", width, 12, Guid.Empty));
        var expected = NetworkCheck.Run(network, samples, token);
        var suffix = Sortopia.Sorting.Networks.sorter.create(Guid.NewGuid(), width, network.Comparators);
        var actual = inputs.EvaluateSuffix(suffix, token);
        Assert.Equal(expected.Tested, actual.Tested);
        Assert.Equal(expected.Exhaustive, actual.Exhaustive);
        Assert.Equal(expected.Failed, actual.Failed);
        Assert.Null(actual.Counterexample);
        Assert.Equal(expected.CeUses.ToArray(), actual.CeUses.ToArray());
        Assert.Throws<OperationCanceledException>(() => inputs.EvaluateSuffix(suffix, new CancellationToken(true)));
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
            CheckCatalogControls();
            CheckRandomEducationControls();
            CheckFirstGenerateFromHome(deferSelection: false);
            CheckFirstGenerateFromHome(deferSelection: true);
            CheckGraphBufferTiles();
            CheckSgdControls();
            CheckSgdPrefixControls();
            CheckThreePaneWorkspace();
            CheckWorkspaceHierarchy();
            CheckWorkspaceParameterInheritance();
            CheckWorkspaceParameterSetPreviews();
            CheckWorkspaceFunctionCatalogs();
            CheckWorkspaceRunExecution();
        }
        finally { app.Shutdown(); }
    });

    private static void CheckWorkspaceRunExecution()
    {
        var temporary = System.IO.Directory.CreateTempSubdirectory("Sortopia-execution-view-");
        try
        {
            string? openedFolder = null; Guid? openedRun = null;
            var vm = WorkspaceRunExecutionTests.Create(temporary.FullName, true, (folder, id) => { openedFolder = folder; openedRun = id; return Task.CompletedTask; });
            WorkspaceParameterSetsPreviewTests.AddSpan(vm.Selected!, "SortableDataFormat", false, "Bools", "Ints", "BitVector512");
            WorkspaceParameterSetsPreviewTests.AddSpan(vm.Selected!, "SortableDataFormat", true, "Bools", "Bools");
            WorkspaceRunExecutionTests.SupplyQuerySpans(vm); vm.SelectConfigurationItem("execution");
            var page = new Sortopia.WPF.Views.Pages.Workspace.WorkspacePage { DataContext = vm };
            void Layout(int width = 1000)
            {
                page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                page.Measure(new Size(width, 1000)); page.Arrange(new Rect(0, 0, width, 1000)); page.UpdateLayout();
            }
            Layout();
            Assert.Null(page.FindName("ExecuteRunButton")); Assert.Null(page.FindName("RunParameterExecutionPanel"));
            var button = (Button)page.FindName("GenerateRunParametersButton");
            var navigate = (Button)page.FindName("ExecutePageButton");
            Assert.False(button.IsEnabled); Assert.False(navigate.IsEnabled);
            CompleteRun(vm.SaveWorkspaceAsync); WorkspaceRunExecutionTests.SelectRun(vm);
            CompleteRun(() => WorkspaceRunExecutionTests.ExecuteSourceAsync(vm)); Layout();
            Assert.True(button.IsEnabled); Assert.True(navigate.IsEnabled);
            Assert.Same(vm.GenerateRunParametersCommand, button.Command); Assert.Same(vm.ExecutePageCommand, navigate.Command);
            CompleteRun(async () =>
            {
                button.Command.Execute(null); Assert.True(vm.IsBusy);
                Assert.False(vm.GenerateRunParametersCommand.CanExecute(null)); Assert.False(vm.ExecutePageCommand.CanExecute(null));
                Assert.True(vm.CancelCommand.CanExecute(null)); Assert.False(await vm.TryLeaveAsync());
                await vm.GenerateRunParametersAsync(); // A second click must not start another generation.
                while (vm.IsBusy) await Task.Delay(10, TestContext.Current.CancellationToken);
            });
            Assert.Equal(Sortopia.Functions.runExecutionStatus.Completed, vm.SelectedExecution!.Report!.Status);
            Assert.Equal(2, vm.SelectedExecution.Report.Setup.Length); Assert.Empty(vm.SelectedExecution.Report.Results);
            Assert.False(vm.HasUnsavedChanges); Layout();
            Assert.Contains("6 results parameter files", ((TextBlock)page.FindName("RunExecutionSummary")).Text);
            navigate.Command.Execute(null); Assert.Equal(vm.RunParametersFolder, openedFolder); Assert.Equal(vm.Selected!.Id, openedRun);
            var outputs = (Expander)page.FindName("RunOutputsExpander"); Assert.Equal(Visibility.Visible, outputs.Visibility);
            outputs.IsExpanded = true;
            foreach (var width in new[] { 1000, 650 })
            {
                Layout(width);
                var viewport = (ScrollViewer)page.FindName("EntityInspectorViewport"); viewport.ScrollToTop(); Layout(width);
                Assert.Equal(0, viewport.ScrollableWidth);
            }
            var badFormat = vm.Selected.Editor!.SetupParameters.Single(row => row.Key == "SortableDataFormat");
            badFormat.Values[0].Text = "invalid"; Layout();
            Assert.False(vm.SaveWorkspaceCommand.CanExecute(null)); Assert.False(button.IsEnabled);
            Assert.True(navigate.IsEnabled); // Existing saved parameter files remain available despite this draft.
            Assert.Contains("SortableDataFormat", vm.SaveHint);
            badFormat.Values[0].Text = "Bools"; CompleteRun(vm.SaveWorkspaceAsync); WorkspaceRunExecutionTests.SelectRun(vm); Layout();
            Assert.True(button.IsEnabled);
        }
        finally { temporary.Delete(recursive: true); }
    }
    private static void CheckWorkspaceFunctionCatalogs()
    {
        var temporary = System.IO.Directory.CreateTempSubdirectory("Sortopia-function-picker-view-");
        try
        {
            var vm = new WorkspacePageViewModel(() => { }, _ => temporary.FullName,
                new Sortopia.WPF.Settings.WorkspaceFolderSettings(System.IO.Path.Combine(temporary.FullName, "setting.txt")));
            vm.BrowseWorkspaceFolderCommand.Execute(null);
            vm.AddBranchModuleCommand.Execute(null);
            var module = vm.Selected!;
            module.Name = "Catalog defaults";
            var page = new Sortopia.WPF.Views.Pages.Workspace.WorkspacePage { DataContext = vm };
            void Layout(int width = 1000)
            {
                page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                page.Measure(new Size(width, 1000)); page.Arrange(new Rect(0, 0, width, 1000)); page.UpdateLayout();
            }
            Layout();
            var fields = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor)page.FindName("ConfigurationFields");
            var resultsEditor = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceOverrideEditor)FindWorkspaceField(fields, "ResultsFunctionOverrideEditor");
            var setupEditor = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceOverrideEditor)FindWorkspaceField(fields, "SetupFunctionOverrideEditor");
            var results = (Sortopia.WPF.Views.Pages.Workspace.FunctionNameSelector)resultsEditor.FindName("FunctionNameInput");
            var setup = (Sortopia.WPF.Views.Pages.Workspace.FunctionNameSelector)setupEditor.FindName("FunctionNameInput");
            void Choose(Sortopia.WPF.Views.Pages.Workspace.WorkspaceOverrideEditor editor, string name)
            {
                ((CheckBox)editor.FindName("OverrideToggle")).SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
                Layout();
                var picker = (Sortopia.WPF.Views.Pages.Workspace.FunctionNameSelector)editor.FindName("FunctionNameInput");
                Assert.True(picker.IsEnabled);
                picker.SetCurrentValue(Sortopia.WPF.Views.Pages.Workspace.FunctionNameSelector.SelectedFunctionProperty, name);
                Layout();
                Assert.Equal(name, picker.SelectedFunction);
            }
            Assert.Equal(Visibility.Collapsed, ((TextBox)resultsEditor.FindName("OverrideValueInput")).Visibility);
            Assert.False(results.IsEnabled);
            Assert.Empty(results.Choices);
            Assert.Equal(Visibility.Visible, ((Expander)fields.FindName("FunctionsSection")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)fields.FindName("SetupFunctionItem")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)fields.FindName("ResultsFunctionItem")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((Expander)fields.FindName("ResultsDefaultsSection")).Visibility);
            Assert.DoesNotContain(vm.InspectorItems, item => item.Key is "resultsDefaults" or "setupOutputs");
            void Snapshot(string name)
            {
                Layout(1400);
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap(1400, 1000, 96, 96, PixelFormats.Pbgra32);
                image.Render(page);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, name + ".png"));
                encoder.Save(stream);
            }
            Snapshot("workspace-module-functions-empty");
            Assert.Equal(Sortopia.Functions.SetupFunctionCatalog.functions.Keys, setup.Choices);
            Choose(setupEditor, Sortopia.Functions.SetupFunctionNames.standardTests);
            Assert.DoesNotContain(Sortopia.Functions.ResultsFunctionNames.sorterEval, results.Choices);
            Choose(resultsEditor, Sortopia.Functions.ResultsFunctionNames.mergeSortableTests);
            Assert.Equal(Sortopia.Functions.ResultsFunctionNames.mergeSortableTests, module.Editor!.ResultsFunctionName);
            Assert.Equal(Sortopia.Functions.SetupFunctionNames.standardTests, module.Editor.SetupFunctionName);
            Assert.Contains(vm.InspectorItems, item => item.Key == "resultsDefaults");
            Snapshot("workspace-module-functions-selected");
            module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
            vm.SelectConfigurationItem("resultsDefaults"); Layout();
            Assert.Equal(Visibility.Collapsed, ((Expander)fields.FindName("FunctionsSection")).Visibility);
            Assert.Equal(Visibility.Visible, ((Expander)fields.FindName("ResultsDefaultsSection")).Visibility);
            Snapshot("workspace-module-function-defaults");
            vm.SelectConfigurationItem("setupFunction");
            Choose(setupEditor, Sortopia.Functions.SetupFunctionNames.evaluationStandardMssi);
            Choose(resultsEditor, Sortopia.Functions.ResultsFunctionNames.sorterEval);
            Choose(setupEditor, Sortopia.Functions.SetupFunctionNames.standardTests);
            Assert.False(module.Editor.FunctionsReady);
            Assert.Equal(Sortopia.Functions.ResultsFunctionNames.sorterEval, module.Editor.ResultsFunctionName);
            Assert.DoesNotContain(vm.InspectorItems, item => item.Key == "resultsDefaults");
            Choose(resultsEditor, Sortopia.Functions.ResultsFunctionNames.mergeSortableTests);
            module.Editor.LoadProject(RunExecutionTests.Settings(true));
            vm.AddProjectCommand.Execute(null);
            var project = vm.Selected!;
            Layout();
            Assert.False(results.IsEnabled);
            Assert.False(setup.IsEnabled);
            Assert.Equal(Sortopia.Functions.ResultsFunctionNames.mergeSortableTests, results.SelectedFunction);
            Assert.Equal(Sortopia.Functions.SetupFunctionNames.standardTests, setup.SelectedFunction);
            Assert.False(project.Editor!.ResultsFunctionOverride.CanOverride);
            Assert.False(project.Editor.SetupFunctionOverride.CanOverride);
            vm.AddRunCommand.Execute(null);
            var run = vm.Selected!;
            Layout();
            Assert.False(results.IsEnabled);
            Assert.Equal(Sortopia.Functions.ResultsFunctionNames.mergeSortableTests, results.SelectedFunction);
            Assert.False(setup.IsEnabled);
            Assert.Equal(Sortopia.Functions.SetupFunctionNames.standardTests, project.Editor!.SetupFunctionName);
            Assert.Equal(Sortopia.Functions.SetupFunctionNames.standardTests, run.Editor!.SetupFunctionName);
            ((CheckBox)setupEditor.FindName("OverrideToggle")).SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
            Layout();
            Assert.Equal(Sortopia.Functions.SetupFunctionNames.standardTests, setup.SelectedFunction);
            Assert.False(run.Editor.SetupFunctionOverride.CanOverride);
            vm.SelectConfigurationItem("setupFunction");
            run.Editor.Sections.Description = run.Editor.Sections.ResultsDefaults = run.Editor.Sections.ResultsSpans = run.Editor.Sections.ResultsPreview = false;
            run.Editor.Sections.SetupDefaults = run.Editor.Sections.SetupSpans = run.Editor.Sections.SetupPreview = false;
            foreach (var width in new[] { 1000, 640 })
            {
                Layout(width);
                var viewport = (ScrollViewer)page.FindName("EntityInspectorViewport");
                viewport.ScrollToVerticalOffset(viewport.VerticalOffset + resultsEditor.TranslatePoint(new Point(0, 0), viewport).Y - 30);
                Layout(width);
                Assert.Equal(0, viewport.ScrollableWidth);
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 1000, 96, 96, PixelFormats.Pbgra32);
                image.Render(page);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"workspace-function-catalogs-{width}.png"));
                encoder.Save(stream);
            }
            vm.SelectConfigurationItem("resultsDefaults");
            run.Editor.Sections.ResultsDefaults = true;
            Layout();
            Assert.Null(fields.FindName("MoveDefaultToSpanButton"));
            Assert.Null(fields.FindName("MoveResultsDefaultsToSpansButton"));
            Assert.Null(fields.FindName("MoveSetupDefaultsToSpansButton"));
            var originalDefaults = run.Editor.InheritedOutputDefaults.Select(value => (value.Key, value.Value)).ToArray();
            run.Editor.AddMissingResultsParametersCommand.Execute(null); Layout();
            Assert.Empty(run.Editor.ResultsParameters);
            Assert.Equal(originalDefaults, run.Editor.InheritedOutputDefaults.Select(value => (value.Key, value.Value)));
            // Explicit span fixture setup is separate from the UI's default controls.
            WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false);
            vm.SelectConfigurationItem("resultsSpans"); run.Editor.Sections.ResultsSpans = true; Layout();
            Assert.Empty(run.Editor.ResultsParametersError);
            var spanImage = new System.Windows.Media.Imaging.RenderTargetBitmap(1000, 1000, 96, 96, PixelFormats.Pbgra32);
            spanImage.Render(page);
            var spanEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            spanEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(spanImage));
            using (var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "workspace-explicit-query-spans.png"))) spanEncoder.Save(stream);
            WorkspaceRunExecutionTests.SupplyQuerySpans(vm);
            CompleteRun(vm.SaveWorkspaceAsync);
            Assert.Empty(vm.Error);
            vm.Selected = vm.Roots[0].Children[0].Children[0];
            Layout();
            Assert.True(vm.Selected.IsSealed);
            Assert.False(results.IsEnabled);
            Assert.False(setup.IsEnabled);
            Assert.Equal(Sortopia.Functions.ResultsFunctionNames.mergeSortableTests, results.SelectedFunction);
        }
        finally { temporary.Delete(recursive: true); }
    }

    private static void CheckWorkspaceParameterSetPreviews()
    {
        foreach (var branch in new[] { false, true })
        {
            var temporary = System.IO.Directory.CreateTempSubdirectory("Sortopia-parameter-preview-view-");
            try
            {
                var vm = new WorkspacePageViewModel(() => { }, _ => temporary.FullName,
                    new Sortopia.WPF.Settings.WorkspaceFolderSettings(System.IO.Path.Combine(temporary.FullName, "setting.txt")));
                vm.BrowseWorkspaceFolderCommand.Execute(null);
                vm.Roots[0].Name = "GuiSpace";
                var (_, module) = WorkspaceParameterInheritanceTests.Hierarchy(vm, branch);
                module.Editor!.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.uniformMssiSorterModels;
                module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
                if (branch) { module.Editor.SetupFunctionName = Sortopia.Functions.SetupFunctionNames.uniformMssiSorterModels; module.Editor.AddRequiredSetupDefaultsCommand.Execute(null); }
                var page = new Sortopia.WPF.Views.Pages.Workspace.WorkspacePage { DataContext = vm };
                void Layout(int width = 1000, int height = 1000)
                {
                    page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    page.Measure(new Size(width, height)); page.Arrange(new Rect(0, 0, width, height)); page.UpdateLayout();
                }
                Layout();
                var fields = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor)page.FindName("ConfigurationFields");
                var settingsSection = (Expander)fields.FindName("FunctionsSection");
                var settingsPeer = (IExpandCollapseProvider)new ExpanderAutomationPeer(settingsSection).GetPattern(PatternInterface.ExpandCollapse)!;
                settingsPeer.Collapse(); Layout();
                Assert.False(settingsSection.IsExpanded);
                vm.AddProjectCommand.Execute(null);
                var project = vm.Selected!;
                Layout();
                Assert.True(settingsSection.IsExpanded);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)((Expander)FindWorkspaceField(fields, "ResultsPreviewSection")).Parent).Visibility);
                vm.Selected = module; Layout();
                Assert.True(settingsSection.IsExpanded); // Returning to the selected setting opens its editor.
                vm.Selected = project;
                vm.AddRunCommand.Execute(null);
                var run = vm.Selected!;
                var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "StageLength", false, "1", "2", "3");
                if (branch) WorkspaceParameterSetsPreviewTests.AddSpan(run, "StageLength", true, "4", "5");
                WorkspaceRunExecutionTests.SupplyQuerySpans(vm);
                var sections = run.Editor!.Sections;
                sections.Description = sections.ResultsSettings = sections.ResultsDefaults = sections.ResultsSpans = false;
                sections.SetupSettings = sections.SetupDefaults = sections.SetupSpans = false;
                Layout();
                var results = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceParameterSetsPreview)FindWorkspaceField(fields, "ResultsSetsPreview");
                var setup = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceParameterSetsPreview)FindWorkspaceField(fields, "SetupSetsPreview");
                var table = (DataGrid)results.FindName("ParameterSetsTable");
                Assert.Equal(3, table.Items.Count);
                Assert.Equal(9, table.Columns.Count);
                var channelColumn = table.Columns.Single(column => column.Header.ToString()!.Contains("ChannelCount"));
                Assert.Contains("run span", channelColumn.Header.ToString());
                Assert.Contains("run span", table.Columns.Single(column => column.Header.ToString()!.Contains("StageLength")).Header.ToString());
                Assert.Equal("16", ((TextBlock)channelColumn.GetCellContent(table.Items[0])).Text);
                Assert.Equal(branch ? Visibility.Visible : Visibility.Collapsed, ((FrameworkElement)FindWorkspaceField(fields, "SetupFields")).Visibility);
                if (branch) Assert.Equal(2, ((DataGrid)setup.FindName("ParameterSetsTable")).Items.Count);
                var viewport = (ScrollViewer)page.FindName("EntityInspectorViewport");
                foreach (var width in new[] { 1000, 640 })
                {
                    Layout(width);
                    var previewSection = (Expander)FindWorkspaceField(fields, "ResultsPreviewSection");
                    viewport.ScrollToVerticalOffset(viewport.VerticalOffset + previewSection.TranslatePoint(new Point(0, 0), viewport).Y);
                    Layout(width);
                    Assert.Equal(0, viewport.ScrollableWidth);
                    var image = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 1000, 96, 96, PixelFormats.Pbgra32);
                    image.Render(page);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"workspace-parameter-sets-{(branch ? "branch" : "root")}-{width}.png"));
                    encoder.Save(stream);
                }
                span.Values[0].Text = "invalid";
                Layout();
                Assert.Equal(Visibility.Collapsed, table.Visibility);
                Assert.NotEmpty(((TextBlock)results.FindName("PreviewError")).Text);
                Assert.NotEmpty(((Expander)FindWorkspaceField(fields, "ResultsSpansSection")).Tag.ToString()!);
                Assert.True(((Expander)FindWorkspaceField(fields, "ResultsSpansSection")).IsExpanded);
                if (branch) Assert.Equal(2, ((DataGrid)setup.FindName("ParameterSetsTable")).Items.Count);
                span.Values[0].Text = "6";
                Layout();
                Assert.Equal(Visibility.Visible, table.Visibility);
                Assert.Equal("6", ((TextBlock)table.Columns.Single(column => column.Header.ToString()!.Contains("StageLength")).GetCellContent(table.Items[0])).Text);
                var previewExpander = (Expander)FindWorkspaceField(fields, "ResultsPreviewSection");
                var previewPeer = (IExpandCollapseProvider)new ExpanderAutomationPeer(previewExpander).GetPattern(PatternInterface.ExpandCollapse)!;
                previewPeer.Collapse(); Layout();
                Assert.False(previewExpander.IsExpanded);
                var collapsedHeight = previewExpander.ActualHeight;
                previewPeer.Expand(); Layout();
                Assert.True(previewExpander.IsExpanded);
                Assert.True(previewExpander.ActualHeight > collapsedHeight);
            }
            finally { temporary.Delete(recursive: true); }
        }
    }

    private static void CheckWorkspaceParameterInheritance()
    {
        var temporary = System.IO.Directory.CreateTempSubdirectory("Sortopia-inherited-defaults-view-");
        try
        {
            var vm = new WorkspacePageViewModel(() => { }, _ => temporary.FullName,
                new Sortopia.WPF.Settings.WorkspaceFolderSettings(System.IO.Path.Combine(temporary.FullName, "setting.txt")));
            vm.BrowseWorkspaceFolderCommand.Execute(null);
            vm.Roots[0].Name = "GuiSpace";
            var (parent, child) = WorkspaceParameterInheritanceTests.Hierarchy(vm, false);
            parent.Editor!.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.uniformMssiSorterModels;
            var page = new Sortopia.WPF.Views.Pages.Workspace.WorkspacePage { DataContext = vm };
            void Layout(int width = 1000)
            {
                page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                page.Measure(new Size(width, 800)); page.Arrange(new Rect(0, 0, width, 800)); page.UpdateLayout();
            }
            Layout();
            var fields = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor)page.FindName("ConfigurationFields");
            var inherited = (ItemsControl)FindWorkspaceField(fields, "InheritedResultsParameterDefaultsList");
            var locals = (ItemsControl)FindWorkspaceField(fields, "ResultsParameterDefaultsList");
            Assert.Single(inherited.Items);
            Assert.Single(locals.Items);
            Assert.Equal("ChannelCount", ((WorkspaceParameterBindingPreview)inherited.Items[0]).Key);
            Assert.Equal("StageLength", ((WorkspaceParameterBinding)locals.Items[0]).Key);
            var row = (ContentPresenter)inherited.ItemContainerGenerator.ContainerFromIndex(0);
            var source = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceValueSource)inherited.ItemTemplate.FindName("ParameterValueSource", row);
            Assert.Equal(Visibility.Visible, ((TextBlock)source.FindName("SourceLinkText")).Visibility);
            var link = (System.Windows.Documents.Hyperlink)source.FindName("SourceLink");
            Assert.Same(parent.Editor, link.CommandParameter);
            ((IInvokeProvider)new HyperlinkAutomationPeer(link).GetPattern(PatternInterface.Invoke)!).Invoke();
            Layout();
            Assert.Same(parent, vm.Selected);
            Assert.Same(parent, ((TreeView)page.FindName("WorkspaceTree")).SelectedItem);
            vm.Selected = child;
            Layout();
            var viewport = (ScrollViewer)page.FindName("EntityInspectorViewport");
            foreach (var width in new[] { 1000, 640 })
            {
                Layout(width);
                viewport.ScrollToVerticalOffset(viewport.VerticalOffset + inherited.TranslatePoint(new Point(0, 0), viewport).Y - 40);
                Layout(width);
                Assert.Equal(0, viewport.ScrollableWidth);
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 800, 96, 96, PixelFormats.Pbgra32);
                image.Render(page);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"workspace-parameter-inheritance-{width}.png"));
                encoder.Save(stream);
            }
            row = (ContentPresenter)inherited.ItemContainerGenerator.ContainerFromIndex(0);
            var overrideButton = (Button)inherited.ItemTemplate.FindName("OverrideParameterButton", row);
            Assert.True(overrideButton.IsEnabled);
            ((IInvokeProvider)new ButtonAutomationPeer(overrideButton).GetPattern(PatternInterface.Invoke)!).Invoke();
            Layout();
            Assert.Empty(inherited.Items);
            Assert.Equal(2, locals.Items.Count);
            var overridden = child.Editor!.ResultsParameterDefaults.Single(value => value.Key == "ChannelCount");
            Assert.Equal("16", overridden.Value.Text);
            var localRow = (ContentPresenter)locals.ItemContainerGenerator.ContainerFromItem(overridden);
            var localSource = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceValueSource)locals.ItemTemplate.FindName("LocalParameterValueSource", localRow);
            Assert.Same(parent.Editor, ((System.Windows.Documents.Hyperlink)localSource.FindName("SourceLink")).CommandParameter);
            overridden.RemoveCommand.Execute(null);
            Layout();
            Assert.Single(inherited.Items);
            Assert.Single(locals.Items);
        }
        finally { temporary.Delete(recursive: true); }
    }

    private static void CheckThreePaneWorkspace()
    {
        var temporary = System.IO.Directory.CreateTempSubdirectory("Sortopia-three-pane-");
        try
        {
            var settings = new Sortopia.WPF.Settings.WorkspaceFolderSettings(System.IO.Path.Combine(temporary.FullName, "settings.txt")); settings.Save(temporary.FullName);
            var vm = new WorkspacePageViewModel(() => { }, _ => null, settings);
            var (_, module) = WorkspaceParameterInheritanceTests.Hierarchy(vm, true);
            module.Editor!.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.uniformMssiSorterModels; module.Editor.SetupFunctionName = Sortopia.Functions.SetupFunctionNames.uniformMssiSorterModels;
            module.Editor.AddRequiredResultsDefaultsCommand.Execute(null); module.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
            vm.AddProjectCommand.Execute(null); vm.Selected!.Name = "Single-stage evaluation";
            vm.AddRunCommand.Execute(null); var run = vm.Selected!; run.Name = "Stage sweep";
            var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "StageLength", false, "1", "2", "3");
            WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", true, "8", "16");
            WorkspaceRunExecutionTests.SupplyQuerySpans(vm);
            var page = new Sortopia.WPF.Views.Pages.Workspace.WorkspacePage { DataContext = vm };
            void Layout(int width = 1440, int height = 980)
            {
                page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                page.Measure(new Size(width, height)); page.Arrange(new Rect(0, 0, width, height)); page.UpdateLayout();
            }
            Layout();
            var panes = (Grid)page.FindName("WorkspacePanes");
            Assert.Equal(5, panes.ColumnDefinitions.Count);
            var summary = (Border)page.FindName("ConfigurationSummaryPanel");
            IEnumerable<DependencyObject> Descendants(DependencyObject node)
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                { var child = VisualTreeHelper.GetChild(node, index); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
            }
            Assert.DoesNotContain(Descendants(summary), element => element is TextBox or ComboBox or CheckBox);
            var items = (ItemsControl)page.FindName("ConfigurationItems");
            void SelectItem(string key)
            {
                var item = vm.InspectorItems.Single(item => item.Key == key);
                var row = (ContentPresenter)items.ItemContainerGenerator.ContainerFromItem(item);
                var link = (System.Windows.Documents.Hyperlink)items.ItemTemplate.FindName("ConfigurationItemLink", row);
                Assert.True(link.Focusable);
                ((IInvokeProvider)new HyperlinkAutomationPeer(link).GetPattern(PatternInterface.Invoke)!).Invoke(); Layout();
                Assert.Equal(key, vm.ActiveItemKey); Assert.True(item.IsActive);
            }
            SelectItem("resultsSpans");
            var fields = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor)page.FindName("ConfigurationFields");
            Assert.Equal(Visibility.Visible, ((Expander)fields.FindName("ResultsSpansSection")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((Expander)fields.FindName("ResultsDefaultsSection")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((Expander)fields.FindName("DescriptionSection")).Visibility);
            var list = (ItemsControl)fields.FindName("ResultsParametersList");
            var spanRow = (ContentPresenter)list.ItemContainerGenerator.ContainerFromIndex(0);
            var values = (ItemsControl)list.ItemTemplate.FindName("ParameterValues", spanRow);
            var valueRow = (ContentPresenter)values.ItemContainerGenerator.ContainerFromIndex(0);
            var valueEditor = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceParameterValueEditor)values.ItemTemplate.FindName("ParameterValueEditor", valueRow);
            var input = (TextBox)valueEditor.FindName("TypedValueInput");
            input.SetCurrentValue(TextBox.TextProperty, "-1"); Layout();
            Assert.True(vm.InspectorItems.Single(item => item.Key == "resultsSpans").HasError);
            Assert.Contains("-1", vm.InspectorItems.Single(item => item.Key == "resultsSpans").Value);
            input.SetCurrentValue(TextBox.TextProperty, "1"); Layout();
            Assert.False(vm.InspectorItems.Single(item => item.Key == "resultsSpans").HasError);
            var function = vm.InspectorItems.Single(item => item.Key == "resultsFunction");
            vm.SelectInspectorSourceCommand.Execute(function); Layout();
            Assert.Same(module, vm.Selected); Assert.Equal("resultsFunction", vm.ActiveItemKey);
            vm.Selected = run; Layout(); SelectItem("resultsSpans");
            foreach (var (name, column, delta) in new[] { ("HierarchySplitter", 0, 36.0), ("EditorSplitter", 2, -28.0) })
            {
                var splitter = (GridSplitter)page.FindName(name);
                Assert.True(splitter.Focusable); Assert.Equal(GridResizeDirection.Columns, splitter.ResizeDirection);
                var before = panes.ColumnDefinitions[column].ActualWidth;
                ((ITransformProvider)new GridSplitterAutomationPeer(splitter).GetPattern(PatternInterface.Transform)!).Move(delta, 0);
                page.UpdateLayout(); Assert.NotEqual(before, panes.ColumnDefinitions[column].ActualWidth);
            }
            foreach (var (width, height) in new[] { (1440, 980), (1000, 900), (640, 800) })
            {
                Layout(width, height);
                Assert.Equal(0, ((ScrollViewer)page.FindName("WorkspaceViewport")).ScrollableWidth);
                Assert.Equal(0, ((ScrollViewer)page.FindName("EntityInspectorViewport")).ScrollableWidth);
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(page);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var output = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"workspace-three-pane-{width}.png")); encoder.Save(output);
            }
            SelectItem("setupPreview"); Assert.Equal(Visibility.Visible, ((Expander)fields.FindName("SetupPreviewSection")).Visibility);
            SelectItem("execution"); Assert.Equal(Visibility.Visible, ((Expander)page.FindName("RunExecutionExpander")).Visibility);
        }
        finally { temporary.Delete(recursive: true); }
    }

    private static object FindWorkspaceField(Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor fields, string name)
    {
        var key = name switch
        {
            "DescriptionSection" or "DescriptionOverrideEditor" => "description",
            "ResultsSettingsSection" or "ResultsFunctionOverrideEditor" => "resultsFunction",
            "ResultsDatabaseOverrideEditor" => "resultsDatabase",
            "OutputTypesHeader" or "OutputTypesList" or "AddOutputTypeButton" => "resultsTypes",
            "ResultsQueryKeysList" => "resultsQueryKeys",
            "ResultsDefaultsSection" or "ResultsParameterDefaultsList" or "InheritedResultsParameterDefaultsList" or "AddResultsParameterDefaultButton" or "AddRequiredResultsDefaultsButton" => "resultsDefaults",
            "ResultsSpansSection" or "ResultsParametersList" or "AddRequiredResultsParametersButton" => "resultsSpans",
            "ResultsPreviewSection" or "ResultsSetsPreview" => "resultsPreview",
            "SetupSettingsSection" or "SetupFunctionOverrideEditor" => "setupFunction",
            "SetupDatabaseOverrideEditor" => "setupDatabase",
            "SetupDataTypesList" or "AddSetupDataTypeButton" => "setupOutputs",
            "SetupDefaultsSection" or "SetupParameterDefaultsList" or "InheritedSetupParameterDefaultsList" or "AddRequiredSetupDefaultsButton" => "setupDefaults",
            "SetupSpansSection" or "SetupParametersList" or "AddRequiredSetupParametersButton" => "setupSpans",
            "SetupPreviewSection" or "SetupSetsPreview" => "setupPreview",
            _ => null
        };
        DependencyObject? ancestor = fields;
        while (ancestor is not null && ancestor is not Sortopia.WPF.Views.Pages.Workspace.WorkspacePage) ancestor = VisualTreeHelper.GetParent(ancestor);
        if (key is not null && ancestor is Sortopia.WPF.Views.Pages.Workspace.WorkspacePage page && page.DataContext is WorkspacePageViewModel vm)
        {
            vm.SelectConfigurationItem(key);
            page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            page.UpdateLayout();
        }
        return fields.FindName(name);
    }

    private static void CheckWorkspaceHierarchy()
    {
        var temporary = System.IO.Directory.CreateTempSubdirectory("Sortopia-workspace-view-");
        try
        {
            string? importSource = null;
            string? openedFolder = null;
            var vm = new WorkspacePageViewModel(() => { }, _ => temporary.FullName,
                new Sortopia.WPF.Settings.WorkspaceFolderSettings(System.IO.Path.Combine(temporary.FullName, "setting.txt")),
                pickExistingFolder: _ => importSource, openFolder: path => openedFolder = path);
            vm.BrowseWorkspaceFolderCommand.Execute(null);
            var page = new Sortopia.WPF.Views.Pages.Workspace.WorkspacePage { DataContext = vm };
            void LayoutWorkspace(int width = 1000, int height = 800)
            {
                page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                page.Measure(new Size(width, height)); page.Arrange(new Rect(0, 0, width, height)); page.UpdateLayout();
            }
            void RenderWorkspaceState(string state, int width = 1000, int height = 800)
            {
                LayoutWorkspace(width, height);
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                image.Render(page);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"workspace-{state}.png"));
                encoder.Save(stream);
            }
            LayoutWorkspace();
            var addModule = (Button)page.FindName("AddExistingModuleButton");
            var addProject = (Button)page.FindName("AddExistingProjectButton");
            Assert.True(addModule.IsEnabled);
            Assert.False(addProject.IsEnabled);
            var newRootModule = (Button)page.FindName("NewRootModuleButton");
            var newBranchModule = (Button)page.FindName("NewBranchModuleButton");
            Assert.Equal(Visibility.Visible, newRootModule.Visibility);
            Assert.Equal(Visibility.Visible, newBranchModule.Visibility);
            Assert.True(newRootModule.IsEnabled);
            Assert.True(newBranchModule.IsEnabled);
            Assert.Contains("without setup", ((TextBlock)page.FindName("SelectionHint")).Text);
            var prompt = new Sortopia.WPF.Views.Pages.Workspace.WorkspaceUnsavedChangesDialog();
            Assert.True(((Button)prompt.FindName("SaveChangesButton")).IsDefault);
            Assert.True(((Button)prompt.FindName("CancelChangesButton")).IsCancel);
            Assert.Equal(WorkspaceUnsavedChoice.Cancel, prompt.Choice);
            var promptContent = (Panel)prompt.Content;
            promptContent.Background = (Brush)prompt.FindResource("Workspace.Canvas");
            promptContent.Measure(new Size(470, double.PositiveInfinity));
            var promptHeight = (int)Math.Ceiling(promptContent.DesiredSize.Height);
            promptContent.Arrange(new Rect(0, 0, 470, promptHeight));
            promptContent.UpdateLayout();
            var promptImage = new System.Windows.Media.Imaging.RenderTargetBitmap(470, promptHeight, 96, 96, PixelFormats.Pbgra32);
            promptImage.Render(promptContent);
            var promptEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            promptEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(promptImage));
            using (var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "workspace-unsaved-prompt.png"))) promptEncoder.Save(stream);
            prompt.Close();
            var newPanel = (StackPanel)page.FindName("WorkspaceNamePanel");
            var newName = (TextBox)page.FindName("WorkspaceNameInput");
            var save = (Button)page.FindName("SaveWorkspaceButton");
            var discard = (Button)page.FindName("DiscardChangesButton");
            Assert.False(discard.IsEnabled);
            var fileStatus = (TextBlock)page.FindName("WorkspaceFileStatus");
            Assert.Equal(Visibility.Visible, newPanel.Visibility);
            Assert.Contains("No workspace file", fileStatus.Text);
            newName.SetCurrentValue(TextBox.TextProperty, "");
            Assert.False(save.IsEnabled);
            newName.SetCurrentValue(TextBox.TextProperty, "Research");
            Assert.True(save.IsEnabled);
            Assert.True(discard.IsEnabled);
            Assert.Equal("Research", vm.WorkspaceName);
            RenderWorkspaceState("not-saved");
            CompleteRun(vm.SaveWorkspaceAsync);
            LayoutWorkspace();
            Assert.Empty(vm.Error);
            Assert.Equal(Visibility.Visible, newPanel.Visibility);
            Assert.False(save.IsEnabled);
            Assert.False(discard.IsEnabled);
            Assert.Contains("Saved workspace file", fileStatus.Text);
            RenderWorkspaceState("saved");
            var entityName = (TextBox)page.FindName("EntityNameInput");
            var folderName = (TextBox)page.FindName("EntityFolderNameInput");
            var fullPath = (TextBox)page.FindName("EntityFullPath");
            var openFolder = (Button)page.FindName("OpenEntityFolderButton");
            Assert.True(fullPath.IsReadOnly);
            var details = (Border)page.FindName("EntityDetailsPanel");
            Assert.Equal(Visibility.Visible, details.Visibility); // Workspace identity also uses the item editor.
            Assert.True(folderName.IsReadOnly);
            var originalFolderName = folderName.Text;
            newName.SetCurrentValue(TextBox.TextProperty, "Updated research");
            Assert.Equal("Updated research", vm.WorkspaceName);
            Assert.True(save.IsEnabled);
            Assert.Contains("Unsaved changes", fileStatus.Text);
            RenderWorkspaceState("renamed");
            CompleteRun(vm.SaveWorkspaceAsync);
            LayoutWorkspace();
            Assert.Empty(vm.Error);
            Assert.False(save.IsEnabled);
            Assert.DoesNotContain("Unsaved changes", fileStatus.Text);
            Assert.Equal(originalFolderName, folderName.Text);
            Assert.Equal("Updated research", WorkspaceFileStore.LoadDefinition(temporary.FullName).WorkspaceName);
            vm.AddModuleCommand.Execute(null);
            LayoutWorkspace();
            Assert.Equal(Visibility.Visible, details.Visibility);
            Assert.False(openFolder.IsEnabled);
            entityName.SetCurrentValue(TextBox.TextProperty, "Sorting");
            Assert.Equal("Sorting", folderName.Text);
            Assert.Equal(System.IO.Path.Combine(temporary.FullName, "Sorting"), fullPath.Text);
            fullPath.SelectAll();
            Assert.Equal(fullPath.Text, fullPath.SelectedText);
            entityName.SetCurrentValue(TextBox.TextProperty, "Sorting research");
            Assert.Equal("Sorting research", folderName.Text);
            Assert.Equal(System.IO.Path.Combine(temporary.FullName, "Sorting research"), fullPath.Text);
            entityName.SetCurrentValue(TextBox.TextProperty, "Sorting");
            CompleteRun(vm.SaveWorkspaceAsync);
            Assert.Empty(vm.Error);
            vm.Selected = vm.Roots.Single().Children.Single();
            LayoutWorkspace();
            Assert.True(openFolder.IsEnabled);
            openFolder.Command.Execute(null);
            Assert.Equal(fullPath.Text, openedFolder);
            Assert.False(vm.HasUnsavedChanges);
            vm.Selected = vm.Roots.Single();
            vm.AddModuleCommand.Execute(null);
            LayoutWorkspace();
            var newModule = vm.Selected!;
            var defaultName = newModule.Name;
            entityName.SetCurrentValue(TextBox.TextProperty, "SORTING");
            Assert.Equal(defaultName, newModule.Name);
            Assert.Equal(defaultName, newModule.FolderName);
            Assert.Equal("SORTING", entityName.Text);
            var nameError = (TextBlock)page.FindName("EntityNameError");
            Assert.Contains("saved workspace", nameError.Text);
            Assert.Equal(Visibility.Visible, nameError.Visibility);
            Assert.False(save.IsEnabled);
            RenderWorkspaceState("name-conflict");
            entityName.SetCurrentValue(TextBox.TextProperty, "Sorting 2");
            Assert.Equal("Sorting 2", folderName.Text);
            folderName.SetCurrentValue(TextBox.TextProperty, "SORTING");
            Assert.Equal("Sorting 2", newModule.FolderName);
            var folderError = (TextBlock)page.FindName("EntityFolderNameError");
            Assert.Contains("Duplicate folder name", folderError.Text);
            Assert.False(save.IsEnabled);
            RenderWorkspaceState("folder-conflict");
            folderName.SetCurrentValue(TextBox.TextProperty, "sorting-2");
            Assert.Empty(folderError.Text);
            vm.RemoveCommand.Execute(null);
            vm.Selected = vm.Roots.Single().Children.Single();
            vm.AddProjectCommand.Execute(null);
            WorkspaceHierarchyTests.ConfigureProject(vm.Selected!);
            vm.Selected!.Name = "Search";
            vm.AddRunCommand.Execute(null);
            vm.Selected!.Name = "Trial 01";
            LayoutWorkspace();
            Assert.Equal(Visibility.Collapsed, ((Button)page.FindName("NewProjectButton")).Visibility);
            Assert.Equal("Root run", vm.Selected.KindLabel);
            Assert.False(addModule.IsEnabled);
            Assert.False(addProject.IsEnabled);
            vm.Selected.Editor!.LoadRun(Sortopia.Workspaces.Serialization.RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("Search", "Trial 01")));
            LayoutWorkspace();
            Assert.Equal(Visibility.Collapsed, ((StackPanel)page.FindName("EntityFolderNamePanel")).Visibility);
            var runId = vm.Selected.Id;
            entityName.SetCurrentValue(TextBox.TextProperty, "Trial: renamed");
            Assert.Equal(runId, vm.Selected.Id);
            Assert.Equal("Trial: renamed", vm.Selected.Name);
            Assert.Equal(System.IO.Path.Combine(temporary.FullName, "Sorting", "Search", "runs", WorkspaceDefinitionFiles.Name(runId)), fullPath.Text);
            Assert.Empty(vm.Error);
            Assert.True(save.IsEnabled);
            foreach (var size in new[] { new Size(1000, 800), new Size(640, 480) })
            {
                page.Measure(size); page.Arrange(new Rect(size)); page.UpdateLayout();
                var tree = (TreeView)page.FindName("WorkspaceTree");
                Assert.Single(tree.Items);
                Assert.True(tree.ActualHeight > 0);
                Assert.True(vm.Selected?.IsRun, $"Selected entity: {vm.Selected?.KindLabel ?? "none"}");
                var editor = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor)page.FindName("ConfigurationFields");
                Assert.Equal(Visibility.Visible, editor.Visibility);
                vm.SelectConfigurationItem("resultsSpans"); page.UpdateLayout(); Assert.True(editor.ActualHeight > 50);
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                image.Render(page);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"workspace-hierarchy-{size.Width}.png"));
                encoder.Save(stream);
            }
            vm.Selected = vm.Roots.Single();
            vm.AddBranchModuleCommand.Execute(null);
            vm.Selected!.Name = "Setup and evolution";
            var branchModule = vm.Selected;
            LayoutWorkspace();
            Assert.Equal(Visibility.Collapsed, newRootModule.Visibility);
            Assert.Equal(Visibility.Visible, newBranchModule.Visibility);
            Assert.Equal("New branch project", ((Button)page.FindName("NewProjectButton")).Content);
            var fields = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor)page.FindName("ConfigurationFields");
            var beforeContract = branchModule.Editor!.ToModuleOverrides();
            branchModule.Editor.LoadProject(FunctionOutputContractTests.Combined(true));
            vm.SelectConfigurationItem("setupOutputs");
            foreach (var width in new[] { 1000, 640 })
            {
                RenderWorkspaceState($"function-output-contracts-{width}", width, 800);
                Assert.Equal(0, ((ScrollViewer)page.FindName("EntityInspectorViewport")).ScrollableWidth);
            }
            Assert.Null(FindWorkspaceField(fields, "AddSetupDataTypeButton"));
            Assert.False(branchModule.Editor.SetupDataTypesOverride.CanEditValue);
            foreach (var setup in new[] { false, true })
            {
                var rows = setup ? branchModule.Editor.SetupParameterDefaults : branchModule.Editor.ResultsParameterDefaults;
                var count = rows.Count;
                rows.Single(row => row.Key == "ChannelCount").RemoveCommand.Execute(null);
                var populate = (Button)FindWorkspaceField(fields, setup ? "AddRequiredSetupDefaultsButton" : "AddRequiredResultsDefaultsButton");
                LayoutWorkspace(); Assert.True(populate.IsEnabled);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)populate.Parent).Visibility);
                populate.Command.Execute(null); LayoutWorkspace();
                Assert.Equal(count, rows.Count); Assert.False(populate.IsEnabled);
                Assert.Equal("4", rows.Single(row => row.Key == "ChannelCount").Value.Text);
                Assert.Empty(rows.Single(row => row.Key == "ChannelCount").Error);
                RenderWorkspaceState($"required-{(setup ? "setup" : "results")}-defaults", 1000, 800);
                rows.Single(row => row.Key == "ChannelCount").Value.Text = "4";
            }
            branchModule.Editor.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.standardMssiEvaluationInputs;
            branchModule.Editor.ResultsParameterDefaultsOverride.IsOverridden = false;
            vm.SelectConfigurationItem("resultsTypes"); RenderWorkspaceState("results-output-contracts");
            Assert.Null(FindWorkspaceField(fields, "OutputTypesList"));
            branchModule.Editor.LoadModuleOverrides(beforeContract);
            LayoutWorkspace();
            var moduleDescriptionEditor = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceOverrideEditor)FindWorkspaceField(fields, "DescriptionOverrideEditor");
            var moduleDescriptionToggle = (CheckBox)moduleDescriptionEditor.FindName("OverrideToggle");
            Assert.Equal("Override on this module", moduleDescriptionToggle.Content);
            moduleDescriptionToggle.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            LayoutWorkspace();
            ((TextBox)moduleDescriptionEditor.FindName("OverrideValueInput")).SetCurrentValue(TextBox.TextProperty, "Shared description from the branch module");
            var moduleExecuteEditor = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceOverrideEditor)FindWorkspaceField(fields, "ResultsFunctionOverrideEditor");
            ((CheckBox)moduleExecuteEditor.FindName("OverrideToggle")).SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            LayoutWorkspace();
            ((TextBox)moduleExecuteEditor.FindName("OverrideValueInput")).SetCurrentValue(TextBox.TextProperty, "sortable-tests.standard");
            Assert.Equal("sortable-tests.standard", branchModule.Editor!.ResultsFunctionName);
            var defaultSource = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceValueSource)
                ((Sortopia.WPF.Views.Pages.Workspace.WorkspaceOverrideEditor)FindWorkspaceField(fields, "ResultsDatabaseOverrideEditor")).FindName("ValueSource");
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)FindWorkspaceField(fields, "ResultsDatabaseOverrideEditor")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)FindWorkspaceField(fields, "SetupDatabaseOverrideEditor")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((TextBlock)defaultSource.FindName("SourceLinkText")).Visibility);
            RenderWorkspaceState("branch-module-defaults");
            RenderWorkspaceState("module-defaults-narrow", 640, 800);
            Assert.Equal(0, ((ScrollViewer)page.FindName("EntityInspectorViewport")).ScrollableWidth);
            LayoutWorkspace();
            vm.AddProjectCommand.Execute(null);
            LayoutWorkspace();
            Assert.True(vm.HasSaveIssues);
            Assert.Equal(Visibility.Visible, ((Border)page.FindName("SaveValidationSummary")).Visibility);
            Assert.Contains("Setup function", vm.Error, StringComparison.OrdinalIgnoreCase);
            RenderWorkspaceState("save-validation");
            RenderWorkspaceState("save-validation-narrow", 640, 800);
            var invalidProject = vm.Selected!;
            invalidProject.Editor!.Sections.SetupSettings = false;
            vm.Selected = branchModule;
            LayoutWorkspace();
            var issueList = (ItemsControl)page.FindName("SaveIssuesList");
            var issueRow = (ContentPresenter)issueList.ItemContainerGenerator.ContainerFromIndex(0);
            var issueLink = (System.Windows.Documents.Hyperlink)issueList.ItemTemplate.FindName("SaveIssueLink", issueRow);
            ((IInvokeProvider)new HyperlinkAutomationPeer(issueLink).GetPattern(PatternInterface.Invoke)!).Invoke();
            LayoutWorkspace();
            Assert.Same(invalidProject, vm.Selected);
            Assert.True(((Expander)fields.FindName("FunctionsSection")).IsExpanded);
            var issueSection = (Expander)fields.FindName("FunctionsSection");
            var issueViewport = (ScrollViewer)page.FindName("EntityInspectorViewport");
            Assert.InRange(issueSection.TranslatePoint(new Point(0, 0), issueViewport).Y, -1, issueViewport.ViewportHeight);
            WorkspaceHierarchyTests.ConfigureProject(vm.Selected!);
            vm.Selected!.Name = "Evolution";
            LayoutWorkspace();
            Assert.Equal("New branch run", ((Button)page.FindName("NewRunButton")).Content);
            var projectDatabase = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceOverrideEditor)FindWorkspaceField(fields, "ResultsDatabaseOverrideEditor");
            Assert.Equal(Visibility.Visible, projectDatabase.Visibility);
            Assert.Equal(Visibility.Collapsed, ((CheckBox)projectDatabase.FindName("OverrideToggle")).Visibility);
            Assert.False(((TextBox)projectDatabase.FindName("OverrideValueInput")).IsReadOnly);
            Assert.Equal(Visibility.Collapsed, ((Button)page.FindName("NewProjectButton")).Visibility);
            var executeEditor = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceOverrideEditor)FindWorkspaceField(fields, "ResultsFunctionOverrideEditor");
            var executeInput = (TextBox)executeEditor.FindName("OverrideValueInput");
            Assert.Equal("sortable-tests.standard", executeInput.Text);
            Assert.True(executeInput.IsReadOnly);
            // Clear local project values through the actual controls and show the supplying module.
            ((CheckBox)moduleDescriptionEditor.FindName("OverrideToggle")).SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
            ((CheckBox)executeEditor.FindName("OverrideToggle")).SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
            LayoutWorkspace();
            Assert.True(executeInput.IsReadOnly);
            Assert.Equal("Inherited from Setup and evolution", vm.Selected.Editor!.ResultsFunctionOverride.SourceLabel);
            Assert.Equal(branchModule.Editor.Description, vm.Selected.Editor.Description);
            RenderWorkspaceState("branch-project-defaults");
            var branchProject = vm.Selected;
            // Project settings are constrained by the inherited catalog functions.
            branchModule.Editor!.LoadProject(FunctionOutputContractTests.Combined(true));
            branchModule.Editor.SetupFunctionName = Sortopia.Functions.SetupFunctionNames.evaluationPrefixMssi;
            branchModule.Editor.SetupParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "8";
            branchModule.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
            branchProject!.Editor!.LoadProject(new Sortopia.Workspaces.Serialization.projectSettingsDto());
            LayoutWorkspace();
            Assert.True(branchProject.Editor.FunctionsReady);
            Assert.False(branchProject.Editor.ResultsFunctionOverride.ShowCatalogPicker);
            vm.SelectConfigurationItem("resultsTypes"); LayoutWorkspace();
            Assert.Null(fields.FindName("OutputTypesList"));
            Assert.Null(fields.FindName("AddOutputTypeButton"));
            Assert.Single(branchProject.Editor.ResultsOutputContracts, row => row.Kind == "SorterSetEval");
            RenderWorkspaceState("project-fixed-output-contracts", 1400, 900);
            var sourceModule = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Source catalog", family: Sortopia.Workspaces.hierarchyKind.Branch);
            branchModule.Children.Add(sourceModule);
            sourceModule.Editor!.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.standardSortableTests;
            sourceModule.Editor.SetupFunctionName = Sortopia.Functions.SetupFunctionNames.standardTests;
            sourceModule.Editor.AddRequiredResultsDefaultsCommand.Execute(null); sourceModule.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
            var setupSource = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Tests source", family: Sortopia.Workspaces.hierarchyKind.Branch);
            sourceModule.Children.Add(setupSource);
            setupSource.Children.Add(new(WorkspaceEntityKind.Run, "Source run", family: Sortopia.Workspaces.hierarchyKind.Branch));
            WorkspaceRunExecutionTests.SupplyQuerySpans(vm);
            branchProject.Editor.SetupDataTypesOverride.IsOverridden = true;
            LayoutWorkspace();
            var setupEntries = (ItemsControl)FindWorkspaceField(fields, "SetupDataTypesList");
            var sourceEntry = branchProject.Editor.SetupDataTypes.Single(row => row.RequiresSource);
            var setupEntry = (ContentPresenter)setupEntries.ItemContainerGenerator.ContainerFromItem(sourceEntry);
            var sourcePicker = (ComboBox)setupEntries.ItemTemplate.FindName("SetupSourceProjectInput", setupEntry);
            Assert.Contains(sourcePicker.Items.Cast<WorkspaceSourceProject>(), item => item.Id == setupSource.Id);
            Assert.All(sourcePicker.Items.Cast<WorkspaceSourceProject>(), item => Assert.Contains(item.Settings.resultsDataTypes, output => output.dataType.kind == "SortableTests" && output.dataType.value == ""));
            sourcePicker.SetCurrentValue(ComboBox.SelectedValueProperty, setupSource.Id); LayoutWorkspace();
            Assert.Equal(setupSource.Id, sourceEntry.SourceProjectId);
            Assert.Null(setupEntries.ItemTemplate.FindName("SetupQueryKeysList", setupEntry));
            Assert.All(branchProject.Editor.SetupDataTypes.Where(row => row.GeneratesData), row => Assert.Empty(row.SourceProjects));
            RenderWorkspaceState("project-allowed-source-projects", 1400, 900);
            Sortopia.WPF.Views.Pages.Workspace.WorkspaceParameterValueEditor AddTypedDefault(string key)
            {
                ((Button)FindWorkspaceField(fields, "AddResultsParameterDefaultButton")).Command.Execute(null); LayoutWorkspace();
                var defaults = (ItemsControl)fields.FindName("ResultsParameterDefaultsList");
                var row = (ContentPresenter)defaults.ItemContainerGenerator.ContainerFromIndex(defaults.Items.Count - 1);
                var keyInput = (ComboBox)defaults.ItemTemplate.FindName("DefaultKeyInput", row);
                Assert.True(keyInput.IsReadOnly);
                Assert.Equal(Sortopia.Functions.ResultsFunctionCatalog.functions[Sortopia.Functions.ResultsFunctionNames.sorterEval].RequiredParameters.Select(key => key.Name).Order(), keyInput.Items.Cast<string>());
                keyInput.SetCurrentValue(ComboBox.SelectedItemProperty, key); LayoutWorkspace();
                Assert.False(((ComboBox)defaults.ItemTemplate.FindName("DefaultTypeInput", row)).IsEnabled);
                return (Sortopia.WPF.Views.Pages.Workspace.WorkspaceParameterValueEditor)defaults.ItemTemplate.FindName("DefaultValueEditor", row);
            }
            var integerEditor = AddTypedDefault("ChannelCount");
            var integerInput = (TextBox)integerEditor.FindName("TypedValueInput");
            integerInput.SetCurrentValue(TextBox.TextProperty, "0"); LayoutWorkspace();
            Assert.False(save.IsEnabled); Assert.Contains("1 or greater", vm.Error);
            integerInput.SetCurrentValue(TextBox.TextProperty, "4"); LayoutWorkspace();
            var booleanEditor = AddTypedDefault("CollectSortableTests");
            var booleanInput = (ComboBox)booleanEditor.FindName("BooleanValueInput");
            Assert.Equal(Visibility.Visible, booleanInput.Visibility);
            booleanInput.SetCurrentValue(ComboBox.SelectedItemProperty, true); LayoutWorkspace();
            Assert.True(save.IsEnabled);
            Assert.True(branchProject.Editor.ResultsParameterDefaults.Last().Value.BooleanValue);
            ((ScrollViewer)page.FindName("EntityInspectorViewport")).ScrollToBottom();
            RenderWorkspaceState("project-allowed-defaults", 1400, 900);
            vm.AddRunCommand.Execute(null);
            var branchRun = vm.Selected!; branchRun.Name = "Evolve candidate";
            LayoutWorkspace();
            Assert.Equal("Inherited functions", vm.ActiveItemTitle);
            Assert.False(branchRun.Editor!.SetupFunctionOverride.CanOverride);
            Assert.True(executeInput.IsReadOnly);
            Assert.Equal(Visibility.Visible, executeInput.Visibility);
            Assert.Equal(Sortopia.Functions.ResultsFunctionNames.sorterEval, executeInput.Text);
            Assert.False(((Sortopia.WPF.Views.Pages.Workspace.FunctionNameSelector)executeEditor.FindName("FunctionNameInput")).IsVisible);
            RenderWorkspaceState("run-inherited-functions", 1400, 900);
            var resultFunction = vm.InspectorItems.Single(item => item.Key == "resultsFunction");
            vm.SelectInspectorSourceCommand.Execute(resultFunction); LayoutWorkspace();
            Assert.Same(branchModule, vm.Selected);
            vm.Selected = branchRun;
            // Keep one local default while making the other inputs explicit spans.
            foreach (var value in branchRun.Editor.InheritedOutputDefaults.Where(value => value.Key != "CollectSortableTests").ToArray())
                WorkspaceParameterSetsPreviewTests.AddSpan(branchRun, value.Key, false, value.Value);
            WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(branchRun, true);
            foreach (var output in branchRun.Editor.ResultsQueryOutputs.Concat(branchRun.Editor.SetupQueryOutputs).ToArray())
                foreach (var key in output.Keys) key.Selected = true;
            LayoutWorkspace();
            Assert.True(save.IsEnabled);
            var typedSpans = (ItemsControl)FindWorkspaceField(fields, "ResultsParametersList");
            var channelSpan = branchRun.Editor.ResultsParameters.Single(row => row.Key == "ChannelCount");
            var spanRow = (ContentPresenter)typedSpans.ItemContainerGenerator.ContainerFromItem(channelSpan);
            var spanPicker = (ComboBox)typedSpans.ItemTemplate.FindName("ParameterKeyInput", spanRow);
            Assert.True(spanPicker.IsReadOnly);
            Assert.DoesNotContain("RngType", spanPicker.Items.Cast<string>());
            var values = (ItemsControl)typedSpans.ItemTemplate.FindName("ParameterValues", spanRow);
            var valueRow = (ContentPresenter)values.ItemContainerGenerator.ContainerFromIndex(0);
            var valueEditor = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceParameterValueEditor)values.ItemTemplate.FindName("ParameterValueEditor", valueRow);
            var numeric = (TextBox)valueEditor.FindName("TypedValueInput");
            numeric.SetCurrentValue(TextBox.TextProperty, "0"); LayoutWorkspace();
            Assert.False(save.IsEnabled); Assert.Empty(branchRun.Editor.ResultsParameterSetsPreview.Rows);
            numeric.SetCurrentValue(TextBox.TextProperty, "4"); LayoutWorkspace();
            Assert.True(save.IsEnabled);
            foreach (var width in new[] { 1400, 640 })
            {
                RenderWorkspaceState($"run-allowed-results-spans-{width}", width, 900);
                Assert.Equal(0, ((ScrollViewer)page.FindName("EntityInspectorViewport")).ScrollableWidth);
            }
            vm.SelectConfigurationItem("setupSpans"); LayoutWorkspace();
            Assert.Single(branchRun.Editor.SetupParameters, row => row.Key == "PrefixLibId");
            RenderWorkspaceState("run-allowed-setup-spans", 1400, 1000);
            vm.SelectConfigurationItem("setupOutputs"); LayoutWorkspace();
            Assert.False(((ItemsControl)fields.FindName("SetupDataTypesList")).IsVisible);
            Assert.Equal(setupSource.Id, branchRun.Editor.InheritedSetupDataTypes.Single(row => row.RequiresSource).SourceProjectId);
            RenderWorkspaceState("run-inherited-source-projects", 1400, 900);
            Assert.Equal(Sortopia.Functions.ResultsFunctionNames.sorterEval, branchRun.Editor.ResultsFunctionName);
            // Filtering visible dropdowns must not erase keys from parked editing-session rows.
            var retainedDefault = branchProject.Editor.ResultsParameterDefaults.Single(row => row.Key == "CollectSortableTests");
            var retainedSpan = branchRun.Editor.ResultsParameters.Single(row => row.Key == "SorterEvalType");
            vm.Selected = branchProject; vm.SelectConfigurationItem("resultsDefaults"); LayoutWorkspace();
            branchModule.Editor.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.standardSortableTests; LayoutWorkspace();
            Assert.Equal("CollectSortableTests", retainedDefault.Key);
            Assert.DoesNotContain(retainedDefault, branchProject.Editor.LocalResultsParameterDefaults);
            branchModule.Editor.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.sorterEval;
            vm.Selected = branchRun; vm.SelectConfigurationItem("resultsSpans"); LayoutWorkspace();
            branchModule.Editor.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.standardSortableTests; LayoutWorkspace();
            Assert.Equal("SorterEvalType", retainedSpan.Key);
            Assert.DoesNotContain(retainedSpan, branchRun.Editor.ActiveResultsParameters);
            branchModule.Editor.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.sorterEval; LayoutWorkspace();
            Assert.Contains(retainedDefault, branchProject.Editor.LocalResultsParameterDefaults);
            Assert.Contains(retainedSpan, branchRun.Editor.ActiveResultsParameters);
            Assert.True(save.IsEnabled);
            var overrideEditor = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceOverrideEditor)FindWorkspaceField(fields, "DescriptionOverrideEditor");
            var overrideToggle = (CheckBox)overrideEditor.FindName("OverrideToggle");
            var overrideInput = (TextBox)overrideEditor.FindName("OverrideValueInput");
            Assert.True(overrideInput.IsReadOnly);
            overrideToggle.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            LayoutWorkspace();
            overrideInput.SetCurrentValue(TextBox.TextProperty, "Description for this run only");
            Assert.False(overrideInput.IsReadOnly);
            Assert.Equal("Description for this run only", branchRun.Editor.DescriptionOverride.EffectiveValue);
            Assert.Equal("Overridden for this run", branchRun.Editor.DescriptionOverride.SourceLabel);
            var localSource = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceValueSource)overrideEditor.FindName("ValueSource");
            Assert.Equal(Visibility.Collapsed, ((TextBlock)localSource.FindName("SourceLinkText")).Visibility);
            RenderWorkspaceState("run-description-override");
            overrideToggle.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
            Assert.Equal(branchProject!.Editor!.Description, overrideInput.Text);
            var setupOverride = (Sortopia.WPF.Views.Pages.Workspace.WorkspaceOverrideEditor)FindWorkspaceField(fields, "SetupFunctionOverrideEditor");
            ((CheckBox)setupOverride.FindName("OverrideToggle")).SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            LayoutWorkspace();
            ((TextBox)setupOverride.FindName("OverrideValueInput")).SetCurrentValue(TextBox.TextProperty, "GenerateForThisRun");
            Assert.Equal(branchModule.Editor!.SetupFunctionName, branchRun.Editor.SetupFunctionOverride.EffectiveValue);
            Assert.False(branchRun.Editor.SetupFunctionOverride.CanOverride);
            var inspector = (ScrollViewer)page.FindName("EntityInspectorViewport");
            var setupFields = (FrameworkElement)FindWorkspaceField(fields, "SetupFields");
            inspector.ScrollToVerticalOffset(inspector.VerticalOffset + setupFields.TranslatePoint(new Point(0, 0), inspector).Y);
            RenderWorkspaceState("run-setup-fields");
            inspector.ScrollToBottom();
            RenderWorkspaceState("run-parameter-fields");
            LayoutWorkspace(640, 800);
            inspector.ScrollToVerticalOffset(inspector.VerticalOffset + setupFields.TranslatePoint(new Point(0, 0), inspector).Y);
            RenderWorkspaceState("run-fields-narrow", 640, 800);
            Assert.Equal(0, inspector.ScrollableWidth);
            vm.EditProjectDefaultsCommand.Execute(null);
            Assert.Same(branchProject, vm.Selected);
            vm.Selected = branchModule;
            vm.RemoveCommand.Execute(null);
            importSource = System.IO.Path.Combine(temporary.FullName, "library-project");
            WorkspaceImportTests.WriteProject(importSource, "Imported project");
            vm.Selected = vm.Roots.Single().Children.Single();
            CompleteRun(() => vm.AddExistingAsync(WorkspaceEntityKind.Project));
            Assert.Empty(vm.Error);
            LayoutWorkspace();
            Assert.Contains(importSource, ((TextBlock)page.FindName("PendingCopyNotice")).Text);
            RenderWorkspaceState("pending-copy");
            discard.Command.Execute(null);
            LayoutWorkspace();
            Assert.False(discard.IsEnabled);
            Assert.False(save.IsEnabled);
            Assert.Empty(vm.Roots.Single().Children.Single().Children);
            Assert.True(System.IO.Directory.Exists(importSource));
            Assert.False(System.IO.Directory.Exists(System.IO.Path.Combine(temporary.FullName, "Sorting", "library-project")));
            RenderWorkspaceState("discarded");
            vm.Selected = vm.Roots.Single().Children.Single();
            var moduleFolder = System.IO.Path.Combine(temporary.FullName, "Sorting");
            System.IO.File.Delete(WorkspaceDefinitionFiles.Require(moduleFolder, "module"));
            vm.RefreshDiskStatus();
            LayoutWorkspace();
            Assert.Equal("Missing definition", vm.Selected.DiskStatus);
            Assert.Equal(Visibility.Visible, ((TextBlock)page.FindName("MissingComponentsNotice")).Visibility);
            Assert.True(save.IsEnabled);
            RenderWorkspaceState("missing-definition");
            CompleteRun(vm.SaveWorkspaceAsync);
            Assert.Empty(vm.Error);
            Assert.False(vm.HasMissingComponents);
            vm.AddModuleCommand.Execute(null);
            vm.Selected!.Name = "Organized";
            var newParent = vm.Selected;
            vm.Selected = vm.Roots.Single().Children.Single(node => node.Name == "Sorting");
            LayoutWorkspace();
            var cut = (Button)page.FindName("CutEntityButton");
            var paste = (Button)page.FindName("PasteEntityButton");
            Assert.True(cut.IsEnabled);
            cut.Command.Execute(null);
            Assert.True(vm.Selected.IsCut);
            RenderWorkspaceState("cut");
            vm.Selected = newParent;
            LayoutWorkspace();
            Assert.True(paste.IsEnabled);
            var workspaceTree = (TreeView)page.FindName("WorkspaceTree");
            Assert.Contains(workspaceTree.InputBindings.OfType<KeyBinding>(), binding => binding.Key == Key.X && binding.Modifiers == ModifierKeys.Control && binding.Command == vm.CutCommand);
            Assert.Contains(workspaceTree.InputBindings.OfType<KeyBinding>(), binding => binding.Key == Key.V && binding.Modifiers == ModifierKeys.Control && binding.Command == vm.PasteCommand);
            Assert.Empty(newName.InputBindings);
            CompleteRun(vm.PasteAsync);
            Assert.Empty(vm.Error);
            Assert.Equal("Organized", vm.Selected!.Parent!.Name);
            LayoutWorkspace();
            Assert.Equal(System.IO.Path.Combine(temporary.FullName, "Organized", "Sorting"), fullPath.Text);
            Assert.False(openFolder.IsEnabled);
            Assert.False(vm.HasCutItem);
            RenderWorkspaceState("pending-move");
            CompleteRun(vm.SaveWorkspaceAsync);
            Assert.Empty(vm.Error);
            Assert.False(System.IO.Directory.Exists(moduleFolder));
            Assert.True(System.IO.Directory.Exists(System.IO.Path.Combine(temporary.FullName, "Organized", "Sorting")));
        }
        finally { temporary.Delete(recursive: true); }
    }

    private static void CheckRandomEducationControls()
    {
        using var vm = new RandomNetworksPageViewModel(() => { });
        var page = new Sortopia.WPF.Views.Pages.RandomNetworks.RandomNetworksPage { DataContext = vm };
        var diagram = (NetworkDiagram)FindSgdElement(page, "Diagram");
        var viewport = (ScrollViewer)FindSgdElement(page, "DiagramViewport");
        var selector = (Sortopia.WPF.Views.Shared.Controls.StageLayoutSelector)FindSgdElement(page, "StageLayouts");
        var usedCes = (RadioButton)selector.FindName("UsedCes");
        var compact = (RadioButton)selector.FindName("Compact");
        page.Measure(new Size(1000, 720));
        page.Arrange(new Rect(0, 0, 1000, 720));
        page.UpdateLayout();
        Assert.Null(diagram.Trace);
        Assert.Same(vm.SelectedNetwork, diagram.StaticNetwork);
        Assert.False(usedCes.IsEnabled);
        Assert.False(compact.IsEnabled);
        Assert.Equal("160", ((TextBox)FindSgdElement(page, "CeCountInput")).Text);
        vm.SelectedNetwork = RedundantNetwork();
        CompleteTest(vm);
        Assert.True(usedCes.IsEnabled);
        Assert.True(compact.IsEnabled);
        foreach (var size in new[] { new Size(1000, 720), new Size(640, 430), new Size(480, 360) })
        {
            foreach (var mode in new[] { 0, 1, 2, 3 })
            {
                ((RadioButton)selector.FindName(new[] { "AllCes", "UsedStages", "UsedCes", "Compact" }[mode])).IsChecked = true;
                page.Measure(size);
                page.Arrange(new Rect(new Point(), size));
                page.UpdateLayout();
                Assert.Equal(mode, vm.DisplayMode);
                Assert.Equal(mode, diagram.DisplayMode);
                Assert.Null(diagram.Trace);
                Assert.Same(vm.Usage, diagram.Usage);
                Assert.True(viewport.ViewportHeight > 0, $"Viewport height {viewport.ViewportHeight}, size {size}, mode {mode}");
                Assert.True(viewport.ExtentWidth <= viewport.ViewportWidth + 0.1, $"Width {viewport.ExtentWidth}/{viewport.ViewportWidth}, size {size}, mode {mode}");
                Assert.True(viewport.ExtentHeight <= viewport.ViewportHeight + 0.1, $"Height {viewport.ExtentHeight}/{viewport.ViewportHeight}, size {size}, mode {mode}");
            }
            var fitZoom = diagram.Zoom;
            ((Slider)FindSgdElement(page, "ZoomSlider")).Value = 4;
            page.Measure(size);
            page.Arrange(new Rect(new Point(), size));
            page.UpdateLayout();
            Assert.Equal(4, vm.ZoomFactor);
            Assert.Equal(fitZoom * 4, diagram.Zoom, 5);
            Assert.True(viewport.ScrollableWidth > 0 || viewport.ScrollableHeight > 0);
            viewport.ScrollToHorizontalOffset(viewport.ScrollableWidth / 2);
            viewport.ScrollToVerticalOffset(viewport.ScrollableHeight / 2);
            page.UpdateLayout();
            Assert.True(viewport.HorizontalOffset > 0 || viewport.VerticalOffset > 0);
            ((Slider)FindSgdElement(page, "ZoomSlider")).Value = 1;
            page.UpdateLayout();
            Assert.True(viewport.ExtentWidth <= viewport.ViewportWidth + 0.1);
            Assert.True(viewport.ExtentHeight <= viewport.ViewportHeight + 0.1);
        }
        vm.GenerateCommand.Execute(null);
        Assert.Equal(0, selector.Mode);
        Assert.False(compact.IsEnabled);
        Assert.Null(diagram.Usage);
        Assert.Same(vm.SelectedNetwork, diagram.StaticNetwork);
    }

    private static void CheckCatalogControls()
    {
        using var vm = new NetworksPageViewModel(() => { });
        var page = new Sortopia.WPF.Views.Pages.Networks.NetworksPage { DataContext = vm };
        var viewport = (ScrollViewer)FindSgdElement(page, "DiagramViewport");
        var diagram = (NetworkDiagram)FindSgdElement(page, "Diagram");
        Assert.IsType<Sortopia.WPF.Rendering.AnimationSchemes.CeSliderScheme>(diagram.AnimationScheme);
        var rows = (Grid)FindSgdElement(page, "PageRows");
        Assert.Equal(3, rows.RowDefinitions.Count);
        foreach (var size in new[] { new Size(1000, 720), new Size(640, 430), new Size(480, 360) })
        {
            void Layout()
            {
                page.Measure(size);
                page.Arrange(new Rect(new Point(), size));
                page.UpdateLayout();
            }
            foreach (var network in new[] { vm.Networks.First(), vm.Networks.First(n => n.Width == 8), vm.Networks.Last() })
            {
                vm.SelectedNetwork = network;
                Layout();
                Assert.Equal(1, vm.ZoomFactor);
                Assert.True(viewport.ViewportWidth > 0 && viewport.ViewportHeight > 0);
                Assert.True(viewport.ExtentWidth <= viewport.ViewportWidth + 0.1);
                Assert.True(viewport.ExtentHeight <= viewport.ViewportHeight + 0.1);
                Assert.Same(vm.Trace, diagram.Trace);
                Assert.Same(vm.ComparatorStates, diagram.ComparatorStates);
            }
            vm.ZoomFactor = 2;
            Layout();
            Assert.True(viewport.ScrollableWidth > 0 || viewport.ScrollableHeight > 0);
            vm.ZoomFactor = 1;
            Layout();
            Assert.True(viewport.ExtentWidth <= viewport.ViewportWidth + 0.1);
            Assert.True(viewport.ExtentHeight <= viewport.ViewportHeight + 0.1);
        }
        vm.SeekPosition = 5;
        var colors = diagram.ComparatorStates;
        vm.NewInputCommand.Execute(null);
        Assert.Same(colors, diagram.ComparatorStates);
        Assert.Equal(0, diagram.Position);
        vm.ResetColorsCommand.Execute(null);
        Assert.All(diagram.ComparatorStates!, state => Assert.Equal(ComparatorState.Unseen, state));
    }

    private static void CheckFirstGenerateFromHome(bool deferSelection)
    {
        var window = new MainWindow();
        try
        {
            var shell = Assert.IsType<MainWindowViewModel>(window.DataContext);
            var content = Assert.IsType<ContentControl>(window.Content);
            void Layout()
            {
                CompleteRun(async () => await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle));
                content.Measure(new Size(1000, 740));
                content.Arrange(new Rect(0, 0, 1000, 740));
                content.UpdateLayout();
            }
            Layout();
            var home = Assert.IsType<HomePageViewModel>(shell.CurrentPage);
            home.OpenSgdCommand.Execute(null);
            Layout();
            var presenter = Assert.IsType<ContentPresenter>(VisualTreeHelper.GetChild(content, 0));
            var page = Assert.IsType<Sortopia.WPF.Views.Pages.Sgd.SgdPage>(VisualTreeHelper.GetChild(presenter, 0));
            var vm = Assert.IsType<SgdPageViewModel>(page.DataContext);
            var channels = (ComboBox)FindSgdElement(page, "ChannelPicker");
            var stepButtons = ((WrapPanel)FindSgdElement(page, "WizardSteps")).Children.OfType<Button>().ToArray();
            var controlHeight = channels.ActualHeight;
            Assert.True(controlHeight > 0);
            void CheckHeights(params string[] names)
            {
                Assert.All(stepButtons, button => Assert.Equal(controlHeight, button.ActualHeight));
                foreach (var name in names)
                {
                    var control = (FrameworkElement)FindSgdElement(page, name);
                    Assert.Equal(controlHeight, control.ActualHeight);
                    if (control is TextBox editor)
                    {
                        var textHost = (ScrollViewer)editor.Template.FindName("PART_ContentHost", editor);
                        Assert.True(textHost.ExtentHeight <= textHost.ViewportHeight + 0.1,
                            $"{name}: text needs {textHost.ExtentHeight:F2}px, viewport has {textHost.ViewportHeight:F2}px.");
                    }
                }
            }
            CheckHeights("MethodPicker", "StagesInput", "SampleInput", "Avx512Picker");

            if (deferSelection)
            {
                // Keep the source at its initial eight channels until Make one receives the displayed choice.
                channels.SetBinding(ComboBox.SelectedItemProperty, new Binding("Generator.ChannelCount")
                    { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
            }
            channels.SetCurrentValue(ComboBox.SelectedItemProperty, 16);
            Assert.Equal(16, channels.SelectedItem);
            Assert.Equal(deferSelection ? 8 : 16, vm.Setup.Generator.ChannelCount);
            Assert.Equal(1, (int)vm.CurrentStep);
            Assert.False(vm.EvolutionStepCommand.CanExecute(null));
            // An explicitly deferred binding is committed before leaving Setup.
            channels.GetBindingExpression(ComboBox.SelectedItemProperty)!.UpdateSource();
            vm.SamplingStepCommand.Execute(null);
            Layout();
            Assert.Equal(2, (int)vm.CurrentStep);
            var makeOne = (Button)FindSgdElement(page, "MakeOneButton");
            var batch = (Button)FindSgdElement(page, "BatchButton");
            var evolve = (Button)FindSgdElement(page, "EvolveButton");
            Assert.Same(vm.Sampling.BatchCommand, batch.Command);
            CheckHeights("SamplingDestination", "SourcePicker", "SeedInput");
            var seedInput = (TextBox)FindSgdElement(page, "SeedInput");
            Assert.Equal(controlHeight, ((Grid)seedInput.Parent).Children.OfType<Button>().Single().ActualHeight);
            Assert.False(evolve.IsEnabled);
            var previousSeed = vm.Setup.Generator.SeedText;
            CompleteRun(async () =>
            {
                var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                vm.Sampling.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(vm.Sampling.CanEdit) && vm.EvolutionStepCommand.CanExecute(null)) published.TrySetResult();
                };
                var peer = new ButtonAutomationPeer(makeOne);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
            });
            Assert.Equal(16, vm.Setup.Generator.ChannelCount);
            Assert.NotEqual(previousSeed, vm.Setup.Generator.SeedText);
            Assert.Equal(vm.Setup.Generator.SeedText, seedInput.Text);
            Assert.Equal(int.Parse(seedInput.Text), vm.Sampling.GenerationResult!.Seed);
            Assert.Equal(16, Results(vm).Network!.Width);
            Assert.True(CurrentUsage(vm)!.Exhaustive);
            Assert.Equal(65_536, CurrentUsage(vm)!.Tested);
            Assert.Equal(CurrentUsage(vm)!.Failed, vm.Sampling.GenerationResult!.Failures);
            Assert.Equal(CurrentUsage(vm)!.Tested, vm.Sampling.GenerationResult.Inputs);
            Assert.True(evolve.IsEnabled);
            evolve.Command.Execute(evolve.CommandParameter);
            Assert.Equal(3, (int)vm.CurrentStep);
            Layout();
            Assert.Equal("CE swap frequency", ((TextBlock)FindSgdElement(page, "UsageLegendTitle")).Text);
            var index = (TextBox)FindSgdElement(page, "NetworkIndex");
            var parent = Results(vm).NetworkHistory.Current!;
            Results(vm).NetworkHistory.Add(parent with { Description = "Another network" });
            Assert.Equal("2", index.Text);
            index.SetCurrentValue(TextBox.TextProperty, "1");
            index.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, index, null)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.Same(parent, Results(vm).NetworkHistory.Current);
            var diagram = (NetworkDiagram)FindSgdElement(page, "SwitchDiagram");
            var layouts = (Sortopia.WPF.Views.Shared.Controls.StageLayoutSelector)((Viewbox)FindSgdElement(page, "NetworkLayoutViewport")).Child;
            layouts.SetCurrentValue(Sortopia.WPF.Views.Shared.Controls.StageLayoutSelector.ModeProperty, 0);
            Assert.Equal(0, diagram.DisplayMode);

            // Visiting Workspace preserves SGD settings, buffers and selection.
            vm.ReturnHomeCommand.Execute(null);
            home.OpenWorkspaceCommand.Execute(null);
            Layout();
            presenter = Assert.IsType<ContentPresenter>(VisualTreeHelper.GetChild(content, 0));
            Assert.IsType<Sortopia.WPF.Views.Pages.Workspace.WorkspacePage>(VisualTreeHelper.GetChild(presenter, 0));
            Assert.IsType<WorkspacePageViewModel>(shell.CurrentPage).ReturnHomeCommand.Execute(null);
            home.OpenSgdCommand.Execute(null);
            Layout();
            Assert.Same(vm, shell.CurrentPage);
            presenter = Assert.IsType<ContentPresenter>(VisualTreeHelper.GetChild(content, 0));
            var original = Assert.IsType<Sortopia.WPF.Views.Pages.Sgd.SgdPage>(VisualTreeHelper.GetChild(presenter, 0));
            Assert.Same(vm, original.DataContext);
            Assert.Same(parent, Results(vm).NetworkHistory.Current);
            Assert.Equal(Color.FromRgb(16, 19, 24), ((SolidColorBrush)((Border)original.Content).Background).Color);
        }
        finally { window.Close(); }
    }

    private static void CheckSgdControls()
    {
        var vm = new SgdPageViewModel(() => { });
        var host = new Sortopia.WPF.Views.Pages.Sgd.SgdPage { DataContext = vm };
        void CheckUpperViewports()
        {
            foreach (var size in new[] { new Size(640, 480), new Size(1000, 800) })
            {
                host.Measure(size);
                host.Arrange(new Rect(new Point(), size));
                host.UpdateLayout();
                foreach (var name in new[] { "SettingsViewport", "OutputViewport" })
                {
                    var viewport = (Viewbox)FindSgdElement(host, name);
                    var child = (FrameworkElement)viewport.Child;
                    var bounds = child.TransformToAncestor(viewport).TransformBounds(new Rect(child.RenderSize));
                    Assert.True(bounds.Width > 0 && bounds.Height > 0);
                    Assert.True(bounds.Left >= -0.01 && bounds.Right <= viewport.ActualWidth + 0.01);
                    Assert.True(bounds.Top >= -0.01 && bounds.Bottom <= viewport.ActualHeight + 0.01);
                    var area = (Grid)FindSgdElement(host, "SettingsArea");
                    bounds = child.TransformToAncestor(area).TransformBounds(new Rect(child.RenderSize));
                    Assert.True(bounds.Left >= -0.01 && bounds.Right <= area.ActualWidth + 0.01);
                    Assert.True(bounds.Top >= -0.01 && bounds.Bottom <= area.ActualHeight + 0.01);
                }
            }
        }
        host.Measure(new Size(1000, 800));
        host.Arrange(new Rect(0, 0, 1000, 800));
        CompleteRun(async () => await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle));
        var channels = (ComboBox)FindSgdElement(host, "ChannelPicker");
        var methods = (ComboBox)FindSgdElement(host, "MethodPicker");
        var avx = (CheckBox)FindSgdElement(host, "Avx512Picker");
        Assert.Equal(System.Runtime.Intrinsics.X86.Avx512F.IsSupported, avx.IsChecked);
        Assert.Equal(System.Runtime.Intrinsics.X86.Avx512F.IsSupported, avx.IsEnabled);
        CompleteRun(vm.Sampling.GenerateAsync);
        Assert.Equal(8, Results(vm).Network!.Width);
        if (avx.IsEnabled) avx.SetCurrentValue(CheckBox.IsCheckedProperty, true);
        Assert.Equal(avx.IsEnabled, vm.Setup.UseAvx512);
        methods.SetCurrentValue(ComboBox.SelectedItemProperty, "msuf4");
        Assert.Equal(8, channels.SelectedItem);
        channels.SetCurrentValue(ComboBox.SelectedItemProperty, 16);
        Assert.Equal(16, vm.Setup.Generator.ChannelCount);
        methods.SetCurrentValue(ComboBox.SelectedItemProperty, "msuf6");
        Assert.Equal(12, channels.SelectedItem);
        channels.SetCurrentValue(ComboBox.SelectedItemProperty, 24);
        Assert.Equal(24, vm.Setup.Generator.ChannelCount);
        Assert.False(vm.Setup.Generator.CanTestAll);
        Assert.Equal("4096", vm.Setup.Generator.SamplesText);
        vm.Setup.Generator.SamplesText = "513";
        Assert.False(vm.Evolution.RunSgdCommand.CanExecute(null));
        vm.SamplingStepCommand.Execute(null);
        host.UpdateLayout();
        CheckUpperViewports();
        var tables = (ItemsControl)FindSgdElement(host, "GenerationWeightTables");
        Assert.Equal(2, tables.Items.Count);
        var unfolding = Assert.IsType<ParamEditor2d>(VisualTreeHelper.GetChild(tables.ItemContainerGenerator.ContainerFromIndex(1), 0));
        var weights = (ListBox)unfolding.FindName("Cells");
        weights.SelectedIndex = 4;
        ((Slider)unfolding.FindName("ValueSlider")).SetCurrentValue(Slider.ValueProperty, 0.25);
        Assert.Equal(0.25, vm.Sampling.GenerationOptions.Distributions[2].Weights[1].Value);
        CompleteRun(vm.Sampling.GenerateAsync);
        vm.EvolutionStepCommand.Execute(null);
        host.UpdateLayout();
        var editor = (ParamEditor2d)FindSgdElement(host, "MutationEditor");
        var overall = (Slider)FindSgdElement(host, "ModificationRateSlider");
        host.UpdateLayout();
        CheckUpperViewports();
        Assert.InRange(overall.ActualWidth, 1, 126);
        var rateControls = (Grid)FindSgdElement(host, "EvolutionRateControls");
        var options = (WrapPanel)FindSgdElement(host, "EvolutionOptions");
        Assert.True(rateControls.ActualWidth <= options.ActualWidth);
        var variant = (ComboBox)FindSgdElement(host, "VariantPicker");
        Assert.True(overall.TranslatePoint(new Point(0, overall.ActualHeight), host).Y < variant.TranslatePoint(new Point(), host).Y);
        Assert.Equal(-1, overall.Value);
        overall.SetCurrentValue(Slider.ValueProperty, -2.0);
        Assert.Equal(0.01, vm.Evolution.Mutation.OverallRate.Value);
        var overallTrack = (System.Windows.Controls.Primitives.Track)overall.Template.FindName("PART_Track", overall);
        Assert.Equal(overall.Value, overallTrack.Value);
        Assert.Equal(overall.Minimum, overallTrack.Minimum);
        Assert.Equal(overall.Maximum, overallTrack.Maximum);
        ((ListBox)editor.FindName("Cells")).SelectedIndex = 1;
        var rate = (Slider)editor.FindName("ValueSlider");
        Assert.Equal(1, rate.Value);
        rate.SetCurrentValue(Slider.ValueProperty, 0.25);
        Assert.Equal(0.25, vm.Evolution.Mutation.ParameterTables[0].Parameters.Cells[1].Value);
        var tablePicker = (ComboBox)FindSgdElement(host, "MutationTablePicker");
        static IEnumerable<TextBlock> Labels(DependencyObject visual)
        {
            if (visual is TextBlock label) yield return label;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
                foreach (var text in Labels(VisualTreeHelper.GetChild(visual, i))) yield return text;
        }
        for (var index = 0; index < vm.Evolution.Mutation.ParameterTables.Count; index++)
        {
            tablePicker.SetCurrentValue(ComboBox.SelectedIndexProperty, index);
            host.UpdateLayout();
            var label = Assert.Single(Labels(tablePicker));
            Assert.Equal(vm.Evolution.Mutation.ParameterTables[index].Name, label.Text);
            var bounds = label.TransformToAncestor(tablePicker).TransformBounds(new Rect(label.RenderSize));
            Assert.True(bounds.Left >= 0 && bounds.Right <= tablePicker.ActualWidth - 20);
            Assert.True(bounds.Top >= 2 && bounds.Bottom <= tablePicker.ActualHeight - 2);
        }
        tablePicker.SetCurrentValue(ComboBox.SelectedIndexProperty, 2);
        host.UpdateLayout();
        Assert.Equal("Unfold 6 → 12", vm.Evolution.Mutation.SelectedTable!.Name);
        ((ListBox)editor.FindName("Cells")).SelectedIndex = 1;
        rate.SetCurrentValue(Slider.ValueProperty, 0.5);
        Assert.Equal(0.5, vm.Evolution.Mutation.ParameterTables[2].Parameters.Cells[1].Value);
        vm.Evolution.Children = 1;
        vm.Evolution.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Evolution.Generation) && vm.Evolution.Generation == 1) vm.Stop();
        };
        CompleteRun(() => Evolve(vm).WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(24, Results(vm).Network!.Width);
        Assert.Equal(513, CurrentUsage(vm)!.Tested);
        Assert.Equal(1, vm.Evolution.Generation);
        Assert.Equal(3L, Results(vm).Visits.Counts.Values.Sum());
        Assert.Equal(1L + vm.Evolution.Children, vm.Evolution.UniqueSorters + vm.Evolution.DuplicateSorters);
    }


    private static void CheckSgdPrefixControls()
    {
        var vm = new SgdPageViewModel(() => { });
        vm.Setup.Generator.CountText = "3";
        vm.Setup.Generator.SamplesText = "all";
        vm.Setup.Generator.CountText = "3";
        vm.Setup.UseAvx512 = false;
        var host = new Sortopia.WPF.Views.Pages.Sgd.SgdPage { DataContext = vm };
        void Layout()
        {
            host.Measure(new Size(1000, 800));
            host.Arrange(new Rect(0, 0, 1000, 800));
            host.UpdateLayout();
        }
        void Render(string name)
        {
            Layout();
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap(1000, 800, 96, 96, PixelFormats.Pbgra32);
            image.Render(host);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, name));
            encoder.Save(stream);
        }
        vm.SamplingStepCommand.Execute(null);
        Layout();
        var samplingPicker = (ComboBox)FindSgdElement(host, "PrefixPicker");
        Assert.Equal("Sampling prefix", System.Windows.Automation.AutomationProperties.GetName(samplingPicker));
        var first = vm.Setup.Generator.Prefix.Choices.Single(choice => choice.Key == "Prefix1/1");
        samplingPicker.SetCurrentValue(ComboBox.SelectedItemProperty, first);
        CompleteRun(vm.Sampling.GenerateAsync);
        Assert.Equal("Prefix1 · 1 stage", vm.Sampling.GenerationResult!.Prefix);
        Assert.Equal(81, vm.Sampling.GenerationResult.Inputs);
        Assert.Equal(81, ((HeaderedContentControl)FindSgdElement(host, "TestInputsResult")).Content);
        Assert.Contains("distinct prefix outputs", vm.Sampling.Summary);
        vm.Setup.Generator.SamplesText = "513";
        vm.Setup.Generator.CountText = "3";
        CompleteRun(vm.Sampling.GenerateAsync);
        Assert.InRange(vm.Sampling.GenerationResult!.Inputs, 1, 81);
        Assert.Equal(513, vm.Sampling.LatestCandidate!.RequestedSamples);
        var original = vm.Sampling.LatestCandidate!;
        Assert.Equal(first.Comparators, Sortopia.Models.Sorting.Simple.SimpleSorterModel.getPrefix(original.Model));
        Assert.True(vm.Sampling.LatestMatchesSetup);
        var other = vm.Setup.Generator.Prefix.Choices.Single(choice => choice.Key == "PrefixA/3");
        samplingPicker.SetCurrentValue(ComboBox.SelectedItemProperty, other);
        Assert.False(vm.Sampling.LatestMatchesSetup);
        samplingPicker.SetCurrentValue(ComboBox.SelectedItemProperty, first);
        Render("prefix-sampling.png");

        vm.EvolutionStepCommand.Execute(null);
        Layout();
        var evolutionPicker = (ComboBox)FindSgdElement(host, "PrefixPicker");
        Assert.Equal("Evolution prefix", System.Windows.Automation.AutomationProperties.GetName(evolutionPicker));
        Assert.Equal("Prefix1/1", vm.Evolution.Prefix.Selected.Key);
        var before = Sortopia.Models.Sorting.Simple.SimpleSorterModel.makeSorterWithoutPrefix(null, vm.Evolution.Parent!.Model).Ces;
        var nextPrefix = vm.Evolution.Prefix.Choices.Single(choice => choice.Key == "PrefixA/3");
        CompleteRun(() =>
        {
            evolutionPicker.SetCurrentValue(ComboBox.SelectedItemProperty, nextPrefix);
            Assert.False(evolutionPicker.IsEnabled);
            return vm.Evolution.PrefixChangeTask;
        });
        Assert.True(evolutionPicker.IsEnabled);
        Assert.Equal(nextPrefix.Comparators, Sortopia.Models.Sorting.Simple.SimpleSorterModel.getPrefix(vm.Evolution.Parent!.Model));
        Assert.Equal(before, Sortopia.Models.Sorting.Simple.SimpleSorterModel.makeSorterWithoutPrefix(null, vm.Evolution.Parent.Model).Ces);
        Assert.Equal(first.Comparators, Sortopia.Models.Sorting.Simple.SimpleSorterModel.getPrefix(original.Model));
        Assert.Equal(0, vm.Evolution.Generation);
        var reduced = PrefixNetworkTests.ReducedOracle(vm.Evolution.Parent.RawNetwork, nextPrefix.Comparators, vm.Evolution.Usage!.FixedCes, 513);
        Assert.Equal(reduced.CeUses, vm.Evolution.Usage.CeUses);
        Assert.Equal(reduced.Tested, vm.Evolution.Usage.Tested);
        Assert.True(vm.Evolution.Usage.Tested < 513);
        Render("prefix-evolution.png");

        vm.Evolution.Children = 2;
        vm.Evolution.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.Evolution.Generation) && vm.Evolution.Generation == 1) vm.Stop();
        };
        CompleteRun(() => vm.Evolution.RunSgdAsync().WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(1, vm.Evolution.Generation);
        Assert.Equal(nextPrefix.Comparators, Sortopia.Models.Sorting.Simple.SimpleSorterModel.getPrefix(vm.Evolution.Parent.Model));
        Assert.Equal(513, vm.Evolution.Parent.RequestedSamples);
        Assert.Equal(reduced.Tested, vm.Evolution.Usage!.Tested);
        var retainedParent = vm.Evolution.Parent;
        var none = vm.Evolution.Prefix.Choices.Single(choice => choice.Key == "none");
        CompleteRun(() =>
        {
            evolutionPicker.SetCurrentValue(ComboBox.SelectedItemProperty, none);
            vm.Stop();
            return vm.Evolution.PrefixChangeTask;
        });
        Assert.Same(retainedParent, vm.Evolution.Parent);
        Assert.Equal(nextPrefix.Key, vm.Evolution.Prefix.Selected.Key);
        Assert.Equal(1, vm.Evolution.Generation);
        CompleteRun(() =>
        {
            evolutionPicker.SetCurrentValue(ComboBox.SelectedItemProperty, none);
            return vm.Evolution.PrefixChangeTask;
        });
        Assert.Equal(0, vm.Evolution.Generation);
        Assert.Empty(Sortopia.Models.Sorting.Simple.SimpleSorterModel.getPrefix(vm.Evolution.Parent!.Model));
        Assert.Equal(Sortopia.Models.Sorting.Simple.SimpleSorterModel.makeSorterWithoutPrefix(null, retainedParent.Model).Ces,
            Sortopia.Models.Sorting.Simple.SimpleSorterModel.makeSorterWithoutPrefix(null, vm.Evolution.Parent.Model).Ces);
    }

    private static SgdResultsViewModel Results(SgdPageViewModel vm) =>
        vm.CurrentStep == SgdStep.Evolution ? vm.Evolution : vm.Sampling;

    private static NetworkCheckResult? CurrentUsage(SgdPageViewModel vm) =>
        vm.CurrentStep == SgdStep.Evolution ? vm.Evolution.Usage : vm.Sampling.Usage;

    private static Task Evolve(SgdPageViewModel vm)
    {
        vm.EvolutionStepCommand.Execute(null);
        return vm.Evolution.RunSgdAsync();
    }

    private static object FindSgdElement(FrameworkElement host, string name)
    {
        host.UpdateLayout();
        object? Find(DependencyObject node)
        {
            if (node is FrameworkElement element)
            {
                if (element.Name == name) return element;
                if (element.FindName(name) is { } named) return named;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (Find(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
            return null;
        }
        return Find(host) ?? throw new InvalidOperationException($"Missing {name} in {host.GetType().Name}.");
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
