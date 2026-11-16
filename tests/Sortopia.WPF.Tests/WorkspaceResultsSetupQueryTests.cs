using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceResultsSetupQueryTests
{
    private static (WorkspaceTreeNode Module, WorkspaceTreeNode Project, WorkspaceTreeNode Run) Evaluation()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Evaluation", family: hierarchyKind.Branch);
        module.Editor!.SetupFunctionName = SetupFunctionNames.evaluationStandardMssiSimple;
        module.Editor.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        module.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch);
        module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch);
        project.Children.Add(run);
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", true, "8", "16");
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "Repl", true, "0", "1");
        foreach (var output in project.Editor!.SetupQueryOutputs.ToArray())
            foreach (var key in output.Keys)
                if (key.Key == "ChannelCount" || output.Kind == "SorterModelSet" && key.Key == "Repl") key.Selected = true;
        return (module, project, run);
    }

    [Fact]
    public void Branch_results_show_setup_keys_by_consumed_type_without_adding_results_parameters()
    {
        var (_, project, run) = Evaluation();
        foreach (var editor in new[] { project.Editor!, run.Editor! })
        {
            var output = Assert.Single(editor.ResultsQueryOutputs);
            Assert.True(output.HasSetupInputs);
            Assert.True(output.HasInheritedSetupKeys);
            Assert.False(output.HasNoQueryKeys);
            Assert.Equal(new[] { "ChannelCount", "Repl" }, output.InheritedSetupQueries.Single(input => input.Kind == "SorterModelSet").Keys);
            Assert.Equal(new[] { "ChannelCount" }, output.InheritedSetupQueries.Single(input => input.Kind == "SortableTests").Keys);
            Assert.Empty(output.Keys);
            Assert.Equal("Additional results query parameters: none selected", output.SelectedKeysSummary);
            Assert.Contains("Different setup input files produce separate results", output.Summary);
            Assert.Contains("inherited setup keys: ChannelCount, Repl", output.InspectorSummary);
            Assert.Contains("additional results keys: none", output.InspectorSummary);
            Assert.Equal(output.InspectorSummary, editor.InspectorSnapshots().Single(item => item.Key == "resultsTypes").Value);
            Assert.Empty(Assert.Single(editor.ToProjectSettings().resultsDataTypes).queryParameterKeys);
            Assert.All(editor.SetupQueryOutputs, row => Assert.Empty(row.InheritedSetupQueries));
        }
        Assert.Empty(run.Editor!.ResultsParameters);
        Assert.Empty(run.Editor.ResultsParametersError);
    }

    [Fact]
    public void Results_keys_and_inherited_setup_keys_remain_independent_and_follow_project_changes()
    {
        var (_, project, run) = Evaluation();
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "SorterEvalType", false, "V1", "V2");
        Assert.Contains("No additional results query keys", run.Editor!.ResultsProjectionNotice);
        Assert.Contains("Different setup files", run.Editor.ResultsProjectionNotice);
        Assert.Single(Assert.Single(project.Editor!.ResultsQueryOutputs).Keys).Selected = true;
        Assert.Equal("Additional results query parameters: SorterEvalType", Assert.Single(run.Editor.ResultsQueryOutputs).SelectedKeysSummary);
        Assert.Equal(new[] { "SorterEvalType" }, Assert.Single(project.Editor.ToProjectSettings().resultsDataTypes).queryParameterKeys);
        Assert.Empty(run.Editor.ResultsProjectionNotice);
        project.Editor.SetupQueryOutputs.Single(row => row.Kind == "SorterModelSet").Keys.Single(key => key.Key == "Repl").Selected = false;
        Assert.Equal(new[] { "ChannelCount" }, Assert.Single(run.Editor.ResultsQueryOutputs).InheritedSetupQueries.Single(row => row.Kind == "SorterModelSet").Keys);
        project.Editor.Freeze();
        var frozen = Assert.Single(run.Editor.ResultsQueryOutputs);
        Assert.False(Assert.Single(frozen.Keys).CanChange);
        Assert.All(frozen.InheritedSetupQueries, input => Assert.Equal(new[] { "ChannelCount" }, input.Keys));
    }

    [Fact]
    public void Results_that_do_not_consume_setup_data_do_not_claim_its_query_keys()
    {
        var (module, _, run) = Evaluation();
        module.Editor!.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
        var output = Assert.Single(run.Editor!.ResultsQueryOutputs);
        Assert.False(output.HasSetupInputs);
        Assert.Empty(output.InheritedSetupQueries);
        Assert.Equal("Query parameters: none selected", output.SelectedKeysSummary);
        Assert.True(output.HasNoQueryKeys);
    }

    [Fact]
    public void Empty_setup_projections_still_explain_that_distinct_input_files_distinguish_results()
    {
        var (_, project, run) = Evaluation();
        foreach (var key in project.Editor!.SetupQueryOutputs.SelectMany(row => row.Keys).ToArray()) key.Selected = false;
        var output = Assert.Single(run.Editor!.ResultsQueryOutputs);
        Assert.True(output.HasSetupInputs);
        Assert.False(output.HasInheritedSetupKeys);
        Assert.All(output.InheritedSetupQueries, input => Assert.Equal("No setup query keys selected", input.KeysSummary));
        Assert.Contains("Different setup input files produce separate results", output.Summary);
    }
}
