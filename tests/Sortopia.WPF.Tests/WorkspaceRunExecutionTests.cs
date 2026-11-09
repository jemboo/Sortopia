using System.IO;
using Sortopia.Functions;
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
        page.Selected!.Editor!.LoadProject(RunExecutionTests.Settings(branch));
        page.AddProjectCommand.Execute(null);
        page.AddRunCommand.Execute(null);
        return page;
    }
    internal static void SelectRun(WorkspacePageViewModel page) => page.Selected = page.Roots[0].Children[0].Children[0].Children[0];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execution_uses_saved_defaults_and_spans_without_editing_or_saving_outputs(bool branch)
    {
        var page = Create(temporary.FullName, branch);
        WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected!.Editor!, "Repl", false, "1", "2", "3");
        if (branch) WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected.Editor!, "Repl", true, "4", "5");
        Assert.False(page.ExecuteRunCommand.CanExecute(null));
        Assert.Contains("Save", page.ExecuteHint);
        await page.SaveWorkspaceAsync(); SelectRun(page);
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
        Assert.Equal(files.Count, Directory.GetFiles(page.WorkspaceFolder, "*", SearchOption.AllDirectories).Length);
        page.Selected = page.Selected!.Parent;
        Assert.Null(page.SelectedExecution); Assert.False(page.ExecuteRunCommand.CanExecute(null));
        page.AddRunCommand.Execute(null);
        Assert.False(page.SelectedExecution!.HasAttempt);
        Assert.False(page.ExecuteRunCommand.CanExecute(null));
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
        page.Selected!.Editor!.SetupFunctionOverride.IsOverridden = true;
        page.Selected.Editor.SetupFunctionOverride.EffectiveValue = "missing";
        await page.SaveWorkspaceAsync(); SelectRun(page);
        Assert.False(page.ExecuteRunCommand.CanExecute(null));
        Assert.Contains("missing", page.ExecuteHint);
        Assert.Contains("catalog", page.ExecuteHint);
    }
    public void Dispose() => temporary.Delete(recursive: true);
}
