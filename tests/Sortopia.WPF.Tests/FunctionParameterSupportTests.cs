using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionParameterSupportTests
{
    internal static int? SuffixStages(WorkspaceParameterSetRow row) =>
        row.Previews.FirstOrDefault(value => value.Key == "SuffixStages")?.Value is parameterValue.IntegerValue integer ? integer.Item : null;

    [Fact]
    public void Copy_evaluation_and_test_functions_do_not_assume_generation_previews()
    {
        foreach (var name in new[] { SetupFunctionNames.evaluationSource, SetupFunctionNames.standardTests })
            Assert.Empty(SetupFunctionCatalog.require(name).ParameterSupport.Preview.Invoke(parameterSet.fromBindings([])));
        foreach (var name in new[] { ResultsFunctionNames.sorterEval, ResultsFunctionNames.mutantEval,
                     ResultsFunctionNames.standardSortableTests, ResultsFunctionNames.prefixSortableTests, ResultsFunctionNames.mergeSortableTests })
            Assert.Empty(ResultsFunctionCatalog.require(name).ParameterSupport.Preview.Invoke(parameterSet.fromBindings([])));
    }

    [Theory]
    [InlineData("Simple", false)]
    [InlineData("SimpleR", false)]
    [InlineData("SimpleS", false)]
    [InlineData("Simple", true)]
    [InlineData("SimpleR", true)]
    [InlineData("SimpleS", true)]
    public void Function_providers_supply_sizing_and_preview_without_exposing_model_choices(string family, bool prefix)
    {
        var group = prefix ? "sorter-model-set.prefix" : "sorter-model-set.uniform";
        var definition = ResultsFunctionCatalog.require($"{group}.mssi{ModelFamilyResultsTests.Suffix(family)}");
        var parameters = parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.sorterSuccessFractionKey, sorterSuccessFraction.pct50),
            prefix ? ParameterBinding.create(ParamKeys.prefixLibIdKey, prefixLibId.create(8, 3, prefixLibVariant.PrefixA))
                : ParameterBinding.create(ParamKeys.channelCountKey, 8)
        ]);
        var expected = prefix ? 19 : 22;
        Assert.Equal(expected, Assert.IsType<parameterValue.IntegerValue>(FunctionDefinition.tryPreviewValue("SuffixStages", definition, parameters).Value).Item);
        var defaults = parameters.ParamMap.Values.Select(ParameterBindingDto.fromDomain).ToArray();
        var preview = WorkspaceParameterSetsPreview.Create(defaults, [], "", definition.ParameterSupport);
        Assert.Empty(preview.Error);
        Assert.Equal(expected, SuffixStages(Assert.Single(preview.Rows)));
        Assert.Null(SuffixStages(Assert.Single(WorkspaceParameterSetsPreview.Create(defaults, [], "").Rows)));
        Assert.Contains(family, definition.ParameterSupport.Guidance.Invoke(ListModule.OfSeq(parameters.ParamMap.Values)).Invoke(ListModule.Empty<parameterSpan>()));
    }

    [Fact]
    public void Function_defaults_respect_existing_widths_and_choose_compatible_missing_values()
    {
        var uf6 = SetupFunctionCatalog.require(SetupFunctionNames.uniformMsuf6SimpleSSorterModels).ParameterSupport;
        var uf4 = SetupFunctionCatalog.require(SetupFunctionNames.uniformMsuf4SimpleSSorterModels).ParameterSupport;
        parameterBinding[] Suggest(functionParameterSupport support, int channels, bool missingWidth) => support.Defaults.Invoke(new(
            ListModule.OfSeq(channels == 0 ? [] : new[] { ParameterBinding.create(ParamKeys.channelCountKey, channels) }),
            ListModule.Empty<parameterBinding>(), ListModule.OfSeq(missingWidth
                ? new[] { ParamKey.describe(ParamKeys.channelCountKey), ParamKey.describe(ParamKeys.unfoldingWeightsKey) }
                : new[] { ParamKey.describe(ParamKeys.unfoldingWeightsKey) }))).ToArray();
        Assert.Equal("12", Suggest(uf6, 8, true)[0].FormattedValue);
        Assert.Equal("24", Suggest(uf6, 24, true)[0].FormattedValue);
        Assert.Equal("8", Suggest(uf4, 12, true)[0].FormattedValue);
        Assert.Equal("12", Suggest(uf6, 0, true)[0].FormattedValue);
        Assert.Equal("8", Suggest(uf4, 0, true)[0].FormattedValue);
        Assert.Equal("1,1,1;1,1,1", Assert.Single(Suggest(uf6, 24, false)).FormattedValue);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("simple-r")]
    [InlineData("simple-s")]
    public void Constructor_defaults_are_provided_without_UI_fallback_and_only_for_missing_keys(string constructor)
    {
        var expected = new Dictionary<string, string> { ["SorterCount"] = "100" };
        if (constructor == "simple-r") { expected["AMax"] = "1"; expected["AInitial"] = "0"; }
        if (constructor == "simple-s") expected["ActivationProbability"] = "0.5";
        var providers = SetupFunctionCatalog.functions.Where(entry => entry.Value.SortopiaDataTypes.Any(type => type.IsSorterModelSet)
                && entry.Key.EndsWith($".{constructor}", StringComparison.Ordinal))
            .Select(entry => (entry.Value.RequiredParameters, entry.Value.ParameterSupport))
            .Concat(ResultsFunctionCatalog.functions.Where(entry => entry.Value.SortopiaDataTypes.Any(type => type.IsSorterModelSet)
                    && entry.Key.EndsWith($".{constructor}", StringComparison.Ordinal))
                .Select(entry => (entry.Value.RequiredParameters, entry.Value.ParameterSupport))).ToArray();
        Assert.NotEmpty(providers);
        foreach (var (required, support) in providers)
        {
            var missing = required.Where(key => expected.ContainsKey(key.Name)).Reverse().ToArray();
            var suggested = support.Defaults.Invoke(new([], [], ListModule.OfArray(missing))).ToArray();
            Assert.Equal(expected.Count, suggested.Length);
            Assert.Equal(missing.Select(key => key.Name), suggested.Select(binding => binding.KeyName));
            Assert.All(suggested, binding => Assert.Equal(expected[binding.KeyName], binding.FormattedValue));

            // Already supplied values must not be proposed again, and an empty missing list stays empty.
            var remaining = support.Defaults.Invoke(new(ListModule.OfArray(suggested[..1]), [], ListModule.OfArray(missing[1..])));
            Assert.Equal(suggested.Skip(1), remaining);
            Assert.Empty(support.Defaults.Invoke(new(ListModule.OfArray(suggested), [], [])));
        }
    }

    [Fact]
    public void Copy_provider_supplies_registered_model_defaults_for_source_query_keys()
    {
        var expected = new Dictionary<string, string> {
            ["AMax"] = "1", ["AInitial"] = "0", ["ActivationProbability"] = "0.5", ["ExcludeSelfCe"] = "True",
            ["OrthoRate"] = "1", ["ParaRate"] = "1", ["SelfSymRate"] = "1", ["StageWeights"] = "1,1,1",
            ["Seed4Weights"] = "1,1,1", ["Seed6Weights"] = "1,1,1,1,1,1,1", ["UnfoldingWeights"] = "1,1,1;1,1,1",
            ["SorterSuccessFraction"] = "99%", ["SorterCount"] = "100", ["StageLength"] = "1", ["RngType"] = "SplitMix64", ["Repl"] = "0"
        };
        var keys = expected.Keys.Select(key => ParamKeys.tryFind(key).Value).ToArray();
        var support = SetupFunctionCatalog.require(SetupFunctionNames.evaluationSource).ParameterSupport;
        var context = new parameterDefaultContext([], ListModule.OfArray(new[] { ParameterBinding.create(ParamKeys.channelCountKey, 24) }), ListModule.OfArray(keys));
        var suggestions = support.Defaults.Invoke(context).ToArray();
        Assert.Equal(keys.Select(key => key.Name), suggestions.Select(binding => binding.KeyName));
        Assert.All(suggestions, binding => Assert.Equal(expected[binding.KeyName], binding.FormattedValue));
        Assert.Empty(support.Preview.Invoke(parameterSet.fromBindings(suggestions)));

        var related = context.Related.Select(ParameterBindingDto.fromDomain).ToArray();
        var additions = WorkspaceParameterInitialValues.CreateMissing([], related, keys, support);
        Assert.All(additions, binding => Assert.Equal(expected[binding.keyName], binding.value));
    }

    [Fact]
    public void UI_without_a_provider_does_not_guess_model_defaults()
    {
        var keys = new[] { ParamKey.describe(ParamKeys.activationProbabilityKey), ParamKey.describe(ParamKeys.excludeSelfCeKey),
            ParamKey.describe(ParamKeys.rngTypeKey), ParamKey.describe(ParamKeys.stageWeightsKey), ParamKey.describe(ParamKeys.prefixLibIdKey) };
        var proposed = WorkspaceParameterInitialValues.CreateMissing([], [], keys, null);
        Assert.Equal(new[] { "0", "false", "", "", "" }, proposed.Select(binding => binding.value));
    }

    [Fact]
    public void Generic_preview_displays_unrelated_function_values_without_model_metadata()
    {
        var support = new functionParameterSupport(
            FuncConvert.FromFunc<parameterDefaultContext, FSharpList<parameterBinding>>(context => ListModule.OfSeq(
                context.Missing.Select(key => ParameterBinding.fromText(key.Name, key.ValueType, "7")))),
            FuncConvert.FromFunc<FSharpList<parameterBinding>, FSharpFunc<FSharpList<parameterSpan>, string>>(_ =>
                FuncConvert.FromFunc<FSharpList<parameterSpan>, string>(_ => "Custom recipe guidance")),
            FuncConvert.FromFunc<parameterSet, FSharpList<functionPreviewValue>>(_ => ListModule.OfSeq(new[] {
                new functionPreviewValue("WorkUnits", "Estimated work", parameterValue.NewIntegerValue(42), "Recipe-defined work."),
                new functionPreviewValue("Mode", "Execution mode", parameterValue.NewTextValue("custom"), "") })),
            MapModule.Empty<string, parameterEditorRule>(), FunctionParameterSupport.empty.CombinationError);
        var proposed = WorkspaceParameterInitialValues.CreateMissing([], [], [ParamKey.describe(ParamKeys.sorterCountKey)], support);
        Assert.Equal("7", Assert.Single(proposed).value);
        var preview = WorkspaceParameterSetsPreview.Create(proposed, [], "", support);
        Assert.Empty(preview.Error);
        Assert.Equal(new[] { "Estimated work", "Execution mode" }, preview.PreviewColumns.Select(value => value.Label));
        Assert.Equal(new[] { "42", "custom" }, Assert.Single(preview.Rows).PreviewValues);
        Assert.Equal("Recipe-defined work.", preview.PreviewHint);
        Assert.True(preview.HasPreviews);
    }
}
