using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionParameterContractTests
{
    [Fact]
    public void Every_catalog_definition_declares_unique_typed_keys_and_rejects_extras()
    {
        foreach (var keys in FunctionCatalogs.resultsFunctionCatalog.Values.Select(value => value.RequiredParameters)
            .Concat(FunctionCatalogs.setupFunctionCatalog.Values.Select(value => value.RequiredParameters)))
        {
            Assert.NotEmpty(keys);
            Assert.Equal(keys.Count(), keys.Select(key => key.Name).Distinct().Count());
            Assert.All(keys, key => Assert.Equal(ParamKeys.tryFind(key.Name).Value.ValueType, key.ValueType));
        }
        var definition = SetupFunctions.standardTests;
        var valid = parameterSet.fromBindings([ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")]);
        Assert.Empty(FunctionParameters.validationError(definition.RequiredParameters, valid));
        var extra = valid.WithBinding(ParameterBinding.create(ParamKeys.replKey, 0));
        Assert.Contains("Repl", Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(true, SetupFunctionNames.standardTests, extra)).Message);
        Assert.Contains("Integer", Assert.Throws<ArgumentException>(() => ParameterBinding.fromValue("ChannelCount", parameterValue.NewTextValue("4"))).Message);
        Assert.Equal(new[] { "SortableDataFormat", "MergeLibId" }, ResultsFunctions.mergeSortableTests.RequiredParameters.Select(key => key.Name));
        Assert.Equal(new[] { "SortableDataFormat", "PrefixLibId" }, ResultsFunctions.prefixSortableTests.RequiredParameters.Select(key => key.Name));
    }

    [Fact]
    public void Module_functions_constrain_each_phase_and_missing_keys_include_non_query_inputs()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.LoadProject(RunExecutionTests.Settings(true));
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch);
        module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch);
        project.Children.Add(run);
        Assert.False(project.Editor!.ResultsFunctionOverride.CanOverride);
        Assert.False(project.Editor.SetupFunctionOverride.CanOverride);
        Assert.False(run.Editor!.SetupFunctionOverride.CanOverride);
        project.Editor.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        run.Editor.SetupFunctionName = SetupFunctionNames.uniformSorterModels;
        Assert.Equal(ResultsFunctionNames.mergeSortableTests, run.Editor.ResultsFunctionName);
        Assert.Equal(SetupFunctionNames.standardTests, run.Editor.SetupFunctionName);

        var results = WorkspaceParameterSetsPreviewTests.AddSpan(run, "SortableDataFormat", false, "Bools");
        var setup = WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", true, "8");
        Assert.Equal(new[] { "SortableDataFormat" }, results.KeyChoices);
        Assert.Equal(new[] { "ChannelCount" }, setup.KeyChoices);
        Assert.True(results.HasRestrictedKeys);
        results.Key = "ChannelCount";
        Assert.Contains("not required", results.Error);
        Assert.Empty(run.Editor.ResultsParameterSetsPreview.Rows);
        results.Key = "SortableDataFormat";
        WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false);
        Assert.Single(run.Editor.ResultsParameterSetsPreview.Rows);

        var extra = WorkspaceParameterInheritanceTests.AddDefault(project.Editor, "Repl", "1");
        Assert.Contains("not required", extra.Error);
        Assert.Empty(project.Editor.ResultsParameterDefaultsError);
        Assert.DoesNotContain(project.Editor.LocalResultsParameterDefaults, row => row.Key == "Repl");
        extra.RemoveCommand.Execute(null);
        module.Editor.ResultsParameterDefaults.Single(row => row.Key == "MergeLibId").RemoveCommand.Execute(null);
        run.Editor.ResultsParameters.Single(row => row.Key == "MergeLibId").RemoveCommand.Execute(null);
        Assert.Contains("MergeLibId", run.Editor.ResultsParametersError);
        run.Editor.AddMissingResultsParametersCommand.Execute(null);
        Assert.True(run.Editor.ResultsParameters.Single(row => row.Key == "MergeLibId").Values.Count == 0);
        Assert.Null(typeof(projectOverridesDto).GetProperty("resultsFunctionName"));
    }

    [Fact]
    public void Switching_module_function_filters_inherited_defaults_and_retains_other_local_defaults_for_switching_back()
    {
        var parent = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Parent");
        parent.Editor!.LoadProject(RunExecutionTests.Settings(false));
        var child = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Child"); parent.Children.Add(child);
        child.Editor!.ResultsFunctionName = ResultsFunctionNames.prefixSortableTests;
        Assert.Equal(new[] { "SortableDataFormat" }, child.Editor.InheritedOutputDefaults.Select(value => value.Key));
        var local = WorkspaceParameterInheritanceTests.AddDefault(child.Editor, "MergeLibId", parent.Editor.ResultsParameterDefaults.Single(value => value.Key == "MergeLibId").Value.Text);
        Assert.Empty(child.Editor.ResultsParameterDefaultsError);
        Assert.Empty(child.Editor.LocalResultsParameterDefaults);
        Assert.Empty(child.Editor.ToLocalOverrides().resultsParameterDefaults);
        Assert.Contains("switching back", child.Editor.RetainedResultsDefaultsHint);
        child.Editor.ResultsFunctionName = ResultsFunctionNames.mergeSortableTests;
        Assert.Empty(child.Editor.ResultsParameterDefaultsError);
        Assert.Equal("MergeLibId", local.Key);

        child.Editor.ResultsFunctionName = ResultsFunctionNames.prefixSortableTests;
        local.RemoveCommand.Execute(null);
        var rootConfig = ModuleSettingsDto.toDomain(hierarchyKind.Root, ModuleSettingsDto.fromProjectSettings(parent.Editor.ToProjectSettings()));
        var overrides = ModuleOverridesDto.toDomain(hierarchyKind.Root, child.Editor.ToModuleOverrides());
        var resolved = ModuleDefaults.resolve(rootConfig, overrides);
        Assert.Equal(child.Editor.ToProjectSettings().resultsParameterDefaults.Select(value => value.keyName),
            ModuleSettingsDto.fromDomain(resolved).resultsParameterDefaults.Select(value => value.keyName));
    }

    [Fact]
    public void Invalid_results_inputs_are_rejected_before_a_run_can_be_saved()
    {
        Assert.Contains("Repl", Assert.Throws<ArgumentException>(() =>
            RunExecutionTests.MakeRun(true, results: [new() { keyName = "Repl", values = ["1"] }])).Message);
    }

    [Fact]
    public void Project_local_overrides_cannot_define_functions()
    {
        Assert.Null(typeof(projectOverridesDto).GetProperty("resultsFunctionName"));
        Assert.Null(typeof(rootDefaultOverrides).GetProperty("ResultsFunctionName"));
        Assert.Null(typeof(branchDefaultOverrides).GetProperty("SetupFunctionName"));
        Assert.Null(typeof(branchRunConfig).GetProperty("SetupFunctionNameOverride"));
    }
}
