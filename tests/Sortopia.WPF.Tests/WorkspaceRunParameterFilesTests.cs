using System.IO;
using System.Buffers;
using System.Security.Cryptography;
using MessagePack;
using Sortopia.Core.Serialization;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
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
            Assert.Equal("—", row.SuffixStages);
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

    [Theory]
    [InlineData("uniform")]
    [InlineData("prefix")]
    [InlineData("both")]
    [InlineData("setup")]
    public async Task Each_saved_combination_shows_its_suffix_lengths_after_reopening_and_draft_edits(string mode)
    {
        var branch = mode is "both" or "setup";
        var root = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Models")).FullName;
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "models-settings.txt")); settings.Save(root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        try
        {
            (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
            var editor = page.Selected!.Editor!;
            editor.ResultsFunctionName = mode switch {
                "prefix" => ResultsFunctionNames.prefixMssiSimpleSorterModels,
                "setup" => ResultsFunctionNames.sorterEval,
                _ => ResultsFunctionNames.uniformMssiSimpleSorterModels };
            if (branch) editor.SetupFunctionName = SetupFunctionNames.evaluationStandardMssiSimple;
            editor.AddRequiredResultsDefaultsCommand.Execute(null);
            if (branch) editor.AddRequiredSetupDefaultsCommand.Execute(null);
            foreach (var row in editor.ResultsParameterDefaults.Concat(editor.SetupParameterDefaults).Where(row => row.Key == "SorterCount")) row.Value.Text = "1";
            page.AddProjectCommand.Execute(null); var project = page.Selected!;
            page.AddRunCommand.Execute(null); var run = page.Selected!;
            if (mode != "setup") WorkspaceParameterSetsPreviewTests.AddSpan(run, "SorterSuccessFraction", false, "50%", "90%", "99%");
            if (mode == "prefix")
                WorkspaceParameterSetsPreviewTests.AddSpan(run, "PrefixLibId", false,
                    PrefixLibId.toString(prefixLibId.create(8, 1, prefixLibVariant.Prefix1)), PrefixLibId.toString(prefixLibId.create(8, 3, prefixLibVariant.PrefixA)));
            else if (!branch) WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", false, "8", "16");
            if (branch) WorkspaceParameterSetsPreviewTests.AddSpan(run, "SorterSuccessFraction", true, "50%", "99%");
            foreach (var key in project.Editor!.ResultsQueryOutputs.Concat(project.Editor.SetupQueryOutputs).SelectMany(output => output.Keys)) key.Selected = true;
            var resultsPreview = run.Editor!.ResultsParameterSetsPreview;
            var setupPreview = run.Editor.SetupParameterSetsPreview;
            Assert.Equal(mode != "setup", resultsPreview.HasSuffixStages);
            Assert.Equal(branch, setupPreview.HasSuffixStages);
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error); page.Selected = run;
            await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
            var files = page.SelectedExecution!.Files;
            Assert.Equal(mode == "setup" ? 2 : 6, files.Rows.Count);
            foreach (var row in files.Rows)
            {
                var item = RunParameterFiles.loadFile(row.Path);
                Assert.Equal("Ready", row.FileStatus);
                if (branch)
                {
                    Assert.Contains($"Setup: {item.SetupSuffixStages.Value:N0}", row.SuffixStages);
                    Assert.Equal(setupPreview.Rows.Single(preview => preview.Number == item.SetupSetNumber.Value).SuffixStages, item.SetupSuffixStages.Value);
                }
                else Assert.Null(item.SetupSuffixStages);
                if (mode != "setup")
                {
                    var expected = ModelGenerationStageLengths.tryForFunction(item.Results.FunctionName, item.Parameters).Value;
                    Assert.Equal(expected, item.ResultsSuffixStages.Value);
                    Assert.Equal(resultsPreview.Rows.Single(preview => preview.Number == item.SetNumber).SuffixStages, item.ResultsSuffixStages.Value);
                    Assert.Contains($"Results: {expected:N0}", row.SuffixStages);
                }
                else Assert.Null(item.ResultsSuffixStages);
                Assert.Contains("Fixed prefix stages are additional", row.SuffixStagesDetails);
            }
            if (branch) Assert.Equal(new[] { 22, 49 }, files.Rows.Select(row => RunParameterFiles.loadFile(row.Path).SetupSuffixStages.Value).Distinct().Order());
            if (mode == "prefix") Assert.Equal(new[] { 19, 21, 30, 32, 46, 48 }, files.Rows.Select(row => RunParameterFiles.loadFile(row.Path).ResultsSuffixStages.Value).Order());
            if (mode != "setup") run.Editor!.ResultsParameters.Single(row => row.Key == "SorterSuccessFraction").Values[0].Text = "invalid";
            var reopened = new WorkspaceRunParameterFiles();
            await reopened.RefreshAsync(page.RunParametersFolder, project.Id, run.Id);
            Assert.Equal(files.Rows, reopened.Rows);
            var missing = files.Rows[0]; File.Delete(missing.Path);
            await reopened.RefreshAsync(page.RunParametersFolder, project.Id, run.Id);
            var retained = reopened.Rows.Single(row => row.Path == missing.Path);
            Assert.Equal("Missing", retained.FileStatus); Assert.Equal(missing.SuffixStages, retained.SuffixStages);
            Assert.Contains("last successful read", retained.SuffixStagesDetails);
            if (mode == "uniform")
            {
                // Decode an actual old 11-field payload, not a new payload with null fields.
                var saved = files.Rows[1];
                var legacy = WriteWithoutStageMetadata(saved.Path);
                var item = RunParameterFiles.loadFile(legacy);
                Assert.Null(item.ResultsSuffixStages); Assert.Null(item.SetupSuffixStages);
                await reopened.RefreshAsync(page.RunParametersFolder, project.Id, run.Id);
                var legacyRow = reopened.Rows.Single(row => row.Path == legacy);
                Assert.Equal(saved.SuffixStages, legacyRow.SuffixStages);
                Assert.Contains("calculated from this file's saved parameters", legacyRow.SuffixStagesDetails);
            }
        }
        finally { page.Cancel(); }
    }

    private static string WriteWithoutStageMetadata(string path)
    {
        var options = MessagePackSetup.options.WithCompression(MessagePackCompression.Lz4BlockArray);
        var envelope = MessagePackSerializer.Deserialize<runParametersEnvelopeDto>(File.ReadAllBytes(path), options, TestContext.Current.CancellationToken);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(envelope.Payload));
        Assert.Equal(13, reader.ReadArrayHeader());
        int start = (int)reader.Consumed;
        for (int i = 0; i < 11; i++) reader.Skip();
        byte[] payload = [0x9b, .. envelope.Payload.AsSpan(start, (int)reader.Consumed - start).ToArray()];
        var checksum = SHA256.HashData(payload);
        var legacy = Path.Combine(Path.GetDirectoryName(path)!, new Guid(checksum.AsSpan(0, 16)) + ".msgpack");
        File.WriteAllBytes(legacy, MessagePackSerializer.Serialize(new runParametersEnvelopeDto(1, "runParameters", checksum, payload), options, TestContext.Current.CancellationToken));
        return legacy;
    }
}
