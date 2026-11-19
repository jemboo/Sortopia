using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionValidationRulesTests
{
    [Theory]
    [InlineData("sorter-model-set.uniform.mssi.simple-r", "AMax", "-1")]
    [InlineData("sorter-model-set.uniform.mssi.simple-s", "ActivationProbability", "1.1")]
    [InlineData("sorter-model-set.uniform.msuf4.simple", "OrthoRate", "-1")]
    [InlineData("sorter-model-set.uniform.msuf4.simple", "ChannelCount", "6")]
    [InlineData("sorter-model-set.uniform.msuf6.simple", "ChannelCount", "8")]
    [InlineData("sorter-model-set.uniform.mssi.simple", "SorterCount", "2147483647")]
    [InlineData("sorter-model-set.uniform.mssi.simple", "Repl", "-1")]
    [InlineData("sorter-model-set.uniform.mssi.simple", "RngType", "unknown")]
    [InlineData("sorter-model-set.uniform.msuf6.simple", "Seed6Weights", "0,0,0,0,0,0,0")]
    [InlineData("sorter-model-set.uniform.msuf4.simple", "OrthoRate", "NaN")]
    public void Generation_fields_defaults_and_spans_share_validation(string function, string key, string invalid)
    {
        Check(SetupFunctionCatalog.require(function));
        Check(ResultsFunctionCatalog.require(function));
        void Check<T>(functionDefinition<T> definition)
        {
            var type = ParamKeys.valueType(key);
            var field = new WorkspaceTypedValue(invalid, () => type, key: () => key, support: () => definition.ParameterSupport);
            Assert.NotEmpty(field.Error);
            var spans = ListModule.Singleton(ParameterSpan.fromTexts(key, type, [invalid]));
            Assert.Equal(field.Error, definition.InputError.Invoke([]).Invoke(spans));
            // Malformed typed values cannot be constructed as defaults in the first place.
            if (ParameterValue.validationError(type, invalid).Length == 0)
                Assert.Equal(field.Error, definition.InputError.Invoke([ParameterBinding.fromText(key, type, invalid)]).Invoke([]));
        }
    }

    [Theory]
    [InlineData("InitialPoolSeed", "-1")]
    [InlineData("InitialPoolSelection", "unknown")]
    [InlineData("GenerationCount", "-1")]
    [InlineData("SorterPoolCount", "0")]
    [InlineData("SelectedSorterCountPerPool", "-1")]
    [InlineData("SortedFraction", "0")]
    [InlineData("ModificationRate", "1.1")]
    [InlineData("SeedModificationRate", "-1")]
    [InlineData("OrthoRate", "-1")]
    [InlineData("MutatorVariantKey", "V3")]
    [InlineData("SorterEvalMeasure", "unknown")]
    public void Sgd_fields_and_execution_validation_use_the_same_rules(string key, string invalid)
    {
        var definition = SgdFunctions.standardMsuf4Simple;
        var descriptor = definition.RequiredParameters.Single(value => value.Name == key);
        var field = new WorkspaceTypedValue(invalid, () => descriptor.ValueType, key: () => key, support: () => definition.ParameterSupport);
        Assert.NotEmpty(field.Error);
        Assert.Equal(field.Error, definition.InputError.Invoke([ParameterBinding.fromText(key, descriptor.ValueType, invalid)]).Invoke([]));
        Assert.Equal(field.Error, definition.InputError.Invoke([]).Invoke([ParameterSpan.fromTexts(key, descriptor.ValueType, [invalid])]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Function_builders_validate_custom_field_rules_before_combinations_or_execution(bool setup)
    {
        var empty = FunctionParameterSupport.empty;
        var rule = new parameterEditorRule([], "Batch width must be odd.", FuncConvert.FromFunc<string, bool>(text => int.Parse(text) % 2 == 1));
        var required = ListModule.Singleton(new paramKeyDescriptor("BatchWidth", parameterValueType.Integer));
        var outputs = ListModule.Singleton(Tuple.Create(sortopiaDataType.NewSorterModelSet(""), ListModule.Singleton("BatchWidth")));
        var combinations = 0;
        var cross = FuncConvert.FromFunc<FSharpList<parameterBinding>, FSharpFunc<FSharpList<parameterSpan>, string>>(_ =>
            FuncConvert.FromFunc<FSharpList<parameterSpan>, string>(_ => { combinations++; return "Combination rejected."; }));
        var support = new functionParameterSupport(empty.Defaults, empty.Guidance, empty.Preview, MapModule.OfSeq(new[] { Tuple.Create("BatchWidth", rule) }), cross);

        void Check<T>(functionDefinition<T> definition, Action<runHost> execute)
        {
            var binding = ParameterBinding.fromText("BatchWidth", parameterValueType.Integer, "2");
            var error = "BatchWidth: Batch width must be odd.";
            Assert.Equal(error, definition.InputError.Invoke([binding]).Invoke([]));
            Assert.Equal(error, definition.InputError.Invoke([]).Invoke([ParameterSpan.fromTexts("BatchWidth", parameterValueType.Integer, ["1", "2"])]));
            Assert.Equal(0, combinations);
            var host = new runHost(setup ? runExecutionPhase.Setup : runExecutionPhase.Results, parameterSet.fromBindings([binding]),
                null!, null!, CancellationToken.None, 1, null, null);
            Assert.Contains(error, Assert.Throws<ArgumentException>(() => execute(host)).Message);
            Assert.Equal(0, combinations);
            Assert.Equal("Combination rejected.", definition.InputError.Invoke([ParameterBinding.fromText("BatchWidth", parameterValueType.Integer, "3")]).Invoke([]));
            Assert.Equal(1, combinations);
        }
        if (setup)
        {
            var definition = FunctionDefinition.setup("1", setupParameterRequirements.NewDeclared(required), outputs, [], support,
                FuncConvert.FromFunc<runHost, FSharpFunc<FSharpMap<sortopiaDataType, Guid>, Unit>>(_ => throw new InvalidOperationException("Execution must not start.")));
            Check(definition, host => definition.Execute.Invoke(host).Invoke(MapModule.Empty<sortopiaDataType, Guid>()));
        }
        else
        {
            var definition = FunctionDefinition.results("1", required, outputs, [], support,
                FuncConvert.FromFunc<runHost, Unit>(_ => throw new InvalidOperationException("Execution must not start.")));
            Check(definition, host => definition.Execute.Invoke(host));
        }
    }

    [Fact]
    public void Copy_inputs_share_field_rules_without_generator_combination_constraints()
    {
        var copy = SetupFunctions.evaluationSource;
        var validFields = ListModule.OfArray(new[] {
            ParameterBinding.create(ParamKeys.aMaxKey, 1), ParameterBinding.create(ParamKeys.aInitialKey, 2),
            ParameterBinding.create(ParamKeys.orthoRateKey, 0.0), ParameterBinding.create(ParamKeys.paraRateKey, 0.0),
            ParameterBinding.create(ParamKeys.selfSymRateKey, 0.0), ParameterBinding.create(ParamKeys.channelCountKey, 6),
            ParameterBinding.fromText("UnfoldingWeights", parameterValueType.UnfoldingWeights, "1,1,1;1,1,1") });
        Assert.Empty(copy.InputError.Invoke(validFields).Invoke([]));
        Assert.Empty(copy.InputError.Invoke([]).Invoke(ListModule.OfSeq(validFields.Select(binding =>
            ParameterSpan.fromTexts(binding.KeyName, binding.ValueType, [binding.FormattedValue])))));
        Assert.Contains("[-AMax, AMax]", SetupFunctionCatalog.require(SetupFunctionNames.uniformMssiSimpleRSorterModels).InputError.Invoke(validFields).Invoke([]));

        var bad = ParameterBinding.create(ParamKeys.aMaxKey, -1);
        var fieldError = FunctionParameterSupport.valueError(copy.ParameterSupport, bad.KeyName, bad.ValueType, bad.FormattedValue);
        Assert.NotEmpty(fieldError);
        Assert.Equal(fieldError, copy.InputError.Invoke([bad]).Invoke([]));
        var host = new runHost(runExecutionPhase.Setup, parameterSet.fromBindings([bad]), null!, null!, CancellationToken.None, 1, null, null);
        Assert.Contains(fieldError, Assert.Throws<ArgumentException>(() => copy.Execute.Invoke(host)
            .Invoke(MapModule.OfSeq(copy.SourceDataTypes.Select(type => Tuple.Create(type, Guid.NewGuid()))))).Message);
    }

    [Fact]
    public void Sgd_combination_checks_still_examine_all_candidates_with_partial_inputs()
    {
        var definition = SgdFunctions.standardMsuf4Simple;
        Assert.Empty(definition.InputError.Invoke([ParameterBinding.create(ParamKeys.selectedSorterCountPerPoolKey, 2)]).Invoke([]));
        var retained = ParameterSpan.create(ParamKeys.selectedSorterCountPerPoolKey, ListModule.OfSeq(Enumerable.Repeat(0, 60).Append(2)));
        Assert.Contains("Cannot exceed", definition.InputError.Invoke([ParameterBinding.create(ParamKeys.sorterCountPerPoolKey, 1)]).Invoke([retained]));
        var rates = ListModule.OfSeq(new[] { ParamKeys.orthoRateKey, ParamKeys.paraRateKey, ParamKeys.selfSymRateKey }
            .Select(key => ParameterSpan.create(key, ListModule.OfSeq(Enumerable.Repeat(1.0, 60).Append(0.0)))));
        Assert.Contains("At least one mutation weight", definition.InputError.Invoke([]).Invoke(rates));
    }
}
