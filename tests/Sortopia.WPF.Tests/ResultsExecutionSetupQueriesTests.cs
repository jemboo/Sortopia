using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.ViewModels.Shared;
using Sortopia.WPF.Views.Pages.ResultsExecution;
using Xunit;

namespace Sortopia.WPF.Tests;

[Collection("Workspace editor rendering")]
public sealed class ResultsExecutionSetupQueriesTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-execution-setup-values-");

    private async Task<WorkspacePageViewModel> Generate(bool sgd = false, bool reporting = false, bool generateFiles = true, int generations = 0, int snapshotInterval = 100)
    {
        var root = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        page.AddBranchModuleCommand.Execute(null);
        var module = page.Selected!.Editor!;
        module.SetupFunctionName = sgd ? SetupFunctionNames.evaluationStandardMsuf4Simple : SetupFunctionNames.evaluationStandardMssiSimple;
        module.ResultsFunctionName = sgd ? ResultsFunctionNames.standardMsuf4SimpleSgd : ResultsFunctionNames.sorterEval;
        module.AddRequiredSetupDefaultsCommand.Execute(null); module.AddRequiredResultsDefaultsCommand.Execute(null);
        module.SetupParameterDefaults.Single(row => row.Key == "SorterCount").Value.Text = "1";
        if (sgd)
        {
            module.SetupParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "8";
            var values = new Dictionary<string, string> {
                ["SorterPoolCount"] = "1", ["SorterCountPerPool"] = "1", ["SelectedSorterCountPerPool"] = "1",
                ["InitialPoolSeed"] = "0",
                ["InitialPoolSelection"] = "Random",
                ["SorterChildCount"] = "1", ["MutationMod"] = "0", ["AdjustCeLengthFrequency"] = "2", ["SortedFraction"] = "0.99",
                ["PrioritizeNewMutants"] = "true", ["DistinctSorterHashes"] = "true", ["RngType"] = "LCG", ["MutatorVariantKey"] = "V2",
                ["SorterEvalMeasure"] = Sortopia.Evaluation.SorterEvalMeasure.toCompactString(Sortopia.Evaluation.SorterEvalMeasure.stageBiasedFilterUnsorted),
                ["SeedModificationRate"] = "0.02", ["ModificationRate"] = "0.035", ["OrthoRate"] = "1", ["ParaRate"] = "1.15", ["SelfSymRate"] = "0.025" };
            foreach (var row in module.ResultsParameterDefaults) row.Value.Text = values[row.Key];
        }
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        if (sgd)
        {
            Assert.True(run.Editor!.SupportsReporting);
            run.Editor.SnapshotInterval = snapshotInterval.ToString();
            run.Editor.SnapshotCount = (generations / snapshotInterval).ToString();
            run.Editor.SummaryReporting.Interval = "1";
            Assert.Empty(run.Editor.ReportingError);
        }
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "Repl", true, "2", "10");
        WorkspaceParameterSetsPreviewTests.AddSpan(run, sgd ? "OrthoRate" : "SorterEvalType", false, sgd ? ["1", "2"] : ["V1", "V2"]);
        project.Editor!.SetupQueryOutputs.Single(row => row.Kind == "SorterModelSet").Keys.Single(key => key.Key == "Repl").Selected = true;
        project.Editor.SetupQueryOutputs.Single(row => row.Kind == "SortableTests").Keys.Single(key => key.Key == "Repl").Selected = false;
        if (sgd)
            foreach (var output in project.Editor.ResultsQueryOutputs) output.Keys.Single(key => key.Key == "OrthoRate").Selected = true;
        else Assert.Single(Assert.Single(project.Editor.ResultsQueryOutputs).Keys).Selected = true;
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        page.Selected = page.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => node.Id == run.Id);
        if (generateFiles)
        {
            await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
            Assert.Equal(4, page.SelectedExecution!.Files.Rows.Count);
        }
        return page;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sgd_generation_counts_survive_refresh_and_completed_reopen(bool reporting)
    {
        var workspace = await Generate(sgd: true, reporting: reporting, generations: 6, snapshotInterval: 2);
        try
        {
            using var model = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
            Assert.All(model.Rows, row => Assert.Equal("— / 6", row.GenerationsText));
            var selected = model.Rows[0]; selected.IsSelected = true;
            await model.ExecuteAsync(); Assert.Empty(model.Error);
            var completed = model.Rows.Single(row => row.Path == selected.Path);
            Assert.Equal("6 / 6", completed.GenerationsText);
            Assert.All(model.Rows.Where(row => row.Path != selected.Path), row => Assert.Equal("— / 6", row.GenerationsText));
            await model.RefreshAsync();
            Assert.Equal("6 / 6", model.Rows.Single(row => row.Path == selected.Path).GenerationsText);
            using var reopened = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
            Assert.Equal("6 / 6", reopened.Rows.Single(row => row.Path == selected.Path).GenerationsText);
        }
        finally { workspace.Cancel(); }
    }

    [Fact]
    public async Task Sgd_controls_validate_lock_and_extend_without_rewriting_parameters()
    {
        var page = await Generate(sgd: true, generateFiles: false, generations: 4, snapshotInterval: 2);
        try
        {
            var runId = page.Selected!.Id;
            var editor = page.Selected.Editor!;
            Assert.True(editor.UsesSnapshotControls);
            Assert.DoesNotContain(page.Selected.Parent!.Parent!.Editor!.ResultsParameterDefaults, row => row.Key == "GenerationCount");
            editor.SnapshotInterval = "0";
            Assert.Contains("positive", editor.ReportingError);
            Assert.False(page.GenerateRunParametersCommand.CanExecute(null));
            editor.SnapshotInterval = "2";
            editor.SnapshotCount = "-1";
            Assert.Contains("negative", editor.ReportingError);
            editor.SnapshotCount = int.MaxValue.ToString();
            Assert.Contains("limit", editor.ReportingError);
            editor.SnapshotCount = "2";
            Assert.Empty(editor.ReportingError);
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
            page.Selected = page.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => node.Id == runId);
            await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
            editor = page.Selected!.Editor!;
            var files = Directory.GetFiles(page.RunParametersFolder, "*.msgpack").ToDictionary(path => path, File.ReadAllBytes);
            string completedPath;
            using (var initial = await WorkspaceRunExecutionTests.OpenExecutionAsync(page))
            {
                var row = initial.Rows[0]; row.IsSelected = true; completedPath = row.Path;
                await initial.ExecuteAsync(); Assert.Empty(initial.Error);
                Assert.Equal(outputState.Complete, initial.Rows.Single(r => r.Path == completedPath).State);
            }
            editor.SnapshotInterval = "3";
            Assert.Equal("2", editor.SnapshotInterval);
            Assert.False(editor.CanEditSnapshotInterval);
            Assert.True(editor.CanEditSnapshotCount);
            editor.SnapshotCount = "1";
            Assert.Contains("only increase", editor.ReportingError);
            editor.SnapshotCount = "3";
            Assert.Empty(editor.ReportingError);
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
            page.Selected = page.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => node.Id == runId);
            foreach (var file in files)
            {
                Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
                Assert.Equal(3, RunParameterFiles.loadFile(file.Key).Sgd.Value.SnapshotCount);
            }
            using var execution = await WorkspaceRunExecutionTests.OpenExecutionAsync(page);
            Assert.All(execution.Rows, row => Assert.Equal(6, row.TotalGenerations));
            var extended = execution.Rows.Single(row => row.Path == completedPath);
            Assert.Equal(outputState.Partial, extended.State);
            extended.IsSelected = true;
            await execution.ExecuteAsync(); Assert.Empty(execution.Error);
            Assert.Equal("6 / 6", execution.Rows.Single(row => row.Path == completedPath).GenerationsText);
        }
        finally { page.Cancel(); }
    }
    [Fact]
    public async Task Validation_cache_rechecks_bytes_and_never_hides_changed_missing_or_damaged_outputs()
    {
        var workspace = await Generate(sgd: true, reporting: true);
        try
        {
            using var page = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
            page.SelectAllCommand.Execute(null);
            await page.ExecuteAsync(); Assert.Empty(page.Error);
            var path = page.Rows[0].Path;
            var item = RunParameterFiles.loadFile(path);
            var folder = Path.GetDirectoryName(workspace.RunParametersFolder)!;
            var store = new SortopiaFileDb(folder, item.ProjectId);
            var filesBefore = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order().ToArray();
            var cache = new ResultsOutputValidationCache();
            Assert.False(cache.Inspect(path, CancellationToken.None).Reused);
            var reused = cache.Inspect(path, CancellationToken.None);
            Assert.True(reused.Reused);
            Assert.Equal(outputState.Complete, reused.Inspection.Outputs.State);

            foreach (var key in RunOutputFiles.keys(item))
            {
                var output = store.GetPath(key.Reference);
                var original = File.ReadAllBytes(output);
                var timestamp = File.GetLastWriteTimeUtc(output);
                var damaged = (byte[])original.Clone(); damaged[^1] ^= 1;
                File.WriteAllBytes(output, damaged);
                File.SetLastWriteTimeUtc(output, timestamp); // Size/dates alone cannot detect this change.
                var invalid = cache.Inspect(path, CancellationToken.None);
                Assert.False(invalid.Reused);
                Assert.Equal(outputState.Invalid, invalid.Inspection.Outputs.State);
                File.WriteAllBytes(output, original);
                Assert.False(cache.Inspect(path, CancellationToken.None).Reused);
                Assert.True(cache.Inspect(path, CancellationToken.None).Reused);
                File.Delete(output);
                var missing = cache.Inspect(path, CancellationToken.None);
                Assert.False(missing.Reused);
                Assert.Equal(outputState.Partial, missing.Inspection.Outputs.State);
                File.WriteAllBytes(output, original);
                Assert.False(cache.Inspect(path, CancellationToken.None).Reused);
                Assert.True(cache.Inspect(path, CancellationToken.None).Reused);
            }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => cache.Inspect(path, cancelled.Token));
            var parameterBytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, [0]);
            Assert.ThrowsAny<Exception>(() => cache.Inspect(path, CancellationToken.None));
            File.WriteAllBytes(path, parameterBytes);
            Assert.Equal(filesBefore, Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order());
        }
        finally { workspace.Cancel(); }
    }

    [Fact]
    public async Task Evolution_reporting_controls_freeze_schedules_in_parameter_files_and_export_both_levels()
    {
        var workspace = await Generate(sgd: true, reporting: true);
        try
        {
            var editor = workspace.Selected!.Editor!;
            Assert.True(editor.ReportingEnabled);
            editor.SnapshotInterval = "5";
            Assert.Equal("100", editor.SnapshotInterval);
            workspace.SelectConfigurationItem("evolutionReporting");
            Assert.Equal("Evolution reporting", workspace.ActiveItemTitle);
            Assert.False(workspace.ShowParentDefaultsAction);
            Exception? renderFailure = null;
            var renderThread = new Thread(() => {
                try
                {
                    var view = new Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor { DataContext = editor, ActiveItemKey = "setupFunction" };
                    var section = (Expander)view.FindName("EvolutionReportingSection");
                    Assert.Equal(Visibility.Collapsed, section.Visibility);
                    view.ActiveItemKey = "resultsFunction";
                    Assert.Equal(Visibility.Collapsed, section.Visibility);
                    view.ActiveItemKey = workspace.ActiveItemKey;
                    view.Measure(new Size(800, double.PositiveInfinity));
                    view.Arrange(new Rect(0, 0, 800, view.DesiredSize.Height)); view.UpdateLayout();
                    Assert.Equal(Visibility.Visible, section.Visibility);
                    Assert.True(section.IsExpanded);
                    var interval = Descendants(section).OfType<TextBox>().Single(box => System.Windows.Automation.AutomationProperties.GetName(box) == "SnapshotInterval");
                    var count = Descendants(section).OfType<TextBox>().Single(box => System.Windows.Automation.AutomationProperties.GetName(box) == "SnapshotCount");
                    Assert.Equal("100", interval.Text);
                    Assert.False(interval.IsEnabled);
                    Assert.True(count.IsEnabled);
                    Assert.Equal("100", editor.SnapshotInterval);
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    view.Measure(new Size(800, double.PositiveInfinity));
                    view.Arrange(new Rect(0, 0, 800, view.DesiredSize.Height)); view.UpdateLayout();
                    if (Environment.GetEnvironmentVariable("SORTOPIA_REPORTING_PREVIEW") is { Length: > 0 } path)
                    {
                        var visual = new DrawingVisual();
                        using (var context = visual.RenderOpen())
                        {
                            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(19, 27, 39)), null, new Rect(0, 0, section.ActualWidth, section.ActualHeight));
                            context.DrawRectangle(new VisualBrush(section), null, new Rect(0, 0, section.ActualWidth, section.ActualHeight));
                        }
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(section.ActualWidth), (int)Math.Ceiling(section.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(visual);
                        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var stream = File.Create(path); png.Save(stream);
                    }
                }
                catch (Exception error) { renderFailure = error; }
            });
            renderThread.SetApartmentState(ApartmentState.STA); renderThread.Start(); renderThread.Join();
            if (renderFailure is not null) ExceptionDispatchInfo.Capture(renderFailure).Throw();
            using var execution = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
            Assert.All(execution.Rows, row =>
            {
                Assert.Equal("Constant(100)", row.Parameters!.Reporting.Value.SnapshotSchedule);
                Assert.Equal("Constant(1)", row.Parameters.Reporting.Value.SummarySchedule);
            });
            execution.SelectAllCommand.Execute(null);
            await execution.ExecuteAsync(); Assert.Empty(execution.Error);
            Assert.All(execution.Rows, row => Assert.Contains("evolution reports complete", row.OutputSummary));
            var destination = Path.Combine(temporary.FullName, "Evolution reports");
            await execution.ExportReportsAsync(destination); Assert.Empty(execution.Error);
            // Zero generations still records the baseline exactly once for each of four combinations.
            Assert.Contains("4 evolution summary rows and 4 snapshot network rows", execution.Status);
            Assert.Equal(5, File.ReadAllLines(Path.Combine(destination, "EvolutionSummary.csv")).Length);
            Assert.Equal(5, File.ReadAllLines(Path.Combine(destination, "Snapshots.csv")).Length);
        }
        finally { workspace.Cancel(); }
    }

    [Fact]
    public async Task Sgd_sweep_exposes_CSV_export_and_combines_the_selected_final_networks()
    {
        var workspace = await Generate(sgd: true);
        try
        {
            using var model = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
            model.SelectAllCommand.Execute(null);
            Assert.False(model.CanExportReports);
            await model.ExecuteAsync(); Assert.Empty(model.Error);
            Assert.Equal(4, model.SelectedCount); Assert.True(model.CanExportReports);
            Assert.All(model.Rows, row => Assert.True(Assert.Single(row.Parameters!.Results.DataTypes).Key.IsSorterPoolSet));
            var destination = Path.Combine(temporary.FullName, "SGD reports");
            await model.ExportReportsAsync(destination); Assert.Empty(model.Error);
            Assert.Contains("4 summary rows and 4 detail rows", model.Status);
            var lines = File.ReadAllLines(Path.Combine(destination, "Details.csv"));
            Assert.Equal(5, lines.Length);
            Assert.Contains("Results.OrthoRate", lines[0]); Assert.Contains("Setup.SorterModelSet.Query.Repl", lines[0]);
            Assert.Contains("StageCrossingsCount", lines[0]); Assert.Contains("ReflectiveCount", lines[0]);
            Assert.All(lines.Skip(1), line => Assert.Contains(ResultsFunctionNames.standardMsuf4SimpleSgd, line));
            Assert.All(model.Rows, row => { Assert.True(row.IsSelected); Assert.Equal("Complete", row.Status); });
        }
        finally { workspace.Cancel(); }
    }

    [Fact]
    public async Task Execution_and_workspace_tables_share_saved_values_and_refresh_missing_inputs_in_a_copied_project()
    {
        var workspace = await Generate();
        try
        {
            using var execution = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
            Assert.Equal(4, execution.FileCount);
            Assert.DoesNotContain("Repl", execution.ParameterColumns);
            Assert.Equal(2, execution.Rows.Select(row => row.InheritedSetupQueryValues).Distinct().Count());
            foreach (var row in execution.Rows)
            {
                var expected = workspace.SelectedExecution!.Files.Rows.Single(file => file.Path == row.Path);
                Assert.Equal(expected.InheritedSetupQueryValues, row.InheritedSetupQueryValues);
                Assert.Equal(expected.InheritedSetupQueryDetails, row.InheritedSetupQueryDetails);
                Assert.Contains("SorterModelSet: Repl =", row.InheritedSetupQueryValues);
                Assert.Contains("SortableTests: No query keys", row.InheritedSetupQueryValues);
                Assert.DoesNotContain("SorterCount", row.InheritedSetupQueryValues);
                Assert.True(row.HasSetupInputs);
            }
            var source = RunParameterFiles.projectFolder(workspace.RunParametersFolder);
            var copy = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Portable project")).FullName;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(copy, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
            }
            await execution.SetFolderAsync(Path.Combine(copy, "runParameters"), workspace.Selected!.Id);
            Assert.Empty(execution.Error); Assert.Equal(4, execution.FileCount);
            Assert.All(execution.Rows, row => Assert.Contains(copy, row.InheritedSetupQueryDetails));
            var selected = execution.Rows[0];
            var originalValues = selected.InheritedSetupQueryValues;
            var store = new SortopiaFileDb(copy, selected.Parameters!.ProjectId);
            var reference = selected.Parameters.SetupInputs.Single(input => input.DataType.IsSorterModelSet);
            var inputPath = store.GetPath(reference);
            var inputBytes = File.ReadAllBytes(inputPath);
            File.Delete(inputPath);
            await execution.RefreshAsync();
            Assert.Contains("SorterModelSet: Unavailable", execution.Rows.Single(row => row.Path == selected.Path).InheritedSetupQueryValues);
            Assert.Contains(execution.Rows, row => row.InheritedSetupQueryValues.Contains("SorterModelSet: Repl ="));
            File.WriteAllBytes(inputPath, [1, 2, 3]);
            await execution.RefreshAsync();
            Assert.Contains("SorterModelSet: Unavailable", execution.Rows.Single(row => row.Path == selected.Path).InheritedSetupQueryValues);
            File.WriteAllBytes(inputPath, inputBytes);
            await execution.RefreshAsync();
            selected = execution.Rows.Single(row => row.Path == selected.Path);
            Assert.Equal(originalValues, selected.InheritedSetupQueryValues);
            var parameterBytes = File.ReadAllBytes(selected.Path);
            selected.IsSelected = true; File.Delete(selected.Path);
            await execution.ExecuteAsync();
            var missing = execution.Rows.Single(row => row.Path == selected.Path);
            Assert.Equal("Failed", missing.Status); Assert.Equal(originalValues, missing.InheritedSetupQueryValues);
            Assert.Contains("last successful read", missing.InheritedSetupQueryDetails);
            File.WriteAllBytes(selected.Path, parameterBytes);
            await execution.RefreshAsync(); await execution.ExecuteAsync();
            Assert.Empty(execution.Error);
            Assert.Equal("Complete", execution.Rows.Single(row => row.Path == selected.Path).Status);
            Assert.Equal(originalValues, execution.Rows.Single(row => row.Path == selected.Path).InheritedSetupQueryValues);
            var invalid = Path.Combine(copy, "runParameters", Guid.NewGuid() + ".msgpack"); File.WriteAllBytes(invalid, [1, 2, 3]);
            await execution.SetFolderAsync(Path.Combine(copy, "runParameters"));
            Assert.Equal("Unavailable", execution.Rows.Single(row => row.Path == invalid).InheritedSetupQueryValues);
        }
        finally { workspace.Cancel(); }
    }

    [Fact]
    public async Task Reports_export_only_the_selected_completed_evaluations_and_keep_selection_and_results()
    {
        var workspace = await Generate();
        try
        {
            using var model = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
            model.SelectAllCommand.Execute(null); Assert.False(model.CanExportReports);
            await model.ExecuteAsync(); Assert.Empty(model.Error); Assert.True(model.CanExportReports);
            model.ClearSelectionCommand.Execute(null); Assert.False(model.CanExportReports);
            var row = model.Rows[0]; row.IsSelected = true; Assert.True(model.ExportReportsCommand.CanExecute(null));
            model.ParallelJobs = "invalid"; Assert.True(model.CanExportReports);
            var destination = Path.Combine(temporary.FullName, "Reports");
            await model.ExportReportsAsync(destination); Assert.Empty(model.Error);
            Assert.Contains("1 summary rows and 1 detail rows", model.Status);
            Assert.Equal(2, File.ReadAllLines(Path.Combine(destination, "Summary.csv")).Length);
            Assert.Equal(2, File.ReadAllLines(Path.Combine(destination, "Details.csv")).Length);
            Assert.True(row.IsSelected); Assert.All(model.Rows, file => Assert.Equal("Complete", file.Status));
            Assert.Contains("Setup.SorterModelSet.Query.Repl", File.ReadAllText(Path.Combine(destination, "Summary.csv")));
            await model.ExportReportsAsync(destination); Assert.Empty(model.Error);
            await model.ExportReportsAsync(); Assert.Empty(model.Error);
            var reportsFolder = Path.Combine(Path.GetDirectoryName(model.Folder)!, "data", "results", "reports");
            Assert.Equal(File.ReadAllBytes(Path.Combine(destination, "Summary.csv")), File.ReadAllBytes(Path.Combine(reportsFolder, "Summary.csv")));
            await model.ExportReportsAsync(); Assert.Empty(model.Error);
            Assert.Empty(Directory.GetDirectories(reportsFolder));
            Assert.Equal(File.ReadAllBytes(Path.Combine(reportsFolder, "Details.csv")), File.ReadAllBytes(Path.Combine(destination, "Details.csv")));
        }
        finally { workspace.Cancel(); }
    }

    [Fact]
    public async Task Setup_values_are_visible_and_copyable_without_an_execution_error()
    {
        var workspace = await Generate();
        try
        {
            using var model = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
            Exception? failure = null;
            var thread = new Thread(() => {
                try
                {
                    var view = new ResultsExecutionPage { DataContext = model };
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(view.Dispatcher));
                    void Layout() { view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); view.Measure(new Size(1300, 850)); view.Arrange(new Rect(0, 0, 1300, 850)); view.UpdateLayout(); }
                    Layout();
                    var panel = (ResultsExecutionPanel)view.FindName("ExecutionPanel");
                    var grid = (DataGrid)panel.FindName("RunParametersGrid");
                    Assert.Same(model.ExportReportsCommand, ((Button)panel.FindName("ExportReportsButton")).Command);
                    var column = Assert.IsAssignableFrom<DataGridTextColumn>(grid.Columns.Single(column => Equals(column.Header, "Setup · Repl")));
                    Assert.Equal("SetupQueries.Cells[0].Text", Assert.IsType<Binding>(column.Binding).Path.Path);
                    Assert.Equal("SetupQueries.Cells[0].SortValue", column.SortMemberPath);
                    CheckSorting(grid, column, Layout);
                    var row = model.Rows[0]; Assert.False(row.HasError); grid.SelectedItem = row; Layout();
                    var container = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(row);
                    var details = Descendants(container).OfType<Expander>().Single(item => Equals(item.Header, "Inherited setup query values"));
                    Assert.Equal(Visibility.Visible, details.Visibility); details.IsExpanded = true; Layout();
                    var text = Assert.Single(Descendants(details).OfType<TextBox>());
                    Assert.True(text.IsReadOnly); Assert.Equal(row.InheritedSetupQueryDetails, text.Text);
                    text.SelectAll(); Assert.Equal(text.Text, text.SelectedText);
                    Assert.InRange(text.ActualWidth, 200, grid.ActualWidth - 24);
                    Assert.Equal(6, grid.FrozenColumnCount); Assert.True(grid.IsReadOnly);
                    workspace.SelectConfigurationItem("execution");
                    var workspaceView = new Sortopia.WPF.Views.Pages.Workspace.WorkspacePage { DataContext = workspace };
                    void WorkspaceLayout() { workspaceView.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); workspaceView.Measure(new Size(1400, 1000)); workspaceView.Arrange(new Rect(0, 0, 1400, 1000)); workspaceView.UpdateLayout(); }
                    WorkspaceLayout();
                    // Opening this section starts an asynchronous refresh. Finish
                    // loading before sorting so replacing ItemsSource cannot reset it.
                    var refresh = workspace.RefreshRunParameterFilesAsync();
                    if (!refresh.IsCompleted)
                    {
                        var frame = new DispatcherFrame();
                        refresh.GetAwaiter().OnCompleted(() => frame.Continue = false);
                        Dispatcher.PushFrame(frame);
                    }
                    refresh.GetAwaiter().GetResult(); WorkspaceLayout();
                    Assert.False(workspace.SelectedExecution!.Files.IsRefreshing);
                    var files = (DataGrid)workspaceView.FindName("RunParameterFilesTable");
                    var setupColumn = files.Columns.Single(column => Equals(column.Header, "Setup · Repl"));
                    Assert.Equal(4, files.Items.Count);
                    CheckSorting(files, setupColumn, WorkspaceLayout);
                }
                catch (Exception error) { failure = error; }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { workspace.Cancel(); }
    }

    [Fact]
    public async Task Cached_input_metadata_cannot_be_reused_by_a_different_project()
    {
        var workspace = await Generate();
        try
        {
            var item = RunParameterFiles.loadFile(workspace.SelectedExecution!.Files.Rows[0].Path);
            var query = new SavedSetupQueries(RunParameterFiles.projectFolder(workspace.RunParametersFolder));
            Assert.Contains("Repl =", query.Read(item, TestContext.Current.CancellationToken).Values);
            var other = new runParameters(item.Id, Guid.NewGuid(), item.RunId, item.Results, item.Parameters, item.SetupInputs,
                item.SetNumber, item.SetupSetNumber, item.SetupSuffixStages, item.ResultsSuffixStages, item.Reporting, item.Sgd);
            Assert.DoesNotContain("Repl =", query.Read(other, TestContext.Current.CancellationToken).Values);
            Assert.Contains("not a setup file in this project", query.Read(other, TestContext.Current.CancellationToken).Details);
        }
        finally { workspace.Cancel(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void CheckSorting(DataGrid grid, DataGridColumn column, Action layout)
    {
        Assert.True(grid.CanUserSortColumns); Assert.True(column.CanUserSort);
        void Click()
        {
            var header = Descendants(grid).OfType<DataGridColumnHeader>().Single(header => header.Column == column);
            typeof(DataGridColumnHeader).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(header, null);
            layout();
        }
        Click();
        Assert.Equal(ListSortDirection.Ascending, column.SortDirection);
        Assert.Equal(new[] { "2", "2", "10", "10" }, grid.Items.Cast<ISetupQueryRow>().Select(row => row.SetupQueries.Cells[0].Text));
        Click();
        Assert.Equal(ListSortDirection.Descending, column.SortDirection);
        Assert.Equal(new[] { "10", "10", "2", "2" }, grid.Items.Cast<ISetupQueryRow>().Select(row => row.SetupQueries.Cells[0].Text));
    }
    public void Dispose() => temporary.Delete(true);
}
