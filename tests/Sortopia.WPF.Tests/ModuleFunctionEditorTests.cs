using System.IO;
using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ModuleFunctionEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Modules_start_with_functions_and_reveal_settings_only_for_a_compatible_selection(bool branch)
    {
        var folder = Directory.CreateTempSubdirectory("Sortopia-module-functions-");
        try
        {
            var settings = new WorkspaceFolderSettings(Path.Combine(folder.FullName, "settings.txt")); settings.Save(folder.FullName);
            var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
            (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
            var editor = page.Selected!.Editor!;
            Assert.Equal(branch ? "setupFunction" : "resultsFunction", page.ActiveItemKey);
            Assert.Equal("Functions", page.InspectorItems[0].Group);
            Assert.Equal("Choose functions", page.ActiveItemTitle);
            Assert.DoesNotContain(page.InspectorItems, item => item.Key is "resultsDefaults" or "setupDefaults" or "resultsTypes" or "setupOutputs");
            if (branch)
            {
                editor.ResultsFunctionOverride.IsOverridden = true;
                Assert.False(editor.ResultsFunctionOverride.CanSelectValue);
                Assert.Empty(editor.ResultsFunctionOverride.Choices);
                editor.SetupFunctionName = SetupFunctionNames.standardTests;
                Assert.True(editor.ResultsFunctionOverride.CanSelectValue);
            }
            Assert.DoesNotContain(ResultsFunctionNames.sorterEval, editor.ResultsFunctionOverride.Choices);
            editor.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
            Assert.True(editor.FunctionsReady);
            Assert.Contains(page.InspectorItems, item => item.Key == "resultsDefaults");
            Assert.Contains(page.InspectorItems, item => item.Key == "resultsTypes");
            Assert.DoesNotContain(page.InspectorItems, item => item.Key == "resultsQueryKeys");
            page.SelectConfigurationItem("resultsDefaults");
            editor.ResultsFunctionName = "";
            Assert.Equal(branch ? "setupFunction" : "resultsFunction", page.ActiveItemKey);
            Assert.DoesNotContain(page.InspectorItems, item => item.Key == "resultsDefaults");
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void Inherited_function_pairs_constrain_defaults_and_switching_setup_exposes_incompatibility()
    {
        var parent = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Parent", family: hierarchyKind.Branch);
        parent.Editor!.SetupFunctionName = SetupFunctionNames.evaluationStandardMssi;
        parent.Editor.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        parent.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
        parent.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        var child = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Child", family: hierarchyKind.Branch); parent.Children.Add(child);
        var editor = child.Editor!;
        Assert.True(editor.FunctionsReady);
        Assert.Same(parent.Editor, editor.ResultsFunctionOverride.InheritedSource);
        Assert.Same(parent.Editor, editor.SetupFunctionOverride.InheritedSource);
        editor.AddSetupParameterDefaultCommand.Execute(null);
        Assert.Equal(SetupFunctionCatalog.functions[SetupFunctionNames.evaluationStandardMssi].RequiredParameters.Select(key => key.Name)
                .Where(key => key is not ("AMax" or "ActivationProbability")).Order(StringComparer.Ordinal),
            editor.SetupParameterDefaults.Single().KeyChoices);
        editor.SetupFunctionName = SetupFunctionNames.standardTests;
        Assert.DoesNotContain(ResultsFunctionNames.sorterEval, editor.ResultsFunctionOverride.Choices);
        Assert.False(editor.FunctionsReady);
        Assert.Contains("SorterModelSet", editor.FunctionCompatibilityError);
        Assert.Equal(ResultsFunctionNames.sorterEval, editor.ResultsFunctionName);
        editor.SetupFunctionOverride.IsOverridden = false;
        Assert.True(editor.FunctionsReady);
    }

    [Fact]
    public void Switching_results_functions_keeps_shared_values_and_restores_other_defaults_in_session()
    {
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module").Editor!;
        editor.ResultsFunctionName = ResultsFunctionNames.mergeSortableTests;
        editor.AddRequiredResultsDefaultsCommand.Execute(null);
        var format = editor.ResultsParameterDefaults.Single(row => row.Key == "SortableDataFormat");
        format.Value.Text = "BitVector512";
        var library = editor.ResultsParameterDefaults.Single(row => row.Key == "MergeLibId");
        editor.ResultsFunctionName = ResultsFunctionNames.prefixSortableTests;
        Assert.Same(format, Assert.Single(editor.LocalResultsParameterDefaults));
        Assert.Equal("BitVector512", editor.ToProjectSettings().resultsParameterDefaults.Single().value);
        Assert.DoesNotContain(editor.ToLocalOverrides().resultsParameterDefaults, value => value.keyName == "MergeLibId");
        editor.AddRequiredResultsDefaultsCommand.Execute(null);
        Assert.Equal(new[] { "PrefixLibId", "SortableDataFormat" }, editor.LocalResultsParameterDefaults.Select(row => row.Key).Order());
        editor.ResultsFunctionName = ResultsFunctionNames.mergeSortableTests;
        Assert.Contains(library, editor.LocalResultsParameterDefaults);
        Assert.Same(format, editor.LocalResultsParameterDefaults.Single(row => row.Key == "SortableDataFormat"));
        Assert.Empty(editor.ResultsParameterDefaultsError);
    }

    [Fact]
    public void Source_choices_match_exact_output_and_accept_only_query_keys_supplied_by_setup()
    {
        WorkspaceSourceProject Source(string label, string text, params string[] keys)
        {
            var settings = new projectSettingsDto { resultsSortopiaDataTypes = [new() { kind = "SortableTests", value = text }], resultsQueryParameterKeys = keys };
            return new(Guid.NewGuid(), label, true, settings, new WorkspaceTreeNode(WorkspaceEntityKind.Project, label).Editor!);
        }
        var compatible = Source("Compatible", "", "ChannelCount");
        var wrongLabel = Source("Wrong text", "named", "ChannelCount");
        var wrongKeys = Source("Wrong keys", "", "Repl");
        var editor = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Module", () => [compatible, wrongLabel, wrongKeys]);
        editor.SetupFunctionName = SetupFunctionNames.evaluationPrefixMssi;
        editor.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        editor.SetupDataTypesOverride.IsOverridden = true;
        var output = editor.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests");
        // Repl is a model input for this function, so use a key outside its complete required set.
        wrongKeys.Settings.resultsQueryParameterKeys = ["Message"];
        Assert.Equal(3, output.SourceProjects.Count());
        Assert.Equal(compatible.Id, Assert.Single(output.SourceProjects, choice => choice.IsCompatible).Id);
        Assert.Contains("named", output.SourceProjects.Single(choice => choice.Id == wrongLabel.Id).IncompatibilityReason);
        Assert.Contains("Message", output.SourceProjects.Single(choice => choice.Id == wrongKeys.Id).IncompatibilityReason);
        output.SourceProjectId = wrongKeys.Id;
        Assert.Contains("not accepted", output.Error);
        output.SourceProjectId = compatible.Id;
        Assert.Empty(output.Error);
        Assert.All(editor.SetupDataTypes.Where(row => row.GeneratesData), row => Assert.Empty(row.SourceProjects));
        editor.SetupFunctionName = SetupFunctionNames.evaluationStandardMssi;
        Assert.All(editor.InheritedSetupDataTypes, row => { Assert.True(row.GeneratesData); Assert.Empty(row.SourceProjects); });
    }
}
