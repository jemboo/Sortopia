using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Networks;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class EvaluationInputCompatibilityTests
{
    private static functionData.SortableTests Tests(bool prefixed)
    {
        var parameters = prefixed ? parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.mergeLibIdKey, MergeLibId.availableForChannelCount(4).First(value => value.MergeDimension == 2)),
            ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")])
            : parameterSet.fromBindings([ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")]);
        return Assert.IsType<functionData.SortableTests>(Assert.Single(CatalogTestHost.Invoke(false,
            prefixed ? ResultsFunctionNames.mergeSortableTests : ResultsFunctionNames.standardSortableTests, parameters)));
    }

    private static functionData Models(ce[] prefix)
    {
        var generated = Assert.IsType<functionData.SorterModelSet>(Assert.Single(CatalogTestHost.Invoke(false,
            ResultsFunctionNames.uniformMssiSimpleSorterModels,
            parameterSet.fromBindings(RunExecutionTests.UniformModelDefaults().Select(ParameterBindingDto.toDomain))))).Item;
        var models = generated.SorterModels.Cast<sorterModel.Simple>()
            .Select(model => sorterModel.NewSimple(SimpleSorterModel.withPrefix(prefix, model.Item))).ToArray();
        return functionData.NewSorterModelSet(sorterModelSet.create(Guid.NewGuid(), models, generated.RawCeLength));
    }

    private static FSharpList<functionData> Invoke(string phase, functionData models, functionData tests)
    {
        var setup = phase == "copy";
        var name = setup ? SetupFunctionNames.evaluationSource : phase == "evaluate" ? ResultsFunctionNames.sorterEval : ResultsFunctionNames.mutantEval;
        var parameters = setup ? parameterSet.fromBindings([]) : parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.sorterEvalTypeKey, "V2"),
            .. phase == "evaluate" ? new[] { ParameterBinding.create(ParamKeys.collectSortableTestsKey, false) } : Array.Empty<parameterBinding>()]);
        return CatalogTestHost.Invoke(setup, name, parameters, models, tests);
    }

    public static IEnumerable<object[]> CompatibleInputs =>
        from phase in new[] { "copy", "evaluate", "mutants" }
        from modelPrefix in new[] { false, true }
        from testsPrefix in new[] { false, true }
        select new object[] { phase, modelPrefix, testsPrefix };

    [Theory]
    [MemberData(nameof(CompatibleInputs))]
    public void Matching_prefixes_and_either_empty_prefix_remain_compatible(string phase, bool modelPrefix, bool testsPrefix)
    {
        var tests = Tests(testsPrefix);
        var models = Models(modelPrefix ? Tests(true).Item2 : []);
        var outputs = Invoke(phase, models, tests);
        if (phase == "copy")
        {
            Assert.Same(models, outputs.Single(value => value.IsSorterModelSet));
            Assert.Same(tests, outputs.Single(value => value.IsSortableTests));
        }
        else Assert.Equal(3, Assert.IsType<functionData.SorterSetEval>(Assert.Single(outputs)).Item.SorterEvals.Length);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("evaluate")]
    [InlineData("mutants")]
    public void Different_nonempty_prefixes_keep_the_existing_errors(string phase)
    {
        var tests = Tests(true);
        var models = Models(tests.Item2.Reverse().ToArray());
        var error = Assert.Throws<ArgumentException>(() => Invoke(phase, models, tests));
        Assert.Equal("inputs", error.ParamName);
        Assert.StartsWith(phase == "copy" ? "The source sorter model prefix must match the source SortableTests prefix."
            : "The model prefix must match the source SortableTests prefix. Regenerate models using the associated tests.", error.Message);
    }
}
