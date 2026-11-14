using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class SourceEvaluationInputsTests
{
    private static parameterSet Parameters(int channels) => parameterSet.fromBindings([ParameterBinding.create(ParamKeys.channelCountKey, channels)]);
    private static functionData Models(int channels = 4)
    {
        var parameters = parameterSet.fromBindings(RunExecutionTests.UniformModelDefaults(channels).Select(ParameterBindingDto.toDomain));
        return Assert.Single(CatalogTestHost.Invoke(false, ResultsFunctionNames.uniformMssiSorterModels, parameters));
    }

    private static functionData Tests(int channels = 4)
    {
        var library = MergeLibId.availableForChannelCount(channels).First(value => value.MergeDimension == 2);
        return Assert.Single(CatalogTestHost.Invoke(false, ResultsFunctionNames.mergeSortableTests,
            parameterSet.fromBindings([ParameterBinding.create(ParamKeys.mergeLibIdKey, library),
                ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")])));
    }

    [Fact]
    public void Catalog_function_copies_models_and_tests_unchanged_with_independent_dependencies()
    {
        var definition = SetupFunctionCatalog.functions[SetupFunctionNames.evaluationSource];
        var types = new[] { sortopiaDataType.NewSorterModelSet(""), sortopiaDataType.NewSortableTests("") };
        Assert.Equal(new[] { "ChannelCount" }, definition.RequiredParameters.Select(key => key.Name));
        Assert.Equal(types.Order(), definition.SourceDataTypes.Order());
        Assert.Equal(types.Order(), definition.SortopiaDataTypes.Order());
        foreach (var type in types)
        {
            Assert.Equal(new[] { type }, definition.OutputDependencies[type]);
            Assert.Equal(new[] { "ChannelCount" }, definition.DataTypes[type]);
        }
        var models = Models(); var tests = Tests();
        Assert.NotEmpty(Assert.IsType<functionData.SortableTests>(tests).Item2); // Preserve the generating prefix too.
        var copies = CatalogTestHost.Invoke(true, SetupFunctionNames.evaluationSource, Parameters(4), models, tests);
        Assert.Equal(2, copies.Length);
        Assert.Same(models, copies.Single(copy => copy.IsSorterModelSet));
        Assert.Same(tests, copies.Single(copy => copy.IsSortableTests));
        var evaluation = CatalogTestHost.Invoke(false, ResultsFunctionNames.sorterEval,
            parameterSet.fromBindings([ParameterBinding.create(ParamKeys.channelCountKey, 4),
                ParameterBinding.create(ParamKeys.sorterEvalTypeKey, "V1"), ParameterBinding.create(ParamKeys.collectSortableTestsKey, false)]),
            copies.ToArray());
        Assert.True(Assert.Single(evaluation).IsSorterSetEval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(8)]
    public void Copy_requires_positive_matching_channel_count(int channels)
    {
        var error = Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true,
            SetupFunctionNames.evaluationSource, Parameters(channels), Models(), Tests()));
        Assert.Contains("ChannelCount", error.Message);
    }

    [Fact]
    public void Copy_rejects_mixed_channel_counts()
    {
        var four = Assert.IsType<functionData.SorterModelSet>(Models(4)).Item;
        var eight = Assert.IsType<functionData.SorterModelSet>(Models(8)).Item;
        var mixed = functionData.NewSorterModelSet(sorterModelSet.create(Guid.NewGuid(), [.. four.SorterModels, .. eight.SorterModels], four.RawCeLength));
        Assert.Contains("Every model", Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true,
            SetupFunctionNames.evaluationSource, Parameters(4), mixed, Tests())).Message);
    }

    [Fact]
    public void Copy_rejects_tests_with_a_different_channel_count()
    {
        var error = Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true,
            SetupFunctionNames.evaluationSource, Parameters(8), Models(8), Tests(4)));
        Assert.Contains("SortableTests must have ChannelCount 8", error.Message);
    }

    [Fact]
    public void Module_editor_offers_channel_count_two_sources_and_compatible_evaluation()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: hierarchyKind.Branch);
        var editor = module.Editor!;
        editor.SetupFunctionName = SetupFunctionNames.evaluationSource;
        editor.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        editor.AddRequiredSetupDefaultsCommand.Execute(null);
        Assert.Equal("ChannelCount", Assert.Single(editor.SetupParameterDefaults).Key);
        Assert.Equal(new[] { "SortableTests", "SorterModelSet" }, editor.SetupDataTypes.Select(output => output.DataType.Kind).Order());
        Assert.All(editor.SetupDataTypes, output => Assert.True(output.RequiresSource));
        Assert.Contains(ResultsFunctionNames.sorterEval, editor.CompatibleResultsFunctions);

        foreach (var kind in new[] { "SorterModelSet", "SortableTests" })
        {
            var matching = RunExecutionTests.Settings(false);
            matching.resultsSortopiaDataTypes = [new() { kind = kind, value = "" }];
            matching.resultsDataTypes = [new() { dataType = matching.resultsSortopiaDataTypes[0], queryParameterKeys = ["ChannelCount"] }];
            var extraKeys = RunExecutionTests.Settings(false);
            extraKeys.resultsSortopiaDataTypes = matching.resultsSortopiaDataTypes;
            extraKeys.resultsDataTypes = [new() { dataType = matching.resultsSortopiaDataTypes[0], queryParameterKeys = ["ChannelCount", "Repl"] }];
            var source = new WorkspaceSourceProject(Guid.NewGuid(), "Matching source", true, matching, editor);
            var other = new WorkspaceSourceProject(Guid.NewGuid(), "Extra query keys", true, extraKeys, editor);
            var wrongType = RunExecutionTests.Settings(false);
            wrongType.resultsSortopiaDataTypes = [new() { kind = kind == "SorterModelSet" ? "SortableTests" : "SorterModelSet", value = "" }];
            wrongType.resultsDataTypes = [new() { dataType = wrongType.resultsSortopiaDataTypes[0], queryParameterKeys = ["ChannelCount"] }];
            var wrongSource = new WorkspaceSourceProject(Guid.NewGuid(), "Other data type", true, wrongType, editor);
            var row = new WorkspaceSetupDataTypeEntry(new() { dataType = matching.resultsSortopiaDataTypes[0], queryParameterKeys = [] }, () => [source, other, wrongSource]);
            row.SetContract(true, true, ["ChannelCount"], ["ChannelCount"]);
            Assert.Equal(3, row.SourceProjects.Count());
            Assert.Equal(source.Id, Assert.Single(row.SourceProjects, choice => choice.IsCompatible).Id);
            Assert.Contains("Repl", row.SourceProjects.Single(choice => choice.Id == other.Id).IncompatibilityReason);
            Assert.Contains("exact data type", row.SourceProjects.Single(choice => choice.Id == wrongSource.Id).IncompatibilityReason);
            row.SourceProjectId = source.Id; Assert.Empty(row.Error);
            row.SourceProjectId = other.Id; Assert.Contains("query parameters", row.Error);
        }
    }

    [Fact]
    public void Prefix_copy_declares_separate_projections_and_preserves_source_payloads()
    {
        var definition = SetupFunctionCatalog.require(SetupFunctionNames.evaluationSourcePrefix);
        var modelsType = sortopiaDataType.NewSorterModelSet(""); var testsType = sortopiaDataType.NewSortableTests("");
        Assert.Equal(new[] { "ChannelCount", "PrefixLibId" }, definition.RequiredParameters.Select(key => key.Name));
        Assert.Equal(new[] { "ChannelCount" }, definition.DataTypes[modelsType]);
        Assert.Equal(new[] { "PrefixLibId" }, definition.DataTypes[testsType]);
        Assert.Equal(new[] { modelsType }, definition.OutputDependencies[modelsType]);
        Assert.Equal(new[] { testsType }, definition.OutputDependencies[testsType]);
        var prefix = PrefixLib.ids().First(id => id.ChannelCount == 8 && id.StageLength == 1);
        var models = Models(8);
        var tests = Assert.Single(CatalogTestHost.Invoke(false, ResultsFunctionNames.prefixSortableTests,
            parameterSet.fromBindings([ParameterBinding.create(ParamKeys.prefixLibIdKey, prefix), ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")])));
        var parameters = Parameters(8).WithBinding(ParameterBinding.create(ParamKeys.prefixLibIdKey, prefix));
        var copies = CatalogTestHost.Invoke(true, SetupFunctionNames.evaluationSourcePrefix, parameters, models, tests);
        Assert.Same(models, copies.Single(copy => copy.IsSorterModelSet)); Assert.Same(tests, copies.Single(copy => copy.IsSortableTests));
        Assert.NotEmpty(Assert.IsType<functionData.SortableTests>(tests).Item2);
        Assert.Contains("must match PrefixLibId", Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true,
            SetupFunctionNames.evaluationSourcePrefix, parameters.WithBinding(ParameterBinding.create(ParamKeys.channelCountKey, 4)), models, tests)).Message);
        Assert.Contains("Every model", Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true,
            SetupFunctionNames.evaluationSourcePrefix, parameters, Models(4), tests)).Message);
        Assert.Contains("SortableTests must have ChannelCount 8", Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true,
            SetupFunctionNames.evaluationSourcePrefix, parameters, models, Tests(4))).Message);
        var defaults = ListModule.Singleton(ParameterBinding.create(ParamKeys.prefixLibIdKey, prefix));
        var span = ParameterSpan.create(ParamKeys.channelCountKey, ListModule.OfSeq(Enumerable.Repeat(8, 60).Append(16)));
        Assert.Empty(definition.InputError.Invoke(defaults).Invoke([]));
        Assert.Contains("must match PrefixLibId", definition.InputError.Invoke(defaults).Invoke([span]));
    }

    [Fact]
    public void Prefix_copy_editor_accepts_the_prefix_tests_project_and_reports_missing_keys_for_the_original_function()
    {
        var sourceSettings = WorkspaceHierarchyTests.ProjectSettings(false);
        sourceSettings.resultsDataTypes[0].queryParameterKeys = ["PrefixLibId"];
        var source = new WorkspaceSourceProject(Guid.NewGuid(), "smallCC", true, sourceSettings,
            new WorkspaceTreeNode(WorkspaceEntityKind.Project, "smallCC").Editor!, Location: "SortableTests / Prefix");
        var editor = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Module", () => [source]);
        editor.SetupFunctionName = SetupFunctionNames.evaluationSource;
        var choice = Assert.Single(editor.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests").SourceProjects);
        Assert.False(choice.IsCompatible); Assert.Contains("PrefixLibId", choice.IncompatibilityReason); Assert.Contains("ChannelCount", choice.IncompatibilityReason);
        editor.SetupFunctionName = SetupFunctionNames.evaluationSourcePrefix;
        editor.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        editor.AddRequiredSetupDefaultsCommand.Execute(null);
        Assert.Equal(new[] { "ChannelCount", "PrefixLibId" }, editor.SetupParameterDefaults.Select(row => row.Key).Order());
        Assert.Empty(editor.SetupParameterDefaultsError);
        choice = Assert.Single(editor.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests").SourceProjects);
        Assert.True(choice.IsCompatible); Assert.Contains("PrefixLibId", choice.QuerySummary);
    }
}
