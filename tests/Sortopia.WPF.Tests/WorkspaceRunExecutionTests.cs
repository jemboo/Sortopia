using System.IO;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceRunExecutionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-execution-");
    internal static WorkspacePageViewModel Create(string folder, bool branch)
    {
        var root = Directory.CreateDirectory(Path.Combine(folder, "Workspace")).FullName;
        var settings = new WorkspaceFolderSettings(Path.Combine(folder, "settings.txt")); settings.Save(root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
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
            run.Editor!.UseResultsDefaultsAsSpansCommand.Execute(null);
            run.Editor.UseSetupDefaultsAsSpansCommand.Execute(null);
            page.Selected = page.Roots[0]; page.AddModuleCommand.Execute(null);
            page.Selected!.Name = "Source module";
            page.Selected.Editor!.LoadProject(RunExecutionTests.Settings(false));
            page.AddProjectCommand.Execute(null);
            var source = page.Selected!; source.Name = "Source";
            page.AddRunCommand.Execute(null);
            page.Selected!.Editor!.UseResultsDefaultsAsSpansCommand.Execute(null);
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
            node.Editor!.UseResultsDefaultsAsSpansCommand.Execute(null);
            node.Editor.UseSetupDefaultsAsSpansCommand.Execute(null);
        }
    }
    internal static async Task ExecuteSourceAsync(WorkspacePageViewModel page)
    {
        if (page.Roots[0].Children.Count > 1)
        {
            page.Selected = page.Roots[0].Children[1].Children[0].Children[0];
            await page.ExecuteRunAsync();
            Assert.Empty(page.Error);
        }
        SelectRun(page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execution_persists_outputs_without_changing_saved_definitions(bool branch)
    {
        var page = Create(temporary.FullName, branch);
        WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected!.Editor!, "SortableDataFormat", false, "Bools", "Ints", "BitVector512");
        if (branch) WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected.Editor!, "SortableDataFormat", true, "Bools", "Bools");
        SupplyQuerySpans(page);
        Assert.False(page.ExecuteRunCommand.CanExecute(null));
        Assert.Contains("Save", page.ExecuteHint);
        await page.SaveWorkspaceAsync(); SelectRun(page);
        await ExecuteSourceAsync(page);
        Assert.Empty(page.Error);
        Assert.True(page.ExecuteRunCommand.CanExecute(null));
        var files = Directory.GetFiles(page.WorkspaceFolder, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        await page.ExecuteRunAsync();
        var state = page.SelectedExecution!;
        Assert.Empty(page.Error);
        Assert.Equal(runExecutionStatus.Completed, state.Report!.Status);
        Assert.Equal(branch ? 2 : 0, state.Report.Setup.Length);
        Assert.Equal(branch ? 6 : 3, state.Report.Results.Length);
        Assert.False(page.IsBusy); Assert.False(page.HasUnsavedChanges);
        Assert.True(page.ExecuteRunCommand.CanExecute(null));
        foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.True(Directory.GetFiles(page.WorkspaceFolder, "*", SearchOption.AllDirectories).Length > files.Count);
        var outputs = state.Report.Results.SelectMany(call => call.StoredOutputs).ToArray();
        Assert.Equal(3, outputs.Select(output => output.Reference.Id).Distinct().Count());
        if (branch) Assert.Single(state.Report.Setup.SelectMany(call => call.StoredOutputs).Select(output => output.Reference.Id).Distinct());
        await page.ExecuteRunAsync();
        Assert.All(page.SelectedExecution!.Report!.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        var reopened = new WorkspacePageViewModel(() => { }, _ => null, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
        SelectRun(reopened);
        await reopened.ExecuteRunAsync();
        Assert.Empty(reopened.Error);
        Assert.Equal(outputs.Select(output => output.Reference.Id), reopened.SelectedExecution!.Report!.Results.SelectMany(call => call.StoredOutputs).Select(output => output.Reference.Id));
        Assert.All(reopened.SelectedExecution.Report.Setup.Concat(reopened.SelectedExecution.Report.Results).SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        page.Selected = page.Selected!.Parent;
        Assert.Null(page.SelectedExecution); Assert.False(page.ExecuteRunCommand.CanExecute(null));
        page.AddRunCommand.Execute(null);
        Assert.False(page.SelectedExecution!.HasAttempt);
        Assert.False(page.ExecuteRunCommand.CanExecute(null));
        SupplyQuerySpans(page);
        await page.SaveWorkspaceAsync(); SelectRun(page);
        Assert.Same(state, page.SelectedExecution);
        page.Selected!.Editor!.DescriptionOverride.IsOverridden = true;
        page.Selected.Editor.DescriptionOverride.EffectiveValue = "Changed";
        Assert.False(page.ExecuteRunCommand.CanExecute(null));
        await page.ExecuteRunAsync();
        Assert.Same(state.Report, page.SelectedExecution!.Report);
    }

    [Fact]
    public async Task Unknown_catalog_selection_explains_why_execute_is_disabled()
    {
        var page = Create(temporary.FullName, true);
        page.Selected!.Parent!.Parent!.Editor!.ResultsFunctionName = "missing";
        SupplyQuerySpans(page);
        await page.SaveWorkspaceAsync(); SelectRun(page);
        Assert.False(page.ExecuteRunCommand.CanExecute(null));
        Assert.Contains("missing", page.Error);
        Assert.Contains("registered", page.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UI_prepares_without_results_and_reopens_saved_preparations_for_results_only_execution(bool branch)
    {
        var page = Create(temporary.FullName, branch);
        SupplyQuerySpans(page);
        Assert.False(page.PrepareRunCommand.CanExecute(null));
        Assert.False(page.ExecutePreparedRunCommand.CanExecute(null));
        await page.SaveWorkspaceAsync(); SelectRun(page);
        await ExecuteSourceAsync(page);
        var runId = page.Selected!.Id;
        Assert.True(page.PrepareRunCommand.CanExecute(null));
        await page.PrepareRunAsync();
        Assert.Empty(page.Error);
        Assert.Empty(page.SelectedExecution!.Report!.Results);
        Assert.Contains("Results have not been executed", page.SelectedExecution.Summary);
        var choice = Assert.Single(page.Preparations);
        Assert.Equal(runId, choice.Preparation.RunId);
        Assert.True(page.ExecutePreparedRunCommand.CanExecute(null));
        Assert.Contains(choice.Id.ToString(), page.PreparedCommand);
        Assert.Contains("execute-results", page.PreparedCommand);
        Assert.False(page.HasUnsavedChanges);

        var reopened = new WorkspacePageViewModel(() => { }, _ => null, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
        SelectRun(reopened);
        Assert.Equal(choice.Id, Assert.Single(reopened.Preparations).Id);
        Assert.Equal(choice.Id, reopened.SelectedPreparation!.Id);
        await reopened.ExecutePreparedRunAsync();
        Assert.Empty(reopened.Error);
        Assert.Equal(runExecutionStatus.Completed, reopened.SelectedExecution!.Report!.Status);
        Assert.Empty(reopened.SelectedExecution.Report.Setup);
        Assert.Single(reopened.SelectedExecution.Report.Results);
        await reopened.ExecutePreparedRunAsync();
        Assert.All(reopened.SelectedExecution.Report.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));

        reopened.Selected!.Editor!.DescriptionOverride.IsOverridden = true;
        reopened.Selected.Editor.DescriptionOverride.EffectiveValue = "Changed";
        Assert.False(reopened.PrepareRunCommand.CanExecute(null));
        Assert.False(reopened.ExecutePreparedRunCommand.CanExecute(null));
        reopened.Selected = reopened.Selected.Parent;
        Assert.Empty(reopened.Preparations);
        Assert.False(reopened.ExecutePreparedRunCommand.CanExecute(null));
    }
    public void Dispose() => temporary.Delete(recursive: true);
}
