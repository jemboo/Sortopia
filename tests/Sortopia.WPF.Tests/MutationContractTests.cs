using Sortopia.Core;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.WPF.ViewModels;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class MutationContractTests
{
    [Theory]
    [InlineData("msce", 8, "V1")]
    [InlineData("mssi", 8, "V1")]
    [InlineData("mssi", 8, "V2")]
    [InlineData("msrs", 8, "V1")]
    [InlineData("msuf4", 16, "V1")]
    [InlineData("msuf4", 16, "V2")]
    [InlineData("msuf6", 24, "V1")]
    public void Every_control_parameter_reaches_the_native_mutator(string method, int width, string variant)
    {
        var model = RandomNetwork.generateModel(method, width, 12, Guid.Parse("8741ece5-b169-45ba-8556-c87329db496d"));
        var settings = new SorterMutationSettings();
        settings.SelectMethod(method);
        settings.Variant = variant;
        settings.ExcludeSelf = false;
        for (int i = 0; i < settings.Parameters.Count; i++) settings.Parameters[i].Value = (i + 1) * 0.13;
        double P(int i) => settings.Parameters[i].Value;
        var rng = rngFactory.SmxFactory;
        var v = variant == "V2" ? mutatorVariant.V2 : mutatorVariant.V1;
        var expected = method switch
        {
            "msce" => SimpleSorterModelMutator.getMsceModelMutator(rng, false, P(0), P(1), P(2), P(3), v),
            "mssi" => SimpleSorterModelMutator.getMssiModelMutator(rng, true, P(0), P(1), P(2), v),
            "msrs" => SimpleSorterModelMutator.getMsrsModelMutator(rng, true, P(0), P(1), P(2), P(3), v),
            "msuf4" => SimpleSorterModelMutator.getMsuf4ModelMutator(width, rng, true, P(1), P(0), P(2), P(3), P(4), v),
            _ => SimpleSorterModelMutator.getMsuf6ModelMutator(width, rng, true, P(1), P(0), P(2), P(3), P(4), v)
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
