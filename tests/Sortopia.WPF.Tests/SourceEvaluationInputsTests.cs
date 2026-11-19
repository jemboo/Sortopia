using Microsoft.FSharp.Collections;
using Sortopia.FileDb;
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
    private static readonly sortopiaDataType ModelsType = sortopiaDataType.NewSorterModelSet("");
    private static readonly sortopiaDataType TestsType = sortopiaDataType.NewSortableTests("");
    private static sortopiaStorageData Models(int channels = 4) => Assert.Single(CatalogTestHost.Invoke(false,
        ResultsFunctionNames.uniformMssiSimpleSorterModels,
        parameterSet.fromBindings(RunExecutionTests.UniformModelDefaults(channels).Select(ParameterBindingDto.toDomain))));
    private static sortopiaStorageData Tests(int channels = 4) => Assert.Single(CatalogTestHost.Invoke(false,
        ResultsFunctionNames.mergeSortableTests, parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.mergeLibIdKey, MergeLibId.availableForChannelCount(channels).First(value => value.MergeDimension == 2)),
            ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")])));
    internal static WorkspaceSourceProject Source(string kind, params string[] keys)
    {
        var type = new sortopiaDataTypeDto { kind = kind, value = "" };
        return new(Guid.NewGuid(), kind + " source", true, new projectSettingsDto {
            resultsDataTypes = [new() { dataType = type, queryParameterKeys = keys }]
        }, new WorkspaceTreeNode(WorkspaceEntityKind.Project, kind + " source").Editor!);
    }

    [Fact]
    public void One_generic_copy_entry_preserves_both_payloads_and_independent_dependencies()
    {
        Assert.Equal(new[] { SetupFunctionNames.evaluationSource }, SetupFunctionCatalog.functions.Keys.Where(name => name.StartsWith("sorter-eval.")));
        var definition = SetupFunctionCatalog.require(SetupFunctionNames.evaluationSource);
        Assert.True(definition.ParametersFromSources); Assert.Empty(definition.RequiredParameters);
        Assert.Equal("2", definition.Version);
        foreach (var type in new[] { ModelsType, TestsType })
        {
            Assert.Contains(type, definition.SourceDataTypes);
            Assert.Empty(definition.DataTypes[type]);
            Assert.Equal(new[] { type }, definition.OutputDependencies[type]);
        }
        var models = Models(); var tests = Tests();
        var copies = CatalogTestHost.Invoke(true, SetupFunctionNames.evaluationSource, parameterSet.fromBindings([]), models, tests);
        Assert.Same(models, copies.Single(copy => copy.IsSorterModelSet));
        Assert.Same(tests, copies.Single(copy => copy.IsSortableTests));
        Assert.True(Assert.Single(CatalogTestHost.Invoke(false, ResultsFunctionNames.sorterEval, parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.sorterEvalTypeKey, "V1"),
            ParameterBinding.create(ParamKeys.collectSortableTestsKey, false)]), copies.ToArray())).IsSorterSetEval);
    }

    [Fact]
    public void Copy_validates_the_actual_widths_without_requiring_a_channel_count_lookup_key()
    {
        Assert.Contains("same channel count", Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true,
            SetupFunctionNames.evaluationSource, parameterSet.fromBindings([]), Models(8), Tests(4))).Message);
        Assert.Contains("queried ChannelCount", Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true,
            SetupFunctionNames.evaluationSource, parameterSet.fromBindings([ParameterBinding.create(ParamKeys.channelCountKey, 8)]), Models(), Tests())).Message);
    }

    [Fact]
    public void Source_selection_drives_required_parameters_instead_of_model_generation_settings()
    {
        var models = Source("SorterModelSet", "ChannelCount", "Repl");
        var tests = Source("SortableTests", "PrefixLibId", "SortableDataFormat");
        var editor = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Evaluation", () => [models, tests]);
        editor.SetupFunctionName = SetupFunctionNames.evaluationSource; editor.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        editor.SetupDataTypesOverride.IsOverridden = true;
        Assert.All(editor.SetupDataTypes, row => Assert.True(row.RequiresSource));
        Assert.Empty(editor.SetupParameterDefaults);
        editor.SetupDataTypes.Single(row => row.DataType.Kind == "SorterModelSet").SourceProjectId = models.Id;
        editor.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests").SourceProjectId = tests.Id;
        editor.AddRequiredSetupDefaultsCommand.Execute(null);
        Assert.Equal(new[] { "ChannelCount", "PrefixLibId", "Repl", "SortableDataFormat" }, editor.LocalSetupParameterDefaults.Select(row => row.Key).Order());
        Assert.DoesNotContain(editor.LocalSetupParameterDefaults, row => row.Key is "SorterSuccessFraction" or "SorterCount" or "ActivationProbability");
        Assert.Empty(editor.SetupDataTypesError); Assert.Empty(editor.SetupParameterDefaultsError);
        Assert.Contains("Copy both", editor.SetupInputGuidance);
    }

    [Fact]
    public void Module_defaults_survive_until_a_child_project_selects_its_copy_sources()
    {
        var models = Source("SorterModelSet", "ChannelCount", "Repl");
        var tests = Source("SortableTests", "ChannelCount");
        var module = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Module", () => [models, tests]);
        module.SetupFunctionName = SetupFunctionNames.evaluationSource;
        WorkspaceParameterInheritanceTests.AddDefault(module, "ChannelCount", "8", true);
        WorkspaceParameterInheritanceTests.AddDefault(module, "Repl", "3", true);
        Assert.Equal(2, module.ToProjectSettings().setupParameterDefaults.Length);
        Assert.Empty(module.SetupParameterDefaultsError);
        var project = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Project, true, () => module, () => "Evaluation", () => [models, tests]);
        project.SetupDataTypesOverride.IsOverridden = true;
        project.SetupDataTypes.Single(row => row.DataType.Kind == "SorterModelSet").SourceProjectId = models.Id;
        project.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests").SourceProjectId = tests.Id;
        Assert.Equal(new[] { "ChannelCount", "Repl" }, project.InheritedSetupDefaults.Select(value => value.Key).Order());
        Assert.Equal("3", project.InheritedSetupDefaults.Single(value => value.Key == "Repl").Value);
        Assert.Empty(project.SetupParameterDefaultsError);
    }

    [Fact]
    public void Source_query_keys_can_exceed_local_keys_but_must_contain_them_for_the_same_type()
    {
        var matching = Source("SortableTests", "PrefixLibId", "SortableDataFormat");
        var extra = Source("SortableTests", "PrefixLibId", "SortableDataFormat", "Repl");
        var missing = Source("SortableTests", "ChannelCount");
        var wrong = Source("SorterModelSet", "PrefixLibId");
        var row = new WorkspaceSetupDataTypeEntry(new() { dataType = new() { kind = "SortableTests", value = "" }, queryParameterKeys = ["PrefixLibId"] }, () => [matching, extra, missing, wrong]);
        row.SetContract(true, false, ["PrefixLibId"], [], true);
        Assert.Equal(new[] { extra.Id, matching.Id }.Order(), row.SourceProjects.Where(choice => choice.IsCompatible).Select(choice => choice.Id).Order());
        row.SourceProjectId = missing.Id; Assert.Contains("subset", row.Error); Assert.Contains("PrefixLibId", row.Error);
        row.SourceProjectId = wrong.Id; Assert.Contains("exact data type", row.Error);
        row.SourceProjectId = extra.Id; Assert.Empty(row.Error);
    }

    [Fact]
    public void Changing_one_source_changes_only_its_own_local_output_identity()
    {
        var definition = SetupFunctionCatalog.require(SetupFunctionNames.evaluationSource);
        var models = new storedDataReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), runExecutionPhase.Results, ModelsType);
        var tests = new storedDataReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), runExecutionPhase.Results, TestsType);
        var changed = new storedDataReference(models.ProjectId, Guid.NewGuid(), models.QueryId, runExecutionPhase.Results, ModelsType);
        var project = Guid.NewGuid();
        var parameters = parameterSet.fromBindings([]);
        var first = OutputPlan.create(project, "Setup", true, SetupFunctionNames.evaluationSource, definition.Version, definition.DataTypes, definition.OutputDependencies, parameters, [models, tests]);
        var second = OutputPlan.create(project, "Setup", true, SetupFunctionNames.evaluationSource, definition.Version, definition.DataTypes, definition.OutputDependencies, parameters, [changed, tests]);
        Assert.NotEqual(first.Single(pair => Equals(pair.Key.DataType, ModelsType)).Value.Key.Id, second.Single(pair => Equals(pair.Key.DataType, ModelsType)).Value.Key.Id);
        Assert.Equal(first.Single(pair => Equals(pair.Key.DataType, TestsType)).Value.Key.Id, second.Single(pair => Equals(pair.Key.DataType, TestsType)).Value.Key.Id);
    }
}
