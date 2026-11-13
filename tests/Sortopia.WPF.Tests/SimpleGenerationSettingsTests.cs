using Microsoft.FSharp.Collections;
using Sortopia.Core;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Models.Sorting.Simple.Ce;
using Sortopia.Models.Sorting.Simple.Si;
using Sortopia.Models.Sorting.Simple.Rs;
using Sortopia.Models.Sorting.Simple.Uf4;
using Sortopia.Models.Sorting.Simple.Uf6;
using Sortopia.Permutations;
using Sortopia.WPF.ViewModels.Pages.Sgd;
using Sortopia.WPF.ViewModels.Shared.Controls;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class SimpleGenerationSettingsTests
{
    public static IEnumerable<object[]> Choices =>
        from source in new[] { "SplitMix64", "LCG", ".NET" }
        from pair in new[] { ("msce", 8), ("mssi", 9), ("msrs", 8), ("msuf4", 16), ("msuf6", 24) }
        select new object[] { source, pair.Item1, pair.Item2 };

    [Theory]
    [MemberData(nameof(Choices))]
    public void Sgd_passes_typed_controls_to_each_model_initializer_and_snapshots_them(string source, string method, int width)
    {
        var page = new SgdPageViewModel(() => { });
        var settings = page.Setup.Generator;
        settings.Method = method; settings.ChannelCount = width; settings.CountText = "5"; settings.SeedText = "123";
        settings.Prefix.Selected = settings.Prefix.Choices.First(choice => choice.Comparators.Length > 0);
        var options = settings.Options!; options.RandomSource = source; options.ExcludeSelf = false;
        string[] names = method switch {
            "msrs" => ["Stage"],
            "msuf4" => ["Seed", "Unfold 4 → 8", "Unfold 8 → 16"],
            "msuf6" => ["Seed", "Unfold 6 → 12", "Unfold 12 → 24"],
            _ => []
        };
        Assert.Equal(names, options.Distributions.Select(row => row.Name));
        if (method == "msuf6") Assert.IsType<Seed6GenerationDistribution>(options.Distributions[0]);
        Assert.All(options.Distributions.Skip(method == "msuf6" ? 1 : 0), row => Assert.IsType<OpsGenerationDistribution>(row));
        foreach (var row in options.Distributions)
            for (var index = 0; index < row.Weights.Length; ++index) row.Weights[index].Value = index / (double)row.Weights.Length;
        Assert.Empty(options.Error); Assert.True(page.Sampling.GenerateCommand.CanExecute(null));
        opsGenRates Ops(GenerationDistribution row)
        {
            var modes = Assert.IsType<OpsGenerationDistribution>(row);
            return OpsGenerationProbabilities.fromWeights(modes.Ortho.Value, modes.Para.Value, modes.SelfReflection.Value);
        }
        seed6GenRates Seed6(GenerationDistribution row)
        {
            var modes = Assert.IsType<Seed6GenerationDistribution>(row);
            return Seed6GenerationProbabilities.fromWeights(modes.Ortho1.Value, modes.Ortho2.Value,
                modes.Para1.Value, modes.Para2.Value, modes.Para3.Value, modes.Para4.Value, modes.SelfReflection.Value);
        }
        var rng = RngFactory.create(RngType.fromString(source == ".NET" ? "Net" : source));
        var prefix = settings.Prefix.Selected.Comparators;
        var unfolding = ListModule.OfSeq(options.Distributions.Skip(1).Select(Ops));
        var expected = method switch {
            "msce" => simpleSorterModelGen.NewSmmMsceRandGen(MsceRandGen.create(rng, width, prefix, 5, false)),
            "mssi" => simpleSorterModelGen.NewSmmMssiRandGen(MssiRandGen.create(rng, width, prefix, 5)),
            "msrs" => simpleSorterModelGen.NewSmmMsrsRandGen(MsrsRandGen.create(rng, width, prefix, 5, Ops(options.Distributions[0]))),
            "msuf4" => simpleSorterModelGen.NewSmmMsuf4RandGen(Msuf4RandGen.create(rng, width, prefix, 5, Ops(options.Distributions[0]), unfolding)),
            "msuf6" => simpleSorterModelGen.NewSmmMsuf6RandGen(Msuf6RandGen.create(rng, width, prefix, 5, Seed6(options.Distributions[0]), unfolding)),
            _ => throw new InvalidOperationException()
        };
        var snapshot = settings.Snapshot();
        Assert.Equal(SimpleSorterModelGen.getId(expected), SimpleSorterModelGen.getId(snapshot.ConfiguredGenerator!));
        var model = snapshot.CreateModel(); var full = SimpleSorterModel.makeSorterWithPrefix(null!, model);
        Assert.Equal(settings.Prefix.Selected.Comparators, SimpleSorterModel.getPrefix(model));
        options.RandomSource = source == "LCG" ? "SplitMix64" : "LCG";
        options.ExcludeSelf = true;
        foreach (var row in options.Distributions) foreach (var value in row.Weights) value.Value = 1;
        Assert.Equal(full, SimpleSorterModel.makeSorterWithPrefix(null!, snapshot.CreateModel()));
        Assert.NotEqual(SimpleSorterModelGen.getId(expected), SimpleSorterModelGen.getId(settings.Snapshot().ConfiguredGenerator!));
    }

    [Fact]
    public void Width_changes_preserve_existing_rows_and_validation_controls_sampling_commands()
    {
        var page = new SgdPageViewModel(() => { }); var settings = page.Setup.Generator;
        settings.Method = "msuf6"; settings.ChannelCount = 12;
        var options = settings.Options!; var seed = options.Distributions[0]; var unfolding = options.Distributions[1];
        seed.Weights[0].Value = 0.25; unfolding.Weights[1].Value = 0.75;
        settings.ChannelCount = 24;
        Assert.Same(seed, options.Distributions[0]); Assert.Same(unfolding, options.Distributions[1]);
        Assert.Equal(0.25, seed.Weights[0].Value); Assert.Equal(0.75, unfolding.Weights[1].Value);
        Assert.All(options.Distributions[2].Weights, value => Assert.Equal(1, value.Value));
        var row = options.Distributions[2]; foreach (var value in row.Weights) value.Value = 0;
        Assert.Contains("Unfold 12 → 24", options.Error); Assert.False(page.Sampling.GenerateCommand.CanExecute(null));
        row.Weights[0].Value = double.NaN;
        Assert.Contains("finite", options.Error); Assert.False(page.Sampling.BatchCommand.CanExecute(null));
        row.Weights[0].Value = 1;
        options.RandomSource = "invalid"; Assert.NotEmpty(options.Error); Assert.False(page.Sampling.GenerateCommand.CanExecute(null));
        options.RandomSource = ".NET"; Assert.Empty(options.Error); Assert.True(page.Sampling.GenerateCommand.CanExecute(null));
        settings.ChannelCount = 12; Assert.Same(seed, options.Distributions[0]); Assert.Same(unfolding, options.Distributions[1]);
    }
}
