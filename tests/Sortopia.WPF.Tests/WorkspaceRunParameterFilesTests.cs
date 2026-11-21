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

    [Fact]
    public async Task Tree_execution_fill_counts_saved_files_across_runs_and_refreshes_changed_results()
    {
        var page = await GenerateAsync(false);
        var firstId = page.Selected!.Id;
        var firstPaths = page.SelectedExecution!.Files.Rows.Select(row => row.Path).ToArray();
        page.Selected = page.Selected.Parent;
        page.AddRunCommand.Execute(null); var secondId = page.Selected!.Id;
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        page.Selected.Editor!.ResultsParameters.Single(row => row.Key == "SortableDataFormat").Values[0].Text = "BitVector512";
        await page.SaveWorkspaceAsync();
        var root = page.Roots[0];
        var first = root.DescendantsAndSelf().Single(node => node.Id == firstId);
        var second = root.DescendantsAndSelf().Single(node => node.Id == secondId);
        page.Selected = second;
        await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
        var secondPath = Assert.Single(page.SelectedExecution!.Files.Rows).Path;
        await page.RefreshExecutionProgressAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, root.RunParameterFileCount);
        Assert.Equal(2, first.RunParameterFileCount); Assert.Equal(1, second.RunParameterFileCount);
        Assert.Equal(WorkspaceExecutionFill.Empty, root.ExecutionFill);
        Assert.Equal(runExecutionStatus.Completed, RunParameterExecution.executeFile(secondPath, null!, TestContext.Current.CancellationToken).Status);
        await page.RefreshExecutionProgressAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkspaceExecutionFill.Full, second.ExecutionFill);
        Assert.Equal(WorkspaceExecutionFill.Empty, first.ExecutionFill);
        Assert.Equal(WorkspaceExecutionFill.Half, root.ExecutionFill);
        Assert.Equal(1, root.CompletedRunParameterFileCount); // One of three files, not one of two runs.
        Assert.Contains("1 of 3", root.ExecutionSummary);
        foreach (var path in firstPaths)
            Assert.Equal(runExecutionStatus.Completed, RunParameterExecution.executeFile(path, null!, TestContext.Current.CancellationToken).Status);
        await page.RefreshExecutionProgressAsync(TestContext.Current.CancellationToken);
        Assert.All(root.DescendantsAndSelf(), node => Assert.Equal(WorkspaceExecutionFill.Full, node.ExecutionFill));

        var reopened = new WorkspacePageViewModel(() => { }, _ => null, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
        await reopened.RefreshExecutionProgressAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, reopened.Roots[0].CompletedRunParameterFileCount);
        Assert.Equal(WorkspaceExecutionFill.Full, reopened.Roots[0].ExecutionFill);

        var item = RunParameterFiles.loadFile(firstPaths[0]);
        var store = new SortopiaFileDb(Path.GetDirectoryName(Path.GetDirectoryName(firstPaths[0]))!, item.ProjectId);
        File.Delete(store.GetPath(Assert.Single(RunOutputFiles.keys(item)).Reference));
        await page.RefreshExecutionProgressAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, root.CompletedRunParameterFileCount);
        Assert.Equal(WorkspaceExecutionFill.Half, first.ExecutionFill);
        Assert.Equal(WorkspaceExecutionFill.Half, root.ExecutionFill);

        File.WriteAllBytes(firstPaths[1], [1, 2, 3]);
        await page.RefreshExecutionProgressAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, root.RunParameterFileCount); Assert.Equal(1, root.CompletedRunParameterFileCount);
        Assert.Equal(2, first.RunParameterFileCount);
        Assert.Equal(WorkspaceExecutionFill.Empty, first.ExecutionFill);
        Assert.Contains("could not be checked", root.ExecutionSummary);
        var unknown = Path.Combine(Path.GetDirectoryName(secondPath)!, Guid.NewGuid() + ".msgpack");
        File.WriteAllBytes(unknown, [1, 2, 3]);
        await page.RefreshExecutionProgressAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, root.RunParameterFileCount);
        Assert.Equal(WorkspaceExecutionFill.Full, second.ExecutionFill);
        foreach (var path in firstPaths.Append(secondPath).Append(unknown)) File.Delete(path);
        await page.RefreshExecutionProgressAsync(TestContext.Current.CancellationToken);
        Assert.All(root.DescendantsAndSelf(), node => { Assert.Equal(0, node.RunParameterFileCount); Assert.Equal(WorkspaceExecutionFill.Empty, node.ExecutionFill); });
        page.Cancel(); reopened.Cancel();
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
            Assert.Equal(mode != "setup", resultsPreview.HasPreviews);
            Assert.Equal(branch, setupPreview.HasPreviews);
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error); page.Selected = run;
            await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
            var files = page.SelectedExecution!.Files;
            Assert.Equal(mode == "setup" ? 2 : 6, files.Rows.Count);
            foreach (var row in files.Rows)
            {
                var item = RunParameterFiles.loadFile(row.Path);
                Assert.Equal("Ready", row.FileStatus);
                if (mode == "setup")
                {
                    var setupValues = page.SelectedExecution!.Report!.Setup.Single(set => set.SetNumber == item.SetupSetNumber.Value).Parameters;
                    Assert.Contains($"SorterModelSet: SorterSuccessFraction = {setupValues.TryFindText("SorterSuccessFraction").Value}", row.InheritedSetupQueryValues);
                    Assert.DoesNotContain("RngType", row.InheritedSetupQueryValues);
                    Assert.Contains("saved input files", row.InheritedSetupQueryDetails);
                }
                else Assert.Equal("—", row.InheritedSetupQueryValues); // Only inputs consumed by results are inherited.
                if (branch)
                {
                    Assert.Contains($"Setup: {item.SetupSuffixStages.Value:N0}", row.SuffixStages);
                    Assert.Equal(FunctionParameterSupportTests.SuffixStages(setupPreview.Rows.Single(preview => preview.Number == item.SetupSetNumber.Value)), item.SetupSuffixStages.Value);
                }
                else Assert.Null(item.SetupSuffixStages);
                if (mode != "setup")
                {
                    var expected = Assert.IsType<parameterValue.IntegerValue>(FunctionDefinition.tryPreviewValue("SuffixStages", ResultsFunctionCatalog.require(item.Results.FunctionName), item.Parameters).Value).Item;
                    Assert.Equal(expected, item.ResultsSuffixStages.Value);
                    Assert.Equal(FunctionParameterSupportTests.SuffixStages(resultsPreview.Rows.Single(preview => preview.Number == item.SetNumber)), item.ResultsSuffixStages.Value);
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
            Assert.Equal(missing.InheritedSetupQueryValues, retained.InheritedSetupQueryValues);
            Assert.Contains("last successful read", retained.InheritedSetupQueryDetails);
            if (mode == "setup")
            {
                var row = files.Rows[1];
                var item = RunParameterFiles.loadFile(row.Path);
                var store = new SortopiaFileDb(RunParameterFiles.projectFolder(page.RunParametersFolder), project.Id);
                var reference = item.SetupInputs.Single(input => input.DataType.IsSorterModelSet);
                var path = store.GetPath(reference);
                var saved = File.ReadAllBytes(path);
                File.Delete(path);
                var fresh = new WorkspaceRunParameterFiles();
                await fresh.RefreshAsync(page.RunParametersFolder, project.Id, run.Id);
                var unavailable = fresh.Rows.Single(value => value.Path == row.Path);
                Assert.Equal("Ready", unavailable.FileStatus);
                Assert.Contains("SorterModelSet: Unavailable", unavailable.InheritedSetupQueryValues);
                Assert.Contains("SortableTests: SorterSuccessFraction =", unavailable.InheritedSetupQueryValues);
                File.WriteAllBytes(path, [1, 2, 3]);
                await fresh.RefreshAsync(page.RunParametersFolder, project.Id, run.Id);
                Assert.Contains("SorterModelSet: Unavailable", fresh.Rows.Single(value => value.Path == row.Path).InheritedSetupQueryValues);
                File.WriteAllBytes(path, saved);
                await fresh.RefreshAsync(page.RunParametersFolder, project.Id, run.Id);
                Assert.Equal(row.InheritedSetupQueryValues, fresh.Rows.Single(value => value.Path == row.Path).InheritedSetupQueryValues);
            }
            if (mode == "uniform")
            {
                // Decode an actual old 11-field payload, not a new payload with null fields.
                var saved = files.Rows[1];
                var legacy = WriteWithoutStageMetadata(saved.Path);
                Assert.Contains("Unsupported runParameters schema", Assert.Throws<InvalidOperationException>(() => RunParameterFiles.loadFile(legacy)).Message);
                await reopened.RefreshAsync(page.RunParametersFolder, project.Id, run.Id);
                var legacyRow = reopened.Rows.Single(row => row.Path == legacy);
                Assert.Equal("Invalid", legacyRow.FileStatus);
                Assert.Equal("—", legacyRow.SuffixStages);
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
        reader.ReadInt32();
        int start = (int)reader.Consumed;
        for (int i = 1; i < 11; i++) reader.Skip();
        byte[] payload = [0x9b, 0x01, .. envelope.Payload.AsSpan(start, (int)reader.Consumed - start).ToArray()];
        var checksum = SHA256.HashData(payload);
        var legacy = Path.Combine(Path.GetDirectoryName(path)!, new Guid(checksum.AsSpan(0, 16)) + ".msgpack");
        File.WriteAllBytes(legacy, MessagePackSerializer.Serialize(new runParametersEnvelopeDto(1, "runParameters", checksum, payload), options, TestContext.Current.CancellationToken));
        return legacy;
    }
}
