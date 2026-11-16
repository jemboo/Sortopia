using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Runs;
using Sortopia.Sorting;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionMetadataTests
{
    // Used by the existing catalog matrix tests for all 30 setup and 45 results generators.
    internal static void AssertModelMetadata(modelGenerationMetadata? metadata, string algorithm, string family, bool prefix)
    {
        Assert.NotNull(metadata);
        Assert.Equal(SimpleSorterModelType.fromString(algorithm), metadata.Algorithm);
        Assert.Equal(family, metadata.Family.ToString());
        Assert.Equal(prefix ? modelPrefixMode.CatalogPrefix : modelPrefixMode.NoPrefix, metadata.PrefixMode);
    }

    [Fact]
    public void Copy_evaluation_and_test_functions_do_not_claim_fixed_model_generation_settings()
    {
        foreach (var name in new[] { SetupFunctionNames.evaluationSource, SetupFunctionNames.standardTests })
        {
            var definition = SetupFunctionCatalog.require(name);
            Assert.Null(definition.ModelGeneration);
            Assert.Null(ModelGenerationStageLengths.tryForDefinition(definition, parameterSet.fromBindings([])));
        }
        foreach (var name in new[] { ResultsFunctionNames.sorterEval, ResultsFunctionNames.mutantEval,
                     ResultsFunctionNames.standardSortableTests, ResultsFunctionNames.prefixSortableTests, ResultsFunctionNames.mergeSortableTests })
        {
            var definition = ResultsFunctionCatalog.require(name);
            Assert.Null(definition.ModelGeneration);
            Assert.Null(ModelGenerationStageLengths.tryForDefinition(definition, parameterSet.fromBindings([])));
        }
        Assert.Equal(30, SetupFunctionCatalog.functions.Values.Count(value => value.ModelGeneration is not null));
        Assert.Equal(45, ResultsFunctionCatalog.functions.Values.Count(value => value.ModelGeneration is not null));
    }

    [Theory]
    [InlineData("Simple", false)]
    [InlineData("SimpleR", false)]
    [InlineData("SimpleS", false)]
    [InlineData("Simple", true)]
    [InlineData("SimpleR", true)]
    [InlineData("SimpleS", true)]
    public void Sizing_and_preview_work_from_unregistered_definitions_without_a_function_name(string familyName, bool prefix)
    {
        var family = familyName switch { "SimpleR" => modelFamily.SimpleR, "SimpleS" => modelFamily.SimpleS, _ => modelFamily.Simple };
        var definition = prefix ? ResultsFunctions.prefixMssiSorterModels.Invoke(family) : ResultsFunctions.uniformMssiSorterModels.Invoke(family);
        var parameters = parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.sorterSuccessFractionKey, sorterSuccessFraction.pct50),
            prefix ? ParameterBinding.create(ParamKeys.prefixLibIdKey, prefixLibId.create(8, 3, prefixLibVariant.PrefixA))
                : ParameterBinding.create(ParamKeys.channelCountKey, 8)
        ]);
        var expected = prefix ? 19 : 22;
        Assert.Equal(expected, ModelGenerationStageLengths.tryForDefinition(definition, parameters).Value);
        var defaults = parameters.ParamMap.Values.Select(ParameterBindingDto.fromDomain).ToArray();
        var preview = WorkspaceParameterSetsPreview.Create(defaults, [], "", definition.ModelGeneration.Value);
        Assert.Empty(preview.Error);
        Assert.Equal(expected, Assert.Single(preview.Rows).SuffixStages);
        Assert.Null(Assert.Single(WorkspaceParameterSetsPreview.Create(defaults, [], "").Rows).SuffixStages);
    }

    [Fact]
    public void Initial_width_and_unfolding_defaults_use_the_declared_algorithm()
    {
        var uf6 = SetupFunctions.uniformMsuf6SorterModels.Invoke(modelFamily.SimpleS).ModelGeneration.Value;
        var uf4 = SetupFunctions.uniformMsuf4SorterModels.Invoke(modelFamily.SimpleS).ModelGeneration.Value;
        Assert.Equal(12, WorkspaceParameterInitialValues.SuggestedChannelCount(8, uf6));
        Assert.Equal(24, WorkspaceParameterInitialValues.SuggestedChannelCount(24, uf6));
        Assert.Equal(8, WorkspaceParameterInitialValues.SuggestedChannelCount(12, uf4));
        Assert.Equal(8, WorkspaceParameterInitialValues.SuggestedChannelCount(8, null));
        Assert.Equal("12", WorkspaceParameterInitialValues.For(ParamKey.describe(ParamKeys.channelCountKey), 0, uf6));
        Assert.Equal("8", WorkspaceParameterInitialValues.For(ParamKey.describe(ParamKeys.channelCountKey), 0, uf4));
        Assert.Equal("1,1,1;1,1,1", WorkspaceParameterInitialValues.For(ParamKey.describe(ParamKeys.unfoldingWeightsKey), 24, uf6));
    }
}
