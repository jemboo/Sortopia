using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ModelFamilyResultsTests
{
    internal static string Suffix(string family) => family switch { "Simple" => ".simple", "SimpleR" => ".simple-r", "SimpleS" => ".simple-s", _ => throw new ArgumentException(nameof(family)) };
    internal static parameterSet WithFamilyParameters(parameterSet parameters, string family) => family switch {
        "SimpleR" => parameters.WithBinding(ParameterBinding.create(ParamKeys.aMaxKey, 1)).WithBinding(ParameterBinding.create(ParamKeys.aInitialKey, 0)),
        "SimpleS" => parameters.WithBinding(ParameterBinding.create(ParamKeys.activationProbabilityKey, 0.5)),
        _ => parameters
    };
    public static IEnumerable<object[]> Choices =>
        from functionGroup in new[] { "sorter-model-set.prefix", "sorter-model-set.uniform", "sorter-eval-inputs.standard" }
        from algorithm in new[] { "msce", "mssi", "msrs", "msuf4", "msuf6" }
        from family in new[] { "Simple", "SimpleR", "SimpleS" }
        select new object[] { functionGroup, algorithm, family };

    [Theory]
    [MemberData(nameof(Choices))]
    public void Every_results_variant_exposes_only_relevant_inputs_and_projections_and_rejects_old_names(string group, string algorithm, string family)
    {
        var oldName = $"{group}.{algorithm}"; var name = oldName + Suffix(family);
        Assert.False(ResultsFunctionCatalog.functions.ContainsKey(oldName));
        Assert.Throws<ArgumentException>(() => ResultsFunctionCatalog.require(oldName));
        var definition = ResultsFunctionCatalog.require(name);
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models").Editor!;
        editor.ResultsFunctionName = name; editor.AddRequiredResultsDefaultsCommand.Execute(null);
        var keys = definition.RequiredParameters.Select(key => key.Name).ToArray();
        Assert.DoesNotContain("SorterModelType", keys);
        Assert.Equal(family == "SimpleR", keys.Contains("AMax")); Assert.Equal(family == "SimpleR", keys.Contains("AInitial"));
        Assert.Equal(family == "SimpleS", keys.Contains("ActivationProbability"));
        Assert.Equal(keys.Order(), editor.LocalResultsParameterDefaults.Select(row => row.Key).Order());
        Assert.Empty(editor.ResultsParameterDefaultsError);
        Assert.Equal(keys.Where(key => key != "SortableDataFormat").Order(), definition.DataTypes[sortopiaDataType.NewSorterModelSet("")].Order());
        if (group == "sorter-eval-inputs.standard")
            Assert.Equal(new[] { "ChannelCount", "SortableDataFormat" }, definition.DataTypes[sortopiaDataType.NewSortableTests("")]);
        Assert.Contains(family, definition.Description);
        var parameters = parameterSet.fromBindings(editor.ToProjectSettings().resultsParameterDefaults.Select(ParameterBindingDto.toDomain));
        FunctionValidation.validate(definition, parameters);
        Assert.Contains("not used", Assert.Throws<ArgumentException>(() => FunctionValidation.validate(definition,
            parameters.WithBinding(ParameterBinding.create(ParamKeys.sorterModelTypeKey, family)))).Message);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, -3)]
    [InlineData(3, 0)]
    [InlineData(3, 3)]
    [InlineData(int.MaxValue, -int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void SimpleR_initializes_every_activator_to_the_requested_inclusive_start(int maximum, int initial)
    {
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models").Editor!;
        editor.ResultsFunctionName = ResultsFunctionNames.prefixMsuf4SimpleRSorterModels;
        editor.AddRequiredResultsDefaultsCommand.Execute(null);
        editor.ResultsParameterDefaults.Single(row => row.Key == "SorterCount").Value.Text = "2";
        editor.ResultsParameterDefaults.Single(row => row.Key == "AMax").Value.Text = maximum.ToString();
        editor.ResultsParameterDefaults.Single(row => row.Key == "AInitial").Value.Text = initial.ToString();
        Assert.Empty(editor.ResultsParameterDefaultsError);
        var parameters = parameterSet.fromBindings(editor.ToProjectSettings().resultsParameterDefaults.Select(ParameterBindingDto.toDomain));
        var models = Assert.IsType<functionData.SorterModelSet>(Assert.Single(CatalogTestHost.Invoke(false, editor.ResultsFunctionName, parameters))).Item;
        foreach (var model in models.SorterModels)
        {
            var value = Assert.IsType<sorterModel.SimpleR>(model).Item;
            Assert.Equal(maximum, value.AMax); Assert.All(value.Activators, actual => Assert.Equal(initial, actual));
            var prefix = SorterModel.getPrefix(model); var output = SorterModel.makeSorterWithPrefix(model, null!).Ces;
            Assert.Equal(prefix, output.Take(prefix.Length));
            Assert.Equal(initial > 0, output.Length > prefix.Length);
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(2, -3)]
    [InlineData(2, 3)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void Invalid_activator_starts_are_rejected_for_defaults_and_every_span_combination(int maximum, int initial)
    {
        var definition = ResultsFunctionCatalog.require(ResultsFunctionNames.uniformMssiSimpleRSorterModels);
        var defaults = ListModule.OfArray(new[] { ParameterBinding.create(ParamKeys.aMaxKey, maximum), ParameterBinding.create(ParamKeys.aInitialKey, initial) });
        Assert.NotEmpty(definition.InputError.Invoke(defaults).Invoke([]));
        var spans = ListModule.OfArray(new[] {
            ParameterSpan.create(ParamKeys.aInitialKey, ListModule.OfSeq(Enumerable.Repeat(0, 50).Append(initial))),
            ParameterSpan.create(ParamKeys.aMaxKey, ListModule.OfArray(new[] { int.MaxValue, maximum })) });
        Assert.NotEmpty(definition.InputError.Invoke([]).Invoke(spans));
    }
}
