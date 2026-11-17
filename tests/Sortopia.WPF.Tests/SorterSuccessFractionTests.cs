using Microsoft.FSharp.Collections;
using Sortopia.Core.Serialization;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Runs;
using Sortopia.Sorting;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class SorterSuccessFractionTests
{
    [Theory]
    [InlineData("pct50", "50%")]
    [InlineData("pct90", "90%")]
    [InlineData("pct99", "99%")]
    public void Typed_parameter_round_trips_and_has_canonical_query_identity(string caseName, string label)
    {
        var binding = ParameterBinding.create(ParamKeys.sorterSuccessFractionKey, SorterSuccessFraction.fromString(caseName));
        Assert.Equal(parameterValueType.SorterSuccessFraction, binding.ValueType);
        Assert.Equal(label, binding.FormattedValue);
        Assert.Equal(binding, ParameterBinding.fromText("SorterSuccessFraction", binding.ValueType, label));
        Assert.Equal(binding, ParameterBindingDto.toDomain(MessagePackSetup.deserialize<parameterBindingDto>(MessagePackSetup.serialize(ParameterBindingDto.fromDomain(binding)))));
        var projectId = Guid.NewGuid();
        Guid Key(string text) => DataKey.create(projectId, "Results", false, "models", "2", sortopiaDataType.NewSorterModelSet(""),
            ["SorterSuccessFraction"], parameterSet.fromBindings([ParameterBinding.fromText("SorterSuccessFraction", binding.ValueType, text)]), []).Id;
        Assert.Equal(Key(caseName), Key(label));
        Assert.NotEqual(Key(label), Key(label == "99%" ? "50%" : "99%"));
        Assert.Throws<ArgumentException>(() => ParameterBinding.fromText("SorterSuccessFraction", binding.ValueType, "75%"));
        Assert.Throws<ArgumentException>(() => ParameterBinding.create(ParamKey.integer("SorterSuccessFraction"), 99));
    }

    public static IEnumerable<object[]> GenerationChoices =>
        from phase in new[] { "setup", "results", "prefix" }
        from model in new[] { "msce", "mssi", "msrs", "msuf4", "msuf6" }
        from family in new[] { "Simple", "SimpleR", "SimpleS" }
        from fraction in new[] { "50%", "90%", "99%" }
        select new object[] { phase, model, family, fraction };

    [Theory]
    [MemberData(nameof(GenerationChoices))]
    public void Generated_capacity_and_live_suggestion_use_the_selected_target(string phase, string model, string family, string fraction)
    {
        bool setup = phase == "setup", prefix = phase == "prefix";
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: setup ? hierarchyKind.Branch : hierarchyKind.Root).Editor!;
        var name = $"sorter-model-set.{(prefix ? "prefix" : "uniform")}.{model}{ModelFamilyResultsTests.Suffix(family)}";
        var version = setup ? SetupFunctionCatalog.require(name).Version : ResultsFunctionCatalog.require(name).Version;
        Assert.Equal(model == "msuf4" ? "6" : "2", version);
        if (setup) editor.SetupFunctionName = name; else editor.ResultsFunctionName = name;
        (setup ? editor.AddRequiredSetupDefaultsCommand : editor.AddRequiredResultsDefaultsCommand).Execute(null);
        var defaults = setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
        Assert.DoesNotContain(defaults, row => row.Key == "StageLength");
        var choice = defaults.Single(row => row.Key == "SorterSuccessFraction").Value;
        Assert.Equal(new[] { "50%", "90%", "99%" }, choice.Choices);
        Assert.Equal("99%", choice.Text);
        choice.Text = fraction;
        defaults.Single(row => row.Key == "SorterCount").Value.Text = "1";
        int width = model == "msuf6" ? 12 : 8;
        var stages = SimpleSorterStageLengths.forSuccessFraction(SorterSuccessFraction.fromString(fraction), width, prefix ? 1 : 0, SimpleSorterModelType.fromString(model)).Stages;
        var guidance = setup ? editor.SetupInputGuidance : editor.ResultsInputGuidance;
        Assert.Contains($"Suggested random suffix: {stages:N0} stages", guidance);
        Assert.Contains($"{fraction} target", guidance);
        var settings = editor.ToProjectSettings();
        var parameters = parameterSet.fromBindings((setup ? settings.setupParameterDefaults : settings.resultsParameterDefaults).Select(ParameterBindingDto.toDomain));
        var generated = Assert.IsType<functionData.SorterModelSet>(Assert.Single(CatalogTestHost.Invoke(setup, name, parameters))).Item;
        var value = Assert.Single(generated.SorterModels);
        var simple = value switch { sorterModel.Simple s => s.Item, sorterModel.SimpleR r => r.Item.Model, sorterModel.SimpleS s => s.Item.Model, _ => throw new InvalidOperationException() };
        Assert.Equal(model == "msce" ? width * stages / 2 : stages, SimpleSorterModel.getLength(simple));
    }

    [Fact]
    public void Prefix_depth_changes_suggestion_and_success_spans_preserve_typed_values()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models");
        var editor = module.Editor!;
        editor.ResultsFunctionName = ResultsFunctionNames.prefixMssiSimpleSorterModels;
        editor.AddRequiredResultsDefaultsCommand.Execute(null);
        editor.ResultsParameterDefaults.Single(row => row.Key == "PrefixLibId").Value.Text = PrefixLibId.toString(prefixLibId.create(8, 3, prefixLibVariant.PrefixA));
        Assert.Contains("46 stages (8 channels, 3 prefix stages, 99% target", editor.ResultsInputGuidance);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project"); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run"); project.Children.Add(run);
        var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "SorterSuccessFraction", false, "50%", "90%", "99%");
        Assert.Empty(run.Editor!.ResultsParametersError);
        Assert.Contains("19–46 stages", run.Editor.ResultsInputGuidance);
        Assert.Equal(SorterSuccessFraction.all, ListModule.OfSeq(run.Editor.ResultsParameterSetsPreview.Rows.Select(row => row.ParameterSet.TryFind(ParamKeys.sorterSuccessFractionKey).Value)));
        span.Values.Last().Text = "75%";
        Assert.NotEmpty(run.Editor.ResultsParametersError);
        Assert.Empty(run.Editor.ResultsParameterSetsPreview.Rows);
    }
}
