using System.IO;
using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceProjectQueryContractTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-project-queries-");
    private WorkspaceFolderSettings Settings => new(Path.Combine(temporary.FullName, "settings.txt"));
    private WorkspacePageViewModel Open()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
        Settings.Save(root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, Settings);
        page.AddBranchModuleCommand.Execute(null);
        page.Selected!.Editor!.LoadFixtureSettings(RunExecutionTests.Settings(true));
        page.AddProjectCommand.Execute(null);
        return page;
    }
    private static WorkspaceQueryKeyChoice Choose(WorkspaceConfigurationViewModel editor, bool setup, string key)
    {
        (setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults).Single(row => row.Key == key).ExcludeCommand!.Execute(null);
        var choice = (setup ? editor.SetupQueryOutputs : editor.ResultsQueryOutputs).Single().Keys.Single(row => row.Key == key);
        Assert.True(choice.CanChange);
        choice.Selected = true;
        return choice;
    }

    [Fact]
    public async Task Project_keys_are_selected_before_runs_and_new_runs_get_only_the_required_empty_spans()
    {
        var page = Open();
        var project = page.Selected!;
        Choose(project.Editor!, false, "SortableDataFormat");
        Choose(project.Editor!, true, "ChannelCount");
        Assert.Empty(project.Children);
        Assert.DoesNotContain("Add run spans first", project.Editor!.QueryConfigurationHint);
        page.AddRunCommand.Execute(null);
        var run = page.Selected!;
        Assert.True(run.IsRun);
        var results = Assert.Single(run.Editor!.ResultsParameters);
        var setup = Assert.Single(run.Editor.SetupParameters);
        Assert.Equal("SortableDataFormat", results.Key);
        Assert.Equal("ChannelCount", setup.Key);
        Assert.Empty(results.Values); Assert.Empty(setup.Values);
        Assert.Equal(new[] { "SortableDataFormat" }, run.Editor.PhaseContract(false).SpanKeys);
        Assert.Equal(new[] { "ChannelCount" }, run.Editor.PhaseContract(true).SpanKeys);
        Assert.Contains(run.Editor.ResultsRequiredInputs.Rows, row => row.Key == "MergeLibId" && row.Supply == "Default");
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.OperationError);
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        WorkspaceRunExecutionTests.SelectRun(page);
        run = page.Selected!;
        results = run.Editor!.ResultsParameters.Single(); setup = run.Editor.SetupParameters.Single();
        results.AddValueCommand.Execute(null); results.Values[0].Text = "Bools";
        setup.AddValueCommand.Execute(null); setup.Values[0].Text = "4";
        Assert.Empty(run.Editor.ResultsParametersError); Assert.Empty(run.Editor.SetupParametersError);
        await page.SaveWorkspaceAsync();
        WorkspaceRunExecutionTests.SelectRun(page);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
        page.Selected = page.Selected!.Parent;
        page.AddRunCommand.Execute(null);
        Assert.Empty(page.Selected!.Editor!.ResultsParameters.Single().Values);
        Assert.Empty(page.Selected.Editor.SetupParameters.Single().Values);
    }

    [Fact]
    public async Task Removing_a_query_key_preserves_legacy_run_values_and_reports_the_undeclared_span()
    {
        var page = Open();
        var project = page.Selected!;
        Choose(project.Editor!, false, "SortableDataFormat");
        page.AddRunCommand.Execute(null);
        var run = page.Selected!;
        var span = run.Editor!.ResultsParameters.Single();
        span.AddValueCommand.Execute(null); span.Values[0].Text = "Ints";
        project.Editor!.ResultsQueryOutputs.Single().Keys.Single(row => row.Key == span.Key).Selected = false;
        Assert.Contains("not a project query key", span.Error);
        Assert.Same(span, Assert.Single(run.Editor.ResultsParameters));
        Assert.True(span.IsRepair);
        Assert.Contains(run.Editor.ResultsRequiredInputs.Rows, row => row.Key == span.Key && row.Status == "Undeclared span");
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.OperationError);
        var reopened = new WorkspacePageViewModel(() => { }, _ => null, Settings);
        WorkspaceRunExecutionTests.SelectRun(reopened);
        Assert.Equal("Ints", reopened.Selected!.Editor!.ResultsParameters.Single().Values.Single().Text);
        Assert.Contains("not a project query key", reopened.Selected.Editor.ResultsParametersError);
        Assert.False(reopened.GenerateRunParametersCommand.CanExecute(null));
    }

    [Fact]
    public void Required_non_query_inputs_need_defaults_and_do_not_create_span_rows()
    {
        var page = Open();
        var project = page.Selected!;
        project.Editor!.InheritedOutputDefaults.Single(row => row.Key == "MergeLibId").ExcludeCommand!.Execute(null);
        page.AddRunCommand.Execute(null);
        var run = page.Selected!;
        Assert.True(run.IsRun);
        Assert.Empty(run.Editor!.ResultsParameters);
        Assert.Empty(run.Editor.DeclaredResultsParameters);
        Assert.Contains(run.Editor.ResultsRequiredInputs.Rows, row => row.Key == "MergeLibId" && row.Status == "Needs default" && !row.NeedsSpan);
        Assert.Contains("fixed defaults", run.Editor.ResultsParametersError);
        Assert.Same(project.Editor, run.Editor.ResultsFunctionParameters.Single(row => row.Name == "MergeLibId").TargetEditor);
    }

    [Fact]
    public async Task Output_subsets_form_one_span_contract_and_shared_keys_have_one_row()
    {
        var page = Open();
        var project = page.Selected!;
        project.Parent!.Editor!.LoadFixtureSettings(FunctionOutputContractTests.Combined(true));
        foreach (var name in new[] { "ChannelCount", "Repl" })
            project.Editor!.InheritedSetupDefaults.Single(row => row.Key == name).ExcludeCommand!.Execute(null);
        foreach (var output in project.Editor!.SetupQueryOutputs.ToArray())
            foreach (var key in output.Keys)
                key.Selected = key.Key == "ChannelCount" || output.Kind == "SorterModelSet" && key.Key == "Repl";
        Assert.Empty(project.Children);
        page.AddRunCommand.Execute(null);
        var editor = page.Selected!.Editor!;
        Assert.Equal(new[] { "ChannelCount", "Repl" }, editor.SetupParameters.Select(row => row.Key).Order());
        Assert.Empty(editor.ResultsParameters);
        foreach (var span in editor.SetupParameters)
        {
            Assert.Empty(span.Values);
            span.AddValueCommand.Execute(null);
            span.Values[0].Text = span.Key == "ChannelCount" ? "4" : "0";
        }
        Assert.Empty(editor.SetupParametersError);
        var outputs = editor.ToProjectSettings().setupDataTypes;
        Assert.Equal(new[] { "ChannelCount" }, outputs.Single(row => row.dataType.kind == "SortableTests").queryParameterKeys);
        Assert.Equal(new[] { "ChannelCount", "Repl" }, outputs.Single(row => row.dataType.kind == "SorterModelSet").queryParameterKeys);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        WorkspaceRunExecutionTests.SelectRun(page);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
    }

    [Fact]
    public void Source_setup_query_choices_respect_each_source_before_runs_exist()
    {
        var models = SourceEvaluationInputsTests.Source("SorterModelSet", "ChannelCount", "Repl");
        var tests = SourceEvaluationInputsTests.Source("SortableTests", "ChannelCount", "SortableDataFormat");
        var module = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Module", () => [models, tests]);
        module.SetupFunctionName = SetupFunctionNames.evaluationSource;
        foreach (var row in module.SetupDataTypes) row.SourceOverride!.IsOverridden = true;
        module.SetupDataTypes.Single(row => row.DataType.Kind == "SorterModelSet").SourceProjectId = models.Id;
        module.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests").SourceProjectId = tests.Id;
        var project = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Project, true, () => module, () => "Project", () => [models, tests]);
        var choices = project.SetupQueryOutputs.ToArray();
        Assert.Equal(new[] { "ChannelCount", "Repl" }, choices.Single(row => row.Kind == "SorterModelSet").Keys.Select(row => row.Key));
        Assert.Equal(new[] { "ChannelCount", "SortableDataFormat" }, choices.Single(row => row.Kind == "SortableTests").Keys.Select(row => row.Key));
        foreach (var choice in choices.SelectMany(row => row.Keys))
        {
            Assert.True(choice.CanChange);
            choice.Selected = true;
        }
        var run = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Run, true, () => project, () => "Run", () => [models, tests]);
        run.RefreshInherited();
        Assert.Equal(new[] { "ChannelCount", "Repl", "SortableDataFormat" }, run.SetupParameters.Select(row => row.Key).Order());
        Assert.All(run.SetupParameters, span => Assert.Empty(span.Values));
    }

    [Fact]
    public void Runtime_requires_exact_span_keys_including_empty_query_contracts()
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings();
        settings.resultsParameterDefaults = [];
        var parent = WorkspaceFixtures.Project(Guid.NewGuid(), "Project", SettingsDto.toDomain(hierarchyKind.Root, settings), []);
        var inputs = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("Project", "Run"));
        inputs.parentProjectId = parent.Definition.id;
        Assert.Contains("Undeclared span keys", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent.Snapshot(), inputs)).Message);
        Assert.Contains("ChannelCount", ParameterSets.querySpansError(ListModule.OfArray(new[] { "ChannelCount" }), ListModule.Empty<string>()));
        Assert.Contains("SortableDataFormat", ParameterSets.querySpansError(ListModule.OfArray(new[] { "ChannelCount" }), ListModule.OfArray(new[] { "ChannelCount", "SortableDataFormat" })));
    }

    public void Dispose() => temporary.Delete(true);
}
