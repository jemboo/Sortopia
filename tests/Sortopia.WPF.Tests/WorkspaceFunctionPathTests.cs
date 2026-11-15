using System.IO;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceFunctionPathTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-function-path-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private WorkspacePageViewModel Open()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(root);
        return new(() => { }, _ => null, settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Containers_with_legacy_empty_function_values_load_without_configuration_issues(bool branch)
    {
        var page = Open();
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        page.Selected!.Name = "SorterModelSets";
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null); page.Selected!.Name = "Prefix";
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null); page.Selected!.Name = "msuf4";
        page.Selected.Editor!.ResultsFunctionName = ResultsFunctionNames.prefixMsuf4SorterModels;
        if (branch) page.Selected.Editor.SetupFunctionName = SetupFunctionNames.evaluationStandardMssi;
        Assert.Empty(page.SaveIssues);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDocument(page.WorkspaceFolder);
        var legacy = WorkspaceFileStore.LoadDocument(page.WorkspaceFolder);
        legacy.Modules[0].Modules[0].Definition.localOverrides.resultsFunctionName = "";
        if (branch) legacy.Modules[0].Modules[0].Definition.localOverrides.setupFunctionName = " ";
        WorkspaceFileStore.SaveDocument(page.WorkspaceFolder, legacy, saved, Token);

        var reopened = Open();
        Assert.Empty(reopened.Error); Assert.Empty(reopened.SaveIssues);
        var prefix = reopened.Roots[0].Children[0].Children[0].Editor!;
        Assert.True(prefix.ResultsFunctionOverride.IsInherited);
        Assert.Null(prefix.ToModuleOverrides().resultsFunctionName);
        Assert.Contains("may leave", prefix.FunctionsHint);
        // Runtime readers also accept old empty overrides without modifying the files.
        WorkspaceFileStore.LoadDefinition(page.WorkspaceFolder);
        Assert.Equal("", WorkspaceFileStore.LoadDocument(page.WorkspaceFolder).Modules[0].Modules[0].Definition.localOverrides.resultsFunctionName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Setup_and_results_can_be_defined_on_different_modules_in_either_order(bool setupFirst)
    {
        var page = Open(); page.AddBranchModuleCommand.Execute(null);
        var parent = page.Selected!; parent.Name = "Parent";
        void Define(WorkspaceTreeNode node, bool setup)
        {
            if (setup)
            {
                node.Editor!.SetupFunctionName = SetupFunctionNames.evaluationStandardMssi;
                node.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
                node.Editor.SetupParameterDefaults.Single(value => value.Key == "ChannelCount").Value.Text = "4";
                node.Editor.SetupParameterDefaults.Single(value => value.Key == "SorterCount").Value.Text = "2";
                node.Editor.SetupParameterDefaults.Single(value => value.Key == "StageLength").Value.Text = "1";
            }
            else
            {
                node.Editor!.ResultsFunctionName = ResultsFunctionNames.sorterEval;
                node.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
                node.Editor.ResultsParameterDefaults.Single(value => value.Key == "ChannelCount").Value.Text = "4";
            }
        }
        Define(parent, setupFirst);
        Assert.Empty(page.SaveIssues);
        Assert.Contains(page.InspectorItems, item => item.Key == (setupFirst ? "setupDefaults" : "resultsDefaults"));
        page.AddBranchModuleCommand.Execute(null); var child = page.Selected!; child.Name = "Child";
        Define(child, !setupFirst);
        page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null);
        var runId = page.Selected!.Id;
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        Assert.Empty(page.SaveIssues);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        page.Selected = page.Roots[0].DescendantsAndSelf().Single(node => node.Id == runId);
        var scope = WorkspaceRunDocument.resolveByRunId(WorkspaceFileStore.LoadDocument(page.WorkspaceFolder), runId);
        Assert.Equal(ResultsFunctionNames.sorterEval, scope.Run.ResultsFunctionName);
        Assert.Equal(SetupFunctionNames.evaluationStandardMssi, scope.Run.SetupFunctionName.Value);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
        await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
        Assert.True(page.Selected.Parent!.IsSealed);
        // Later module choices cannot change the functions in this generated project.
        page.Selected.Parent.Parent!.Editor!.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
        Assert.Empty(page.Selected.Editor!.ResultsFunctionError);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_run_rejects_missing_and_duplicate_definitions_even_when_duplicate_names_match(bool duplicateSetup)
    {
        var page = WorkspaceRunExecutionTests.Create(temporary.FullName, duplicateSetup);
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        await page.SaveWorkspaceAsync(); WorkspaceRunExecutionTests.SelectRun(page);
        if (duplicateSetup) await WorkspaceRunExecutionTests.ExecuteSourceAsync(page);
        var run = page.Selected!; var project = run.Parent!; var module = project.Parent!;
        var child = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Duplicate", family: module.Family);
        module.Children.Add(child); module.Children.Remove(project); child.Children.Add(project);
        var field = duplicateSetup ? child.Editor!.SetupFunctionOverride : child.Editor!.ResultsFunctionOverride;
        field.IsOverridden = true;
        Assert.False(field.IsOverridden); Assert.False(field.CanOverride);
        // Older saved definitions can still contain duplicates; keep them visible and repairable.
        var duplicate = child.Editor.ToModuleOverrides();
        if (duplicateSetup) duplicate.setupFunctionName = module.Editor!.SetupFunctionName;
        else duplicate.resultsFunctionName = module.Editor!.ResultsFunctionName;
        child.Editor.LoadModuleOverrides(duplicate);
        Assert.True(field.IsOverridden); Assert.False(field.CanEditValue);
        Assert.Same(module.Editor, field.InheritedSource);
        Assert.Contains("multiple modules", duplicateSetup ? run.Editor!.SetupFunctionError : run.Editor!.ResultsFunctionError);
        Assert.Contains("Duplicate", duplicateSetup ? run.Editor!.SetupFunctionError : run.Editor!.ResultsFunctionError);
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        var document = page.Roots[0].ToDocument();
        var error = Assert.Throws<InvalidOperationException>(() => WorkspaceRunDocument.resolveByRunId(document, run.Id));
        Assert.Contains("exactly one", error.Message); Assert.Contains("Duplicate", error.Message);
        Assert.True(field.UseInheritedFunctionCommand.CanExecute(null));
        field.UseInheritedFunctionCommand.Execute(null);
        Assert.False(field.IsOverridden); Assert.False(field.CanOverride);
        Assert.Empty(duplicateSetup ? run.Editor!.SetupFunctionError : run.Editor!.ResultsFunctionError);
        WorkspaceRunDocument.resolveByRunId(page.Roots[0].ToDocument(), run.Id);
        (duplicateSetup ? module.Editor!.SetupFunctionOverride : module.Editor!.ResultsFunctionOverride).IsOverridden = false;
        error = Assert.Throws<InvalidOperationException>(() => WorkspaceRunDocument.resolveByRunId(page.Roots[0].ToDocument(), run.Id));
        Assert.Contains("No module defines it", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Descendant_definition_checks_follow_moves_ignore_empty_or_inherited_values_and_survive_reopening(bool setup)
    {
        var page = Open(); page.AddBranchModuleCommand.Execute(null); var first = page.Selected!; first.Name = "First";
        page.Selected = page.Roots[0]; page.AddBranchModuleCommand.Execute(null); var second = page.Selected!; second.Name = "Second";
        var leaf = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Defines function", family: hierarchyKind.Branch);
        first.Children.Add(leaf);
        leaf.Children.Add(new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Inherits function", family: hierarchyKind.Branch));
        WorkspaceOverrideField Field(WorkspaceTreeNode node) => setup ? node.Editor!.SetupFunctionOverride : node.Editor!.ResultsFunctionOverride;
        var function = setup ? SetupFunctionNames.uniformMssiSorterModels : ResultsFunctionNames.uniformMssiSorterModels;
        Field(leaf).IsOverridden = true;
        Assert.True(Field(first).CanOverride); Assert.Empty(Field(first).DescendantFunctionDefinitions);
        Field(leaf).EffectiveValue = function;
        Assert.False(Field(first).CanOverride);
        Assert.Same(leaf.Editor, Assert.Single(Field(first).DescendantFunctionDefinitions).Module);
        Assert.True(Field(second).CanOverride); // A separate branch can define its own function.
        if (setup) first.Editor!.SetupFunctionName = function;
        else first.Editor!.ResultsFunctionName = function;
        Assert.Null(Field(first).OverrideValue);

        first.Children.Remove(leaf); second.Children.Add(leaf);
        Assert.True(Field(first).CanOverride); Assert.Empty(Field(first).DescendantFunctionDefinitions);
        Assert.False(Field(second).CanOverride);
        Assert.Same(leaf.Editor, Assert.Single(Field(second).DescendantFunctionDefinitions).Module);
        var duplicate = second.Editor!.ToModuleOverrides();
        if (setup) duplicate.setupFunctionName = function; else duplicate.resultsFunctionName = function;
        second.Editor.LoadModuleOverrides(duplicate);
        Assert.True(Field(second).IsOverridden); Assert.False(Field(second).CanEditValue);
        Assert.Equal("Remove local definition", Field(second).ClearFunctionDefinitionCaption);
        Assert.True(Field(second).UseInheritedFunctionCommand.CanExecute(null));
        Field(second).UseInheritedFunctionCommand.Execute(null);
        Assert.Null(Field(second).OverrideValue); Assert.Equal(function, Field(leaf).OverrideValue);
        Assert.True(Field(leaf).CanEditValue);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var reopened = Open();
        var restored = reopened.Roots[0].Children.Single(node => node.Id == second.Id);
        Assert.False(Field(restored).CanOverride);
        Assert.Equal("Defines function", Assert.Single(Field(restored).DescendantFunctionDefinitions).Path);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
