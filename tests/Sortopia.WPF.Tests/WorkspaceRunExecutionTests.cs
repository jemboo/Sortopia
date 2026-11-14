using System.IO;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceRunExecutionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-execution-");
    internal static WorkspacePageViewModel Create(string folder, bool branch, Func<string, Guid?, Task>? openResultsExecution = null)
    {
        var root = Directory.CreateDirectory(Path.Combine(folder, "Workspace")).FullName;
        var settings = new WorkspaceFolderSettings(Path.Combine(folder, "settings.txt")); settings.Save(root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings, openResultsExecution: openResultsExecution);
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var configuration = RunExecutionTests.Settings(branch);
        configuration.resultsQueryParameterKeys = ["SortableDataFormat"];
        page.Selected!.Editor!.LoadProject(configuration);
        page.AddProjectCommand.Execute(null);
        page.AddRunCommand.Execute(null);
        var run = page.Selected!;
        if (branch)
        {
            // Complete the first run while constructing the rest of this workspace.
            WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false);
            WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, true);
            page.Selected = page.Roots[0]; page.AddModuleCommand.Execute(null);
            page.Selected!.Name = "Source module";
            page.Selected.Editor!.LoadProject(RunExecutionTests.Settings(false));
            page.AddProjectCommand.Execute(null);
            var source = page.Selected!; source.Name = "Source";
            page.AddRunCommand.Execute(null);
            WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(page.Selected!, false);
            configuration.setupDataTypes = [new() { dataType = configuration.resultsSortopiaDataTypes[0], sourceProjectId = source.Id, queryParameterKeys = ["ChannelCount", "SortableDataFormat"] }];
            run.Parent!.Editor!.LoadProject(configuration);
            page.Selected = run;
            run.Editor.ResultsParameters.Clear(); run.Editor.SetupParameters.Clear();
        }
        return page;
    }
    internal static void SelectRun(WorkspacePageViewModel page) => page.Selected = page.Roots[0].Children[0].Children[0].Children[0];
    internal static void SupplyQuerySpans(WorkspacePageViewModel page)
    {
        foreach (var node in page.Roots.SelectMany(root => root.DescendantsAndSelf()).Where(node => node.IsRun))
        {
            var editor = node.Editor!;
            WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(node, false);
            WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(node, true);
            foreach (var setup in new[] { false, true }) {
                if (setup && !node.IsBranch || setup && !editor.HasSetupContract || !setup && !editor.HasResultsContract) continue;
                var required = setup ? SetupFunctionCatalog.require(editor.SetupFunctionName).RequiredParameters : ResultsFunctionCatalog.require(editor.ResultsFunctionName).RequiredParameters;
                var spans = setup ? editor.SetupParameters : editor.ResultsParameters;
                var defaults = setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults;
                foreach (var key in required.Where(key => !spans.Any(span => span.Key == key.Name) && !defaults.Any(value => value.Key == key.Name))) {
                    var source = node.Parent!.Children.Where(sibling => !ReferenceEquals(sibling, node)).SelectMany(sibling => setup ? sibling.Editor!.SetupParameters : sibling.Editor!.ResultsParameters).FirstOrDefault(span => span.Key == key.Name);
                    if (source is not null) WorkspaceParameterSetsPreviewTests.AddSpan(node, key.Name, setup, source.Values.Take(1).Select(value => value.Text).ToArray());
                }
            }
            foreach (var key in editor.ResultsQueryOutputs.Concat(editor.SetupQueryOutputs).SelectMany(output => output.Keys).ToArray()) key.Selected = true;
        }
    }
    internal static async Task ExecuteSourceAsync(WorkspacePageViewModel page)
    {
        if (page.Roots[0].Children.Count > 1)
        {
            page.Selected = page.Roots[0].Children[1].Children[0].Children[0];
            var report = await GenerateAndExecuteAsync(page);
            Assert.Equal(runExecutionStatus.Completed, report.Status);
            Assert.Empty(page.Error);
        }
        SelectRun(page);
    }

    internal static async Task<ResultsExecutionPageViewModel> OpenExecutionAsync(WorkspacePageViewModel workspace)
    {
        var page = new ResultsExecutionPageViewModel(() => { }, _ => null);
        await page.SetFolderAsync(workspace.RunParametersFolder, workspace.Selected!.Id);
        Assert.Empty(page.Error);
        return page;
    }
    internal static async Task<runExecutionReport> GenerateAndExecuteAsync(WorkspacePageViewModel workspace)
    {
        await workspace.GenerateRunParametersAsync(); Assert.Empty(workspace.Error);
        using var page = await OpenExecutionAsync(workspace);
        page.SelectAllCommand.Execute(null); await page.ExecuteAsync(); Assert.Empty(page.Error);
        return page.LastReport!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generation_and_selected_execution_preserve_definitions_and_reuse_outputs(bool branch)
    {
        var page = Create(temporary.FullName, branch);
        WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected!, "SortableDataFormat", false, "Bools", "Ints", "BitVector512");
        if (branch) WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected!, "SortableDataFormat", true, "Bools");
        SupplyQuerySpans(page);
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null)); Assert.Contains("Save", page.GenerationHint);
        await page.SaveWorkspaceAsync(); SelectRun(page); await ExecuteSourceAsync(page);
        Assert.Empty(page.Error); Assert.True(page.GenerateRunParametersCommand.CanExecute(null));
        var files = Directory.GetFiles(page.WorkspaceFolder, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var report = await GenerateAndExecuteAsync(page);
        var state = page.SelectedExecution!;
        Assert.Equal(runExecutionStatus.Completed, state.Report!.Status);
        Assert.Equal(branch ? 1 : 0, state.Report.Setup.Length); Assert.Empty(state.Report.Results);
        Assert.Equal(3, report.Results.Length); Assert.Empty(report.Setup);
        Assert.False(page.IsBusy); Assert.False(page.HasUnsavedChanges);
        // Generation changes only the parent definition, capturing its settings.
        var parentDefinition = Path.Combine(page.Selected!.Parent!.GetFolderPath(page.WorkspaceFolder), Sortopia.Workspaces.Persistence.WorkspaceDefinitionFiles.Name(page.Selected.Parent.Id));
        Assert.True(page.Selected.Parent.IsSealed);
        foreach (var file in files.Where(file => file.Key != parentDefinition)) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        var outputs = report.Results.SelectMany(call => call.StoredOutputs).ToArray();
        Assert.Equal(3, outputs.Select(output => output.Reference.Id).Distinct().Count());
        var retry = await GenerateAndExecuteAsync(page);
        Assert.All(retry.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        var reopened = new WorkspacePageViewModel(() => { }, _ => null, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
        SelectRun(reopened);
        using var execution = await OpenExecutionAsync(reopened);
        execution.SelectAllCommand.Execute(null); await execution.ExecuteAsync();
        Assert.Empty(execution.Error);
        Assert.Equal(outputs.Select(output => output.Reference.Id), execution.LastReport!.Results.SelectMany(call => call.StoredOutputs).Select(output => output.Reference.Id));
        page.Selected = page.Selected!.Parent; Assert.Null(page.SelectedExecution); Assert.Empty(page.RunParametersFolder);
        page.AddRunCommand.Execute(null); Assert.False(page.SelectedExecution!.HasAttempt);
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        SupplyQuerySpans(page); await page.SaveWorkspaceAsync(); SelectRun(page);
        Assert.Same(state, page.SelectedExecution);
        page.Selected!.Editor!.DescriptionOverride.IsOverridden = true; page.Selected.Editor.DescriptionOverride.EffectiveValue = "Changed";
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        await page.GenerateRunParametersAsync(); Assert.Same(state.Report, page.SelectedExecution!.Report);
        page.Cancel(); reopened.Cancel();
    }

    [Fact]
    public async Task Unknown_catalog_selection_explains_why_generation_is_disabled()
    {
        var page = Create(temporary.FullName, true);
        page.Selected!.Parent!.Parent!.Editor!.ResultsFunctionName = "missing";
        SupplyQuerySpans(page); await page.SaveWorkspaceAsync(); SelectRun(page);
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.Contains("missing", page.Error); Assert.Contains("registered", page.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workspace_only_generates_and_opens_results_page_even_when_a_draft_is_present(bool branch)
    {
        using var execution = new ResultsExecutionPageViewModel(() => { }, _ => null);
        var navigations = 0;
        var page = Create(temporary.FullName, branch, async (path, runId) => { navigations++; await execution.SetFolderAsync(path, runId); });
        SupplyQuerySpans(page);
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null)); Assert.False(page.ExecutePageCommand.CanExecute(null));
        await page.SaveWorkspaceAsync(); SelectRun(page); await ExecuteSourceAsync(page);
        var id = page.Selected!.Id;
        await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
        Assert.Empty(page.SelectedExecution!.Report!.Results);
        Assert.Contains("Results have not been executed", page.SelectedExecution.Summary);
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(page.RunParametersFolder)!, "data", "results")));
        await page.OpenExecutePageAsync(); Assert.Equal(1, navigations);
        Assert.Equal(id, execution.SelectedRun!.Id); Assert.Equal(page.RunParametersFolder, execution.Folder);
        Assert.Equal(Sortopia.FileDb.outputState.Pending, Assert.Single(execution.Rows).State);
        Assert.False(execution.ExecuteCommand.CanExecute(null));
        execution.SelectAllCommand.Execute(null); Assert.True(execution.ExecuteCommand.CanExecute(null));
        await execution.ExecuteAsync(); Assert.Empty(execution.Error);
        Assert.Equal(runExecutionStatus.Completed, execution.LastReport!.Status); Assert.Empty(execution.LastReport.Setup);
        Assert.Equal(Sortopia.FileDb.outputState.Complete, Assert.Single(execution.Rows).State);
        page.Selected.Editor!.DescriptionOverride.IsOverridden = true; page.Selected.Editor.DescriptionOverride.EffectiveValue = "Draft";
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null)); Assert.True(page.ExecutePageCommand.CanExecute(null));
        await page.OpenExecutePageAsync(); Assert.Equal(2, navigations); Assert.True(page.HasUnsavedChanges);
        Assert.Equal("Draft", page.Selected.Editor.DescriptionOverride.EffectiveValue);
        page.Cancel();
    }
    public void Dispose() => temporary.Delete(recursive: true);
}
