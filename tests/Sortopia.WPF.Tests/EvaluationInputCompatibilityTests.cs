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
    private static sortopiaStorageData.SortableTests Tests(bool prefixed)
    {
        var parameters = prefixed ? parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.mergeLibIdKey, MergeLibId.availableForChannelCount(4).First(value => value.MergeDimension == 2)),
            ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")])
            : parameterSet.fromBindings([ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")]);
        return Assert.IsType<sortopiaStorageData.SortableTests>(Assert.Single(CatalogTestHost.Invoke(false,
            prefixed ? ResultsFunctionNames.mergeSortableTests : ResultsFunctionNames.standardSortableTests, parameters)));
    }

    private static sortopiaStorageData Models(ce[] prefix)
    {
        var generated = Assert.IsType<sortopiaStorageData.SorterModelSet>(Assert.Single(CatalogTestHost.Invoke(false,
            ResultsFunctionNames.uniformMssiSimpleSorterModels,
            parameterSet.fromBindings(RunExecutionTests.UniformModelDefaults().Select(ParameterBindingDto.toDomain))))).Item;
        var models = generated.SorterModels.Cast<sorterModel.Simple>()
            .Select(model => sorterModel.NewSimple(SimpleSorterModel.withPrefix(prefix, model.Item))).ToArray();
        return sortopiaStorageData.NewSorterModelSet(sorterModelSet.create(Guid.NewGuid(), models, generated.RawCeLength));
    }

    private static FSharpList<sortopiaStorageData> Invoke(string phase, sortopiaStorageData models, sortopiaStorageData tests)
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
        from modelPrefix in new[] { "empty", "matching", "different" }
        from testsPrefix in new[] { false, true }
        select new object[] { phase, modelPrefix, testsPrefix };

    [Theory]
    [MemberData(nameof(CompatibleInputs))]
    public void Model_prefix_is_independent_of_test_construction(string phase, string modelPrefix, bool testsPrefix)
    {
        var tests = Tests(testsPrefix);
        var models = Models(modelPrefix switch {
            "matching" => [ce.create(0, 1), ce.create(2, 3)],
            "different" => [ce.create(0, 3), ce.create(1, 2)],
            _ => []
        });
        var outputs = Invoke(phase, models, tests);
        if (phase == "copy")
        {
            Assert.Same(models, outputs.Single(value => value.IsSorterModelSet));
            Assert.Same(tests, outputs.Single(value => value.IsSortableTests));
        }
        else Assert.Equal(3, Assert.IsType<sortopiaStorageData.SorterSetEval>(Assert.Single(outputs)).Item.SorterEvals.Length);
    }

}
