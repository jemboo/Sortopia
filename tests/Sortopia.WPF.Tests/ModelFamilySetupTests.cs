using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ModelFamilySetupTests
{
    public static IEnumerable<object[]> Choices =>
        from functionGroup in new[] { "sorter-model-set.uniform", "sorter-eval.standard", "sorter-eval.merge", "sorter-eval.prefix" }
        from algorithm in new[] { "msce", "mssi", "msrs", "msuf4", "msuf6" }
        from family in new[] { "Simple", "SimpleR", "SimpleS" }
        select new object[] { functionGroup, algorithm, family };

    [Theory]
    [MemberData(nameof(Choices))]
    public void Setup_variants_expose_only_relevant_inputs_and_projections_and_reject_old_names(string group, string algorithm, string family)
    {
        var oldName = $"{group}.{algorithm}";
        var name = oldName + ModelFamilyResultsTests.Suffix(family);
        Assert.False(SetupFunctionCatalog.functions.ContainsKey(oldName));
        Assert.Throws<ArgumentException>(() => SetupFunctionCatalog.require(oldName));
        var definition = SetupFunctionCatalog.require(name);
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: hierarchyKind.Branch).Editor!;
        editor.SetupFunctionName = name; editor.AddRequiredSetupDefaultsCommand.Execute(null);
        var keys = definition.RequiredParameters.Select(key => key.Name).ToArray();
        Assert.DoesNotContain("SorterModelType", keys);
        Assert.Equal(family == "SimpleR", keys.Contains("AMax"));
        Assert.Equal(family == "SimpleR", keys.Contains("AInitial"));
        Assert.Equal(family == "SimpleS", keys.Contains("ActivationProbability"));
        Assert.Equal(keys.Order(), editor.LocalSetupParameterDefaults.Select(row => row.Key).Order());
        Assert.Empty(editor.SetupParameterDefaultsError);
        Assert.Contains(family, definition.Description);
        Assert.Contains(family, editor.SetupInputGuidance);
        var modelsType = sortopiaDataType.NewSorterModelSet("");
        var uniform = SetupFunctionCatalog.require($"sorter-model-set.uniform.{algorithm}{ModelFamilyResultsTests.Suffix(family)}");
        Assert.Equal(uniform.RequiredParameters.Select(key => key.Name), definition.DataTypes[modelsType]);
        if (group != "sorter-model-set.uniform")
        {
            var library = group.EndsWith(".merge") ? "MergeLibId" : group.EndsWith(".prefix") ? "PrefixLibId" : "ChannelCount";
            Assert.Equal(new[] { library, "SortableDataFormat" }.Order(), definition.DataTypes[sortopiaDataType.NewSortableTests("")].Order());
        }
        var parameters = parameterSet.fromBindings(editor.ToProjectSettings().setupParameterDefaults.Select(ParameterBindingDto.toDomain));
        FunctionValidation.validate(definition, parameters);
        var irrelevant = new[] {
            ParameterBinding.create(ParamKeys.sorterModelTypeKey, family),
            ParameterBinding.create(ParamKeys.aMaxKey, 1), ParameterBinding.create(ParamKeys.aInitialKey, 0),
            ParameterBinding.create(ParamKeys.activationProbabilityKey, 0.5)
        }.Where(binding => !keys.Contains(binding.KeyName));
        foreach (var binding in irrelevant)
            Assert.Contains("not used", Assert.Throws<ArgumentException>(() => FunctionValidation.validate(definition, parameters.WithBinding(binding))).Message);
        if (family == "SimpleR")
            Assert.Contains("[-AMax, AMax]", Assert.Throws<ArgumentException>(() => FunctionValidation.validate(definition,
                parameters.WithBinding(ParameterBinding.create(ParamKeys.aInitialKey, 2)))).Message);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(0)]
    [InlineData(2)]
    public void SimpleR_setup_initializes_every_activator_from_AInitial(int initial)
    {
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: hierarchyKind.Branch).Editor!;
        editor.SetupFunctionName = SetupFunctionNames.uniformMssiSimpleRSorterModels;
        editor.AddRequiredSetupDefaultsCommand.Execute(null);
        editor.SetupParameterDefaults.Single(row => row.Key == "SorterCount").Value.Text = "2";
        editor.SetupParameterDefaults.Single(row => row.Key == "AMax").Value.Text = "2";
        editor.SetupParameterDefaults.Single(row => row.Key == "AInitial").Value.Text = initial.ToString();
        var parameters = parameterSet.fromBindings(editor.ToProjectSettings().setupParameterDefaults.Select(ParameterBindingDto.toDomain));
        var models = Assert.IsType<functionData.SorterModelSet>(Assert.Single(CatalogTestHost.Invoke(true, editor.SetupFunctionName, parameters))).Item;
        Assert.All(models.SorterModels, model => Assert.All(Assert.IsType<sorterModel.SimpleR>(model).Item.Activators,
            actual => Assert.Equal(initial, actual)));
    }
}
