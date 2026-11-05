using Sortopia.Core;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.WPF.ViewModels;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class MutationContractTests
{
    [Theory]
    [InlineData("msuf4", 4, "V1")]
    [InlineData("msuf4", 8, "V2")]
    [InlineData("msuf4", 16, "V1")]
    [InlineData("msuf4", 64, "V2")]
    [InlineData("msuf6", 12, "V1")]
    [InlineData("msuf6", 24, "V1")]
    [InlineData("msuf6", 48, "V1")]
    public void Every_unfolding_matrix_controls_its_own_source_to_destination_transitions(string method, int width, string variant)
    {
        var settings = new SorterMutationSettings();
        settings.SelectMethod(method, width);
        settings.Variant = variant;
        settings.OverallRate.Value = 1;
        var tables = settings.ParameterTables;
        var stepCount = MathUtils.exactLog2(width / (method == "msuf4" ? 4 : 6));
        Assert.Equal(stepCount + 2, tables.Count);
        Assert.Equal(stepCount + 1, tables[0].Parameters.Columns);
        for (var step = 0; step <= stepCount; step++)
        {
            var table = tables[step + 1].Parameters;
            foreach (var cell in table.Cells) cell.Value = 0;
            for (var source = 0; source < table.Columns; source++)
                table.Cells[source * table.Columns + (source + step + 1) % table.Columns].Value = 1;
        }

        var parent = RandomNetwork.generateModel(method, width, 16, Guid.Parse("8741ece5-b169-45ba-8556-c87329db496d"));
        var before = Modes(parent);
        var mutator = settings.Create(parent);
        simpleSorterModel Mutate(simpleSorterModelMutator m) =>
            SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(m, parent, 3, 7);
        var child = Mutate(mutator);
        var after = Modes(child);
        for (var stage = 0; stage < before.Length; stage++)
            for (var step = 0; step <= stepCount; step++)
                Assert.Equal(before[stage][step].Select(mode => (mode + step + 1) % tables[step + 1].Parameters.Columns), after[stage][step]);
        Assert.Equal(SimpleSorterModel.getId(child), SimpleSorterModelMutator.makeMutantSorterModelIdFromIndexAndMod(mutator, parent, 3, 7));

        // Disable just one unfolding step (or the seed for a seed-only sorter).
        var disabled = Math.Min(1, stepCount);
        tables[0].Parameters.Cells[disabled].Value = 0;
        var withDisabledStep = Modes(Mutate(settings.Create(parent)));
        for (var stage = 0; stage < before.Length; stage++)
            for (var step = 0; step <= stepCount; step++)
                Assert.Equal(step == disabled ? before[stage][step] : after[stage][step], withDisabledStep[stage][step]);
        // Built mutators capture their matrices, so edits do not change their replay.
        Assert.Equal(child, Mutate(mutator));

        // The global rate scales the probability of every seed and unfolding transition.
        settings.OverallRate.Value = 0.4;
        foreach (var cell in tables[0].Parameters.Cells) cell.Value = 0.5;
        var scaled = settings.Create(parent);
        if (scaled is simpleSorterModelMutator.SmmMsuf4RandMutate uf4)
        {
            Assert.Equal(0.8, uf4.Item.Uf4MutationRates.SeedOpsTransitionRates.OrthoRates.NoActionRate, 12);
            Assert.All(uf4.Item.Uf4MutationRates.TwoOrbitPairOpsTransitionRates.RatesArray,
                rate => Assert.Equal(0.8, rate.ParaRates.NoActionRate, 12));
        }
        else
        {
            var uf6 = Assert.IsType<simpleSorterModelMutator.SmmMsuf6RandMutate>(scaled);
            Assert.Equal(0.8, uf6.Item.Uf6MutationRates.Seed6TransitionRates.Para4Rates.NoActionRate, 12);
            Assert.All(uf6.Item.Uf6MutationRates.OpsTransitionRates.RatesArray,
                rate => Assert.Equal(0.8, rate.SelfReflRates.NoActionRate, 12));
        }
    }

    private static int[][][] Modes(simpleSorterModel model) => model switch
    {
        simpleSorterModel.Msuf4 uf4 => uf4.Item.TwoOrbitUnfolder4s.Select(stage =>
            new[] { new[] { stage.SeedTwoOrbitPairType.Tag } }.Concat(stage.TwoOrbitUnfolderSteps
                .Select(step => step.TwoOrbitPairTypes.Select(mode => mode.Tag).ToArray())).ToArray()).ToArray(),
        simpleSorterModel.Msuf6 uf6 => uf6.Item.TwoOrbitUnfolder6s.Select(stage =>
            new[] { new[] { stage.Seed6TwoOrbitType.Tag } }.Concat(stage.TwoOrbitUnfolderSteps
                .Select(step => step.TwoOrbitPairTypes.Select(mode => mode.Tag).ToArray())).ToArray()).ToArray(),
        _ => throw new ArgumentException("Expected an unfolding model.")
    };

    [Fact]
    public void Unfolding_configuration_preserves_shared_steps_when_the_width_changes()
    {
        var settings = new SorterMutationSettings();
        settings.SelectMethod("msuf6", 24);
        settings.OverallRate.Value = 0.2;
        settings.ParameterTables[0].Parameters.Cells[1].Value = 0.35;
        var firstUnfolding = settings.ParameterTables[2];
        firstUnfolding.Parameters.Cells[3].Value = 0.45;
        settings.SelectedTable = firstUnfolding;
        settings.SelectMethod("msuf6", 48);
        Assert.Equal(0.2, settings.OverallRate.Value);
        Assert.Equal(0.35, settings.ParameterTables[0].Parameters.Cells[1].Value);
        Assert.Same(firstUnfolding.Parameters, settings.SelectedTable!.Parameters);
        Assert.Equal(0.45, settings.ParameterTables[2].Parameters.Cells[3].Value);
        Assert.Equal(1, settings.ParameterTables[0].Parameters.Cells[3].Value);
        settings.SelectMethod("msuf6", 12);
        Assert.Equal(3, settings.ParameterTables.Count);
        Assert.Equal(0.35, settings.ParameterTables[0].Parameters.Cells[1].Value);
    }

    [Fact]
    public void Explicit_unfolding_factories_validate_dimensions_rates_and_step_counts()
    {
        var seed = new unfoldingMutationStep(1, new double[3, 3]);
        var step = new unfoldingMutationStep(1, new double[3, 3]);
        simpleSorterModelMutator Make(int width, double overall, unfoldingMutationStep s, params unfoldingMutationStep[] steps) =>
            SimpleSorterModelMutator.getMsuf4ModelMutatorByStep(width, rngFactory.SmxFactory, overall, s, steps, mutatorVariant.V1);
        Assert.Throws<ArgumentException>(() => Make(16, 1, seed, step));
        Assert.Throws<ArgumentException>(() => Make(12, 1, seed, step));
        Assert.Throws<ArgumentException>(() => Make(8, 1, new(1, new double[2, 3]), step));
        Assert.Throws<ArgumentException>(() => Make(8, double.NaN, seed, step));
        Assert.Throws<ArgumentException>(() => Make(8, 1, new(double.PositiveInfinity, new double[3, 3]), step));
        seed.TransitionWeights[0, 0] = -1;
        Assert.Throws<ArgumentException>(() => Make(8, 1, seed, step));
        seed.TransitionWeights[0, 0] = 0;
        var zero = Assert.IsType<simpleSorterModelMutator.SmmMsuf4RandMutate>(Make(8, 1, seed, step));
        Assert.Equal(1, zero.Item.Uf4MutationRates.SeedOpsTransitionRates.OrthoRates.NoActionRate);
        Assert.Equal(1, zero.Item.Uf4MutationRates.TwoOrbitPairOpsTransitionRates.RatesArray[0].ParaRates.NoActionRate);
    }

    [Theory]
    [InlineData("msce", 8, "V1")]
    [InlineData("mssi", 8, "V1")]
    [InlineData("mssi", 8, "V2")]
    [InlineData("msrs", 8, "V1")]
    public void Every_control_parameter_reaches_the_native_mutator(string method, int width, string variant)
    {
        var model = RandomNetwork.generateModel(method, width, 12, Guid.Parse("8741ece5-b169-45ba-8556-c87329db496d"));
        var settings = new SorterMutationSettings();
        settings.SelectMethod(method, width);
        settings.Variant = variant;
        settings.ExcludeSelf = false;
        var table = settings.ParameterTables[0].Parameters;
        Assert.Equal(settings.Parameters.Skip(1).Select(p => p.Name), table.ColumnHeaders);
        static double SliderPosition(int i) => -3 + i * 0.5;
        settings.OverallRate.LogValue = SliderPosition(0);
        for (int i = 1; i < settings.Parameters.Count; i++) table.Cells[i - 1].SliderValue = SliderPosition(i);
        static double P(int i) => Math.Pow(10, SliderPosition(i));
        var rng = rngFactory.SmxFactory;
        var v = variant == "V2" ? mutatorVariant.V2 : mutatorVariant.V1;
        var expected = method switch
        {
            "msce" => SimpleSorterModelMutator.getMsceModelMutator(rng, false, P(0), P(1), P(2), P(3), v),
            "mssi" => SimpleSorterModelMutator.getMssiModelMutator(rng, true, P(0), P(1), P(2), v),
            _ => SimpleSorterModelMutator.getMsrsModelMutator(rng, true, P(0), P(1), P(2), P(3), v)
        };
        var actual = settings.Create(model);
        Assert.Equal(expected, actual);
        var expectedChild = SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(expected, model, 3, 7);
        var child = SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(actual, model, 3, 7);
        Assert.Equal(expectedChild, child);
        Assert.Equal(SimpleSorterModel.getId(expectedChild), SimpleSorterModelMutator.makeMutantSorterModelIdFromIndexAndMod(actual, model, 3, 7));
    }

    [Fact]
    public void Invalid_family_and_unimplemented_variants_are_rejected()
    {
        var settings = new SorterMutationSettings();
        settings.SelectMethod("msce");
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Variant = "V3");
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.SelectMethod("unknown"));
        Assert.Throws<InvalidOperationException>(() => settings.Create(RandomNetwork.generateModel("mssi", 8, 3, Guid.NewGuid())));
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => settings.Parameters[0].Value = invalid);
    }
}
