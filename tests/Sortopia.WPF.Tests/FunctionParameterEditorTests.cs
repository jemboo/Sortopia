using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionParameterEditorTests
{
    [Fact]
    public void Arbitrary_provider_rules_control_defaults_and_spans_and_follow_provider_changes()
    {
        var empty = FunctionParameterSupport.empty;
        var rule = new parameterEditorRule(ListModule.OfArray(new[] { "1", "3" }), "Choose an allowed batch width.",
            FuncConvert.FromFunc<string, bool>(text => text is "1" or "3"));
        var custom = new functionParameterSupport(empty.Defaults, empty.Guidance, empty.Preview,
            MapModule.OfSeq(new[] { Tuple.Create("BatchWidth", rule) }), empty.CombinationError);
        var current = custom;
        var binding = new WorkspaceParameterBinding("BatchWidth", parameterValueType.Integer, "2", _ => { }, support: () => current);
        var span = new WorkspaceParameterSpan("BatchWidth", ["2"], _ => { }, _ => null,
            requirements: () => [new("BatchWidth", parameterValueType.Integer)], support: () => current);
        foreach (var value in new[] { binding.Value, span.Values[0] })
        {
            Assert.Equal(new[] { "1", "3" }, value.Choices);
            Assert.Equal(rule.Hint, value.Hint);
            Assert.Equal($"BatchWidth: {rule.Hint}", value.Error);
            value.Text = "not an integer";
            Assert.Equal(ParameterValue.validationError(parameterValueType.Integer, value.Text), value.Error);
            value.Text = "2";
        }
        Assert.Equal(rule.Hint, span.ValueHint);

        current = empty;
        binding.RefreshSource(); span.RefreshInherited();
        foreach (var value in new[] { binding.Value, span.Values[0] })
        {
            Assert.True(value.IsTextEditor); Assert.Empty(value.Choices); Assert.Empty(value.Error);
            Assert.Equal(ParameterValue.hint(parameterValueType.Integer), value.Hint);
            Assert.Equal("2", value.Text);
        }
        current = custom;
        binding.RefreshSource(); span.RefreshInherited();
        Assert.NotEmpty(binding.Error); Assert.NotEmpty(span.Error);
        binding.Value.Text = "1"; span.Values[0].Text = "3";
        Assert.Empty(binding.Error); Assert.Empty(span.Error);
    }

    [Theory]
    [InlineData("AMax", parameterValueType.Integer, "-1")]
    [InlineData("ActivationProbability", parameterValueType.Floating, "2")]
    [InlineData("OrthoGenRate", parameterValueType.Floating, "-2")]
    [InlineData("RngType", parameterValueType.Text, "custom")]
    public void Without_a_provider_only_the_value_type_supplies_policy(string key, parameterValueType type, string text)
    {
        var value = new WorkspaceTypedValue(text, () => type, key: () => key);
        Assert.Empty(value.Error); Assert.Empty(value.Choices);
        Assert.Equal(ParameterValue.hint(type), value.Hint);
    }

    [Theory]
    [InlineData("sorter-model-set.uniform.mssi.simple-r", "AMax", "-1", "0", "0 or greater")]
    [InlineData("sorter-model-set.uniform.mssi.simple-r", "AInitial", "bad", "-1", "[-AMax, AMax]")]
    [InlineData("sorter-model-set.uniform.mssi.simple-s", "ActivationProbability", "1.1", "0.5", "between 0 and 1")]
    [InlineData("sorter-model-set.uniform.msuf4.simple", "OrthoGenRate", "-1", "0", "normalized together")]
    [InlineData("sorter-model-set.uniform.msuf4.simple", "ParaGenRate", "-1", "0", "normalized together")]
    [InlineData("sorter-model-set.uniform.msuf4.simple", "SelfSymGenRate", "-1", "0", "normalized together")]
    public void Owning_generators_and_copy_functions_supply_the_same_editor_policy(string function, string key, string invalid, string valid, string hint)
    {
        var type = ParamKeys.valueType(key);
        var providers = new[] { SetupFunctionCatalog.require(function).ParameterSupport,
            ResultsFunctionCatalog.require(function).ParameterSupport,
            SetupFunctionCatalog.require(SetupFunctionNames.evaluationSource).ParameterSupport };
        foreach (var support in providers)
        {
            var value = new WorkspaceTypedValue(invalid, () => type, key: () => key, support: () => support);
            Assert.Contains(hint, value.Hint); Assert.NotEmpty(value.Error);
            value.Text = valid; Assert.Empty(value.Error);
        }
        Assert.DoesNotContain(key, SetupFunctionCatalog.require(SetupFunctionNames.uniformMssiSimpleSorterModels).ParameterSupport.EditorRules.Keys);
    }

    [Fact]
    public void Copied_source_keys_keep_typed_editors_and_validate_inherited_defaults_and_spans()
    {
        var models = SourceEvaluationInputsTests.Source("SorterModelSet", "AMax", "AInitial", "OrthoGenRate", "RngType", "Seed6Weights");
        var tests = SourceEvaluationInputsTests.Source("SortableTests", "SortableDataFormat");
        var parent = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Module", () => [models, tests]);
        parent.SetupFunctionName = SetupFunctionNames.evaluationSource;
        foreach (var sourceRow in parent.SetupDataTypes) sourceRow.SourceOverride!.IsOverridden = true;
        parent.SetupDataTypes.Single(row => row.DataType.Kind == "SorterModelSet").SourceProjectId = models.Id;
        parent.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests").SourceProjectId = tests.Id;
        parent.AddRequiredSetupDefaultsCommand.Execute(null);
        var maximum = parent.SetupParameterDefaults.Single(row => row.Key == "AMax").Value;
        Assert.Contains("SplitMix64", parent.SetupParameterDefaults.Single(row => row.Key == "RngType").Value.Choices);
        Assert.True(parent.SetupParameterDefaults.Single(row => row.Key == "Seed6Weights").Value.IsWeights);
        Assert.Contains("[-AMax, AMax]", parent.SetupParameterDefaults.Single(row => row.Key == "AInitial").Value.Hint);
        parent.SetupParameterDefaults.Single(row => row.Key == "OrthoGenRate").RemoveCommand.Execute(null);

        var project = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Project, true, () => parent, () => "Project", () => [models, tests]);
        project.SetupQueryOutputs.Single(row => row.Kind == "SorterModelSet").Keys.Single(row => row.Key == "OrthoGenRate").Selected = true;
        var run = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Run, true, () => project, () => "Run", () => [models, tests]);
        run.RefreshInherited();
        var rate = Assert.Single(run.SetupParameters);
        Assert.Equal("OrthoGenRate", rate.Key); Assert.Contains("normalized together", rate.ValueHint);
        rate.AddValueCommand.Execute(null); rate.Values[0].Text = "-1";
        Assert.True(rate.Values[0].IsNumeric); Assert.NotEmpty(rate.Error);
        Assert.True(run.SetupRequiredInputs.Rows.Single(row => row.Key == "OrthoGenRate").HasIssue);
        rate.Values[0].Text = "1";
        maximum.Text = "-1";
        project.RefreshInherited(); run.RefreshInherited();
        Assert.Contains("AMax", project.SetupParameterDefaultsError);
        Assert.True(run.SetupRequiredInputs.Rows.Single(row => row.Key == "AMax").HasIssue);
        maximum.Text = "2";
        project.RefreshInherited(); run.RefreshInherited();
        Assert.Empty(project.SetupParameterDefaultsError); Assert.Empty(rate.Error);
        Assert.All(run.SetupRequiredInputs.Rows, row => Assert.False(row.HasIssue));
    }

    [Fact]
    public void Sgd_supplies_its_selection_choices_seed_limit_and_mutation_hints()
    {
        var support = SgdFunctions.standardMsuf4Simple.ParameterSupport;
        var selection = new WorkspaceTypedValue("Best", () => parameterValueType.Text, key: () => "InitialPoolSelection", support: () => support);
        Assert.Equal(new[] { "Random", "Best" }, selection.Choices); Assert.Empty(selection.Error);
        Assert.Contains("score ties use model ID", selection.Hint);
        selection.Text = "unknown"; Assert.NotEmpty(selection.Error);
        var seed = new WorkspaceTypedValue("-1", () => parameterValueType.Integer, key: () => "InitialPoolSeed", support: () => support);
        Assert.NotEmpty(seed.Error);
        var rate = new WorkspaceTypedValue("0", () => parameterValueType.Floating, key: () => "OrthoRate", support: () => support);
        Assert.Contains("mutation weight", rate.Hint); Assert.Empty(rate.Error);
    }
}
