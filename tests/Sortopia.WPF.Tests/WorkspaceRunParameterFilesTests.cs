using System.IO;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceRunParameterFilesTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-run-files-");

    private async Task<WorkspacePageViewModel> GenerateAsync(bool branch)
    {
        var page = WorkspaceRunExecutionTests.Create(temporary.FullName, branch);
        WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected!, "SortableDataFormat", false, "Bools", "Ints");
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        await page.SaveWorkspaceAsync();
        WorkspaceRunExecutionTests.SelectRun(page);
        await WorkspaceRunExecutionTests.ExecuteSourceAsync(page);
        await page.RefreshRunParameterFilesAsync();
        Assert.Empty(page.SelectedExecution!.Files.Error);
        Assert.Empty(page.SelectedExecution.Files.Rows);
        Assert.Contains("No run parameter files", page.SelectedExecution.Files.Summary);
        await page.GenerateRunParametersAsync();
        Assert.Empty(page.Error);
        return page;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_files_and_results_are_inspected_independently_of_preview_and_last_attempt(bool branch)
    {
        var page = await GenerateAsync(branch);
        var files = page.SelectedExecution!.Files;
        Assert.True(files.HasLoaded); Assert.Empty(files.Error); Assert.Equal(2, files.Rows.Count);
        Assert.All(files.Rows, row =>
        {
            Assert.Equal("Ready", row.FileStatus); Assert.Equal("Not generated", row.ResultsStatus);
            Assert.Contains("SortableDataFormat =", row.Parameters); Assert.Contains("MergeLibId =", row.Parameters);
            Assert.Equal(branch, row.Parameters.Contains("Setup set 1"));
        });
        using var execution = await WorkspaceRunExecutionTests.OpenExecutionAsync(page);
        execution.Rows[0].IsSelected = true;
        await execution.ExecuteAsync(); Assert.Empty(execution.Error);
        var completedPath = execution.Rows[0].Path;
        // Invalid draft values cannot alter the saved-file parameters or prevent inspection.
        page.Selected!.Editor!.ResultsParameters.Single(row => row.Key == "SortableDataFormat").Values[0].Text = "invalid";
        Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
        Assert.True(page.RefreshRunParameterFilesCommand.CanExecute(null));
        Assert.Contains("Preview unavailable", page.RunParameterPreviewSummary);
        await page.RefreshRunParameterFilesAsync();
        Assert.Equal("Complete", files.Rows.Single(row => row.Path == completedPath).ResultsStatus);
        Assert.Single(files.Rows, row => row.ResultsStatus == "Not generated");
        Assert.DoesNotContain(files.Rows, row => row.Parameters.Contains("invalid"));

        var reopened = new WorkspacePageViewModel(() => { }, _ => null,
            new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
        WorkspaceRunExecutionTests.SelectRun(reopened);
        Assert.False(reopened.SelectedExecution!.HasAttempt);
        await reopened.RefreshRunParameterFilesAsync();
        Assert.Equal(files.Rows, reopened.SelectedExecution.Files.Rows);

        // A damaged output is different from a damaged parameter file.
        var output = Assert.Single(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(page.RunParametersFolder)!, "data", "results"), "*.msgpack", SearchOption.AllDirectories));
        File.WriteAllBytes(output, [1, 2, 3]);
        await page.RefreshRunParameterFilesAsync();
        var damagedResults = files.Rows.Single(row => row.Path == completedPath);
        Assert.Equal("Ready", damagedResults.FileStatus); Assert.Equal("Invalid", damagedResults.ResultsStatus);
        Assert.NotEmpty(damagedResults.Details);
        page.Cancel(); reopened.Cancel();
    }

    [Fact]
    public async Task Refresh_retains_missing_files_and_identifies_corrupt_files_without_claiming_their_results_are_complete()
    {
        var page = await GenerateAsync(false);
        var files = page.SelectedExecution!.Files;
        var missing = files.Rows[0]; var corrupt = files.Rows[1];
        File.Delete(missing.Path);
        File.WriteAllBytes(corrupt.Path, [1, 2, 3]);
        var unknown = Path.Combine(page.RunParametersFolder, Guid.NewGuid() + ".msgpack");
        File.WriteAllBytes(unknown, [1, 2, 3]);
        await page.RefreshRunParameterFilesAsync();
        Assert.Equal(3, files.Rows.Count);
        var missingRow = files.Rows.Single(row => row.Path == missing.Path);
        Assert.Equal("Missing", missingRow.FileStatus); Assert.Equal(missing.Parameters, missingRow.Parameters);
        var invalidRow = files.Rows.Single(row => row.Path == corrupt.Path);
        Assert.Equal("Invalid", invalidRow.FileStatus); Assert.Equal(corrupt.Parameters, invalidRow.Parameters);
        var unknownRow = files.Rows.Single(row => row.Path == unknown);
        Assert.Equal("Invalid", unknownRow.FileStatus); Assert.Null(unknownRow.RunId);
        Assert.Contains("run unknown", unknownRow.Parameters); Assert.Contains("shared project folder", files.Summary);
        Assert.All(files.Rows, row => { Assert.Equal("Unknown", row.ResultsStatus); Assert.NotEmpty(row.Details); });
        page.Cancel();
    }

    [Fact]
    public async Task Runs_in_the_shared_folder_have_separate_file_lists()
    {
        var page = await GenerateAsync(false);
        var first = page.Selected!; var firstState = page.SelectedExecution!;
        page.Selected = first.Parent;
        page.AddRunCommand.Execute(null);
        var secondId = page.Selected!.Id;
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        await page.SaveWorkspaceAsync();
        page.Selected = page.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => node.Id == secondId);
        await page.RefreshRunParameterFilesAsync();
        Assert.Empty(page.SelectedExecution!.Files.Rows);
        await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
        var second = Assert.Single(page.SelectedExecution.Files.Rows);
        Assert.Equal(secondId, second.RunId);
        WorkspaceRunExecutionTests.SelectRun(page);
        await page.RefreshRunParameterFilesAsync();
        Assert.Same(firstState, page.SelectedExecution);
        Assert.Equal(2, page.SelectedExecution!.Files.Rows.Count);
        Assert.All(page.SelectedExecution.Files.Rows, row => Assert.Equal(first.Id, row.RunId));
        page.Cancel();
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
