using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.ViewModels.Pages.FileInspector;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Sortopia.WPF.Views.Pages.ResultsExecution;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels;
using Sortopia.WPF.ViewModels.Pages.Home;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.Runs;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace Sortopia.WPF.Tests;

[Collection("Workspace editor rendering")]
public sealed class ResultsExecutionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-results-ui-");

    private async Task<WorkspacePageViewModel> GenerateFiles(int repetitions = 1)
    {
        var page = WorkspaceRunExecutionTests.Create(temporary.FullName, false);
        WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected!, "SortableDataFormat", false,
            Enumerable.Range(0, repetitions).SelectMany(_ => new[] { "Bools", "Ints", "BitVector512" }).ToArray());
        page.Selected!.Editor!.ResultsQueryOutputs.Single().Keys.Single().Selected = true;
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error); WorkspaceRunExecutionTests.SelectRun(page);
        await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
        return page;
    }

    [Fact]
    public async Task Parallel_jobs_are_validated_remembered_and_execute_multiple_rows()
    {
        var workspace = await GenerateFiles();
        var settings = new ResultsExecutionSettings(Path.Combine(temporary.FullName, "parallel-jobs.txt"));
        Assert.Equal(1, settings.Load());
        using var model = new ResultsExecutionPageViewModel(() => { }, _ => null, settings.Load, settings.Save);
        await model.SetFolderAsync(workspace.RunParametersFolder);
        model.SelectAllCommand.Execute(null);
        foreach (var invalid in new[] { "", "0", "-1", "1.5", "abc" })
        {
            model.ParallelJobs = invalid;
            Assert.NotEmpty(model.ParallelJobsError); Assert.False(model.CanExecute);
        }
        model.ParallelJobs = "2";
        Assert.Empty(model.ParallelJobsError); Assert.True(model.CanExecute); Assert.Equal(2, settings.Load());
        await model.ExecuteAsync(); Assert.Empty(model.Error);
        Assert.Equal(3, model.LastReport!.Results.Length);
        Assert.All(model.Rows, row => Assert.Equal("Complete", row.Status));
        Assert.All(model.Rows, row => Assert.NotNull(row.Elapsed));
        Assert.Equal(3, model.ExecutionProgress.Total); Assert.Equal(3, model.ExecutionProgress.Finished);
        Assert.Equal(0, model.ExecutionProgress.Running); Assert.Equal(100, model.ExecutionProgress.Percent);
        using var reopened = new ResultsExecutionPageViewModel(() => { }, _ => null, settings.Load, settings.Save);
        Assert.Equal("2", reopened.ParallelJobs);
        workspace.Cancel();
    }

    [Fact]
    public async Task Successful_timings_survive_refresh_and_only_the_retried_row_resets()
    {
        var workspace = await GenerateFiles();
        var clock = new ResultsExecutionProgressTests.Clock();
        using var model = new ResultsExecutionPageViewModel(() => { }, _ => null, timeProvider: clock);
        await model.SetFolderAsync(workspace.RunParametersFolder);
        Assert.All(model.Rows, row => Assert.Null(row.Elapsed));
        foreach (var row in model.Rows) row.PropertyChanged += (_, change) => {
            if (change.PropertyName != nameof(row.Status) || row.Status != "Running") return;
            Assert.Equal(1, model.ExecutionProgress.Running); Assert.Equal(3, model.ExecutionProgress.Total);
            clock.Advance(TimeSpan.FromSeconds(3));
            model.RefreshElapsedTimes();
            Assert.Equal("00:00:03.0", row.ElapsedText);
        };
        model.SelectAllCommand.Execute(null); await model.ExecuteAsync(); Assert.Empty(model.Error);
        Assert.All(model.Rows, row => Assert.Equal(TimeSpan.FromSeconds(3), row.Elapsed));
        clock.Advance(TimeSpan.FromHours(1));
        await model.RefreshAsync();
        Assert.All(model.Rows, row => Assert.Equal(TimeSpan.FromSeconds(3), row.Elapsed));
        model.ClearSelectionCommand.Execute(null);
        var retry = model.Rows[0]; retry.IsSelected = true;
        retry.PropertyChanged += (_, change) => {
            if (change.PropertyName != nameof(retry.Status) || retry.Status != "Running") return;
            Assert.Equal(TimeSpan.Zero, retry.Elapsed); Assert.Equal(1, model.ExecutionProgress.Total);
            clock.Advance(TimeSpan.FromSeconds(1));
        };
        await model.ExecuteAsync(); Assert.Empty(model.Error);
        Assert.Equal(TimeSpan.FromSeconds(1), model.Rows.Single(row => row.Path == retry.Path).Elapsed);
        Assert.All(model.Rows.Where(row => row.Path != retry.Path), row => Assert.Equal(TimeSpan.FromSeconds(3), row.Elapsed));
        await model.SetFolderAsync(Path.Combine(temporary.FullName, "Another", "runParameters"));
        Assert.False(model.ExecutionProgress.HasWork);
        await model.SetFolderAsync(workspace.RunParametersFolder);
        Assert.All(model.Rows, row => { Assert.Equal("Complete", row.Status); Assert.Null(row.Elapsed); });
        workspace.Cancel();
    }

    [Fact]
    public async Task Grid_inspects_files_on_open_and_executes_only_selected_rows_with_inherited_bindings()
    {
        var workspace = await GenerateFiles();
        var folder = workspace.RunParametersFolder;
        var invalid = Path.Combine(folder, Guid.NewGuid() + ".msgpack"); File.WriteAllBytes(invalid, [1, 2, 3]);
        using var page = new ResultsExecutionPageViewModel(() => { }, _ => null);
        await page.SetFolderAsync(folder); Assert.Empty(page.Error);
        Assert.Equal(4, page.FileCount); Assert.Equal(0, page.SelectedCount);
        Assert.Equal(new[] { "MergeLibId", "SortableDataFormat" }, page.ParameterColumns);
        var broken = page.Rows.Single(row => row.State == outputState.Invalid);
        Assert.Equal(Path.GetFileName(invalid), broken.FileName); Assert.NotEmpty(broken.Error);
        foreach (var row in page.Rows.Where(row => row.CanExecute))
        {
            Assert.Equal(outputState.Pending, row.State);
            Assert.Equal(Path.GetFileName(row.Path), row.FileName);
            Assert.IsType<parameterValue.MergeLibIdValue>(row.Parameters!.Parameters.TryFind("MergeLibId").Value.Value);
            Assert.All(page.ParameterColumns, key => Assert.Equal(row.Parameters.Parameters.TryFind(key).Value.FormattedValue, row.Cells[Array.IndexOf(page.ParameterColumns, key)]));
        }
        page.SelectAllCommand.Execute(null); Assert.Equal(4, page.SelectedCount); Assert.False(page.CanExecute);
        page.ClearSelectionCommand.Execute(null);
        var chosen = page.Rows.Single(row => row.Parameters?.Parameters.TryFindText("SortableDataFormat")?.Value == "Bools");
        chosen.IsSelected = true;
        Assert.True(page.CanExecute); Assert.Contains(chosen.FileName, page.CommandLine);
        Assert.DoesNotContain("--parameters-folder", page.CommandLine);
        await page.ExecuteAsync(); Assert.Empty(page.Error); Assert.Single(page.LastReport!.Results);
        Assert.Equal(outputState.Complete, page.Rows.Single(row => row.Path == chosen.Path).State);
        Assert.Equal(2, page.Rows.Count(row => row.State == outputState.Pending));
        Assert.Single(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(folder)!, "data", "results", "SortableTests"), "*.msgpack"));
        Assert.Single(page.Rows, row => row.IsSelected);
        page.SelectPendingCommand.Execute(null); Assert.Equal(2, page.SelectedCount);
        await page.ExecuteAsync(); Assert.Empty(page.Error); Assert.Equal(2, page.LastReport.Results.Length);
        Assert.Equal(3, page.Rows.Count(row => row.State == outputState.Complete));
        page.SelectedRun = page.Runs.Single(row => row.Id == workspace.Selected!.Id);
        Assert.Equal(3, page.FileCount); Assert.Equal(0, page.SelectedCount);
        page.SelectAllCommand.Execute(null); await page.ExecuteAsync(); Assert.Empty(page.Error);
        Assert.All(page.LastReport.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        await page.SetFolderAsync(Path.Combine(temporary.FullName, "Missing", "runParameters"));
        Assert.Empty(page.Rows); Assert.Empty(page.ParameterColumns); Assert.False(page.CanExecute);
        workspace.Cancel();
    }

    [Fact]
    public async Task Live_rows_continue_after_failure_and_keep_copyable_errors_until_retry()
    {
        var workspace = await GenerateFiles();
        using var model = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
        var first = model.Rows[0]; var failing = model.Rows[1]; var unstarted = model.Rows[2];
        var item = failing.Parameters!;
        var planned = FileResultsHost.plan(item.ProjectId, item.Results, item.Parameters, item.SetupInputs).Values.Single();
        var blockedOutput = new SortopiaFileDb(Path.GetDirectoryName(workspace.RunParametersFolder)!, item.ProjectId).GetPath(planned.Key.Reference);
        Exception? failure = null;
        var thread = new Thread(() => {
            try
            {
                var view = new ResultsExecutionPage { DataContext = model };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(view.Dispatcher));
                void Layout() {
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    view.Measure(new Size(1100, 850)); view.Arrange(new Rect(0, 0, 1100, 850)); view.UpdateLayout();
                }
                void Wait(Task task) {
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!task.IsCompleted && DateTime.UtcNow < deadline) { Layout(); Thread.Sleep(1); }
                    Assert.True(task.IsCompleted); task.GetAwaiter().GetResult(); Layout();
                }
                var updates = new List<(string File, string Status)>();
                var uiThread = Environment.CurrentManagedThreadId;
                var timerPanel = (ResultsExecutionPanel)view.FindName("ExecutionPanel");
                timerPanel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var elapsedTicks = 0;
                foreach (var row in model.Rows) row.PropertyChanged += (_, change) => {
                    if (change.PropertyName == nameof(WorkItemStatusRow.ElapsedText) && row.Status == "Running")
                    { Assert.Equal(uiThread, Environment.CurrentManagedThreadId); elapsedTicks++; }
                    if (change.PropertyName != nameof(WorkItemStatusRow.Status)) return;
                    Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                    updates.Add((row.FileName, row.Status));
                    if (row == first && row.Status == "Running")
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(3);
                        while (elapsedTicks == 0 && DateTime.UtcNow < deadline) { Layout(); Thread.Sleep(1); }
                        Assert.True(elapsedTicks > 0, "Running elapsed time must refresh without another worker event.");
                        Assert.Contains("1 running", ((TextBlock)timerPanel.FindName("BatchProgressSummary")).Text);
                    }
                    if (row == failing && row.Status == "Running") Directory.CreateDirectory(blockedOutput);
                };
                Layout(); model.SelectAllCommand.Execute(null); Wait(model.ExecuteAsync());
                Assert.Equal(runExecutionStatus.Failed, model.LastReport!.Status); Assert.Equal(2, model.LastReport.Results.Length);
                Assert.Equal(0, model.ExecutionProgress.Running); Assert.Equal(3, model.ExecutionProgress.Finished);
                Assert.Equal(1, model.ExecutionProgress.Failed); Assert.Equal(100, model.ExecutionProgress.Percent);
                Assert.Equal(100, ((ProgressBar)timerPanel.FindName("BatchProgressBar")).Value);
                Assert.Equal(new[] { (first.FileName, "Running"), (first.FileName, "Complete"), (failing.FileName, "Running"), (failing.FileName, "Failed"), (unstarted.FileName, "Running"), (unstarted.FileName, "Complete") }, updates);
                Assert.Equal("Complete", model.Rows.Single(row => row.Path == first.Path).Status);
                Assert.Equal("Complete", model.Rows.Single(row => row.Path == unstarted.Path).Status);
                var failedRow = model.Rows.Single(row => row.Path == failing.Path);
                Assert.Equal("Failed", failedRow.Status); Assert.Contains(blockedOutput, failedRow.Error); Assert.True(failedRow.HasError);
                var elapsed = failedRow.Elapsed; Assert.NotNull(elapsed);
                Wait(model.RefreshAsync());
                failedRow = model.Rows.Single(row => row.Path == failing.Path);
                Assert.Equal("Failed", failedRow.Status); Assert.Contains(blockedOutput, failedRow.Error);
                Assert.Equal(elapsed, failedRow.Elapsed);
                model.ClearSelectionCommand.Execute(null); failedRow.IsSelected = true; Layout();
                var panel = (ResultsExecutionPanel)view.FindName("ExecutionPanel");
                var grid = (DataGrid)panel.FindName("RunParametersGrid");
                var jobs = (TextBox)panel.FindName("ParallelJobsInput");
                jobs.Text = "0"; Layout(); Assert.False(model.CanExecute); Assert.NotEmpty(model.ParallelJobsError);
                jobs.Text = "2"; Layout(); Assert.Equal("2", model.ParallelJobs); Assert.Empty(model.ParallelJobsError);
                grid.ScrollIntoView(failedRow); Layout();
                var container = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(failedRow);
                var details = Descendants(container).OfType<Expander>().Single(expander => Equals(expander.Header, "Error details"));
                Assert.Equal(Visibility.Visible, details.Visibility); details.IsExpanded = true; Layout();
                var error = Descendants(details).OfType<TextBox>().Single();
                Assert.True(error.IsReadOnly); Assert.Equal(failedRow.Error, error.Text);
                Assert.InRange(error.ActualWidth, 200, grid.ActualWidth - 24);
                Assert.InRange(error.ActualHeight, 40, 160); // Long errors wrap within the viewport, rather than disappearing off-screen.
                if (Environment.GetEnvironmentVariable("SORTOPIA_RESULTS_FAILURE_SCREENSHOT") is { Length: > 0 } path) {
                    var bitmap = new RenderTargetBitmap(1100, 850, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(path); encoder.Save(stream);
                }
                Directory.Delete(blockedOutput); Wait(model.RefreshAsync());
                Wait(model.ExecuteAsync()); Assert.Empty(model.Error);
                var repaired = model.Rows.Single(row => row.Path == failing.Path);
                Assert.Equal("Complete", repaired.Status); Assert.False(repaired.HasError);
                Assert.Equal("Complete", model.Rows.Single(row => row.Path == unstarted.Path).Status);
                timerPanel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        workspace.Cancel();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Fact]
    public async Task Missing_selected_file_keeps_a_failed_row_and_error_even_after_refresh()
    {
        var workspace = await GenerateFiles();
        using var model = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
        var missing = model.Rows[1]; var bytes = File.ReadAllBytes(missing.Path); File.Delete(missing.Path);
        model.SelectAllCommand.Execute(null); await model.ExecuteAsync();
        Assert.Equal(runExecutionStatus.Failed, model.LastReport!.Status); Assert.Empty(model.LastReport.Results);
        await model.RefreshAsync();
        Assert.Equal(3, model.Rows.Count);
        var failed = model.Rows.Single(row => row.Path == missing.Path);
        Assert.Equal("Failed", failed.Status); Assert.Contains(missing.FileName, failed.Error); Assert.False(failed.CanExecute);
        Assert.Null(failed.Elapsed);
        Assert.Equal(1, model.ExecutionProgress.Finished); Assert.Equal(2, model.ExecutionProgress.NotStarted);
        Assert.All(model.Rows.Where(row => row != failed), row => Assert.Equal("Pending", row.Status));
        File.WriteAllBytes(missing.Path, bytes); await model.RefreshAsync();
        model.ClearSelectionCommand.Execute(null); model.Rows.Single(row => row.Path == missing.Path).IsSelected = true;
        await model.ExecuteAsync(); Assert.Empty(model.Error);
        Assert.Equal("Complete", model.Rows.Single(row => row.Path == missing.Path).Status);
        Assert.Empty(model.Rows.Single(row => row.Path == missing.Path).Error);
        workspace.Cancel();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelling_a_running_row_never_leaves_it_running_or_changes_a_new_folder(bool navigate)
    {
        var workspace = await GenerateFiles();
        using var model = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
        var row = model.Rows[0]; Task navigation = Task.CompletedTask;
        row.PropertyChanged += (_, change) => {
            if (change.PropertyName != nameof(WorkItemStatusRow.Status) || row.Status != "Running") return;
            if (navigate) navigation = model.SetFolderAsync(Path.Combine(temporary.FullName, "Another", "runParameters"));
            else model.Cancel();
        };
        model.SelectAllCommand.Execute(null); await model.ExecuteAsync(); await navigation;
        if (navigate) {
            Assert.Empty(model.Rows); Assert.Null(model.LastReport); Assert.Empty(model.Error);
            Assert.False(model.ExecutionProgress.HasWork); Assert.Equal(0, model.ExecutionProgress.Running);
        }
        else {
            Assert.Equal(runExecutionStatus.Cancelled, model.LastReport!.Status); Assert.Equal("Cancelled", row.Status);
            Assert.All(model.Rows.Skip(1), other => Assert.Equal("Pending", other.Status));
            Assert.DoesNotContain(model.Rows, other => other.Status is "Running" or "Failed");
            Assert.Equal(0, model.ExecutionProgress.Running); Assert.Equal(1, model.ExecutionProgress.Cancelled);
            Assert.Equal(1, model.ExecutionProgress.Finished); Assert.Equal(2, model.ExecutionProgress.NotStarted);
            var elapsed = row.Elapsed; Assert.NotNull(elapsed);
            await model.RefreshAsync(); Assert.Equal("Cancelled", model.Rows.Single(other => other.Path == row.Path).Status);
            Assert.Equal(elapsed, model.Rows.Single(other => other.Path == row.Path).Elapsed);
            model.SelectAllCommand.Execute(null); await model.ExecuteAsync(); Assert.Empty(model.Error);
            Assert.All(model.Rows, other => Assert.Equal("Complete", other.Status));
        }
        workspace.Cancel();
    }

    [Fact]
    public async Task Execute_page_navigation_opens_the_selected_run_folder_and_preserves_workspace_drafts()
    {
        var generated = await GenerateFiles(); var runId = generated.Selected!.Id; var folder = generated.RunParametersFolder;
        using var shell = new MainWindowViewModel(_ => null, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
        Assert.IsType<HomePageViewModel>(shell.CurrentPage).OpenWorkspaceCommand.Execute(null);
        var workspace = Assert.IsType<WorkspacePageViewModel>(shell.CurrentPage); WorkspaceRunExecutionTests.SelectRun(workspace);
        workspace.WorkspaceName = "Unsaved title";
        Assert.True(workspace.ExecutePageCommand.CanExecute(null)); Assert.False(workspace.GenerateRunParametersCommand.CanExecute(null));
        await workspace.OpenExecutePageAsync();
        var execution = Assert.IsType<ResultsExecutionPageViewModel>(shell.CurrentPage);
        Assert.Equal(folder, execution.Folder); Assert.Equal(runId, execution.SelectedRun!.Id);
        Assert.Equal(3, execution.Rows.Count); Assert.All(execution.Rows, row => Assert.Equal(outputState.Pending, row.State));
        Assert.Contains("workspace", execution.ReturnLabel);
        execution.ReturnCommand.Execute(null); Assert.Same(workspace, shell.CurrentPage);
        Assert.True(workspace.HasUnsavedChanges); Assert.Equal("Unsaved title", workspace.WorkspaceName);
        generated.Cancel();
    }

    [Fact]
    public async Task Wpf_grid_has_executor_binding_columns_and_native_multiple_row_selection()
    {
        var workspace = await GenerateFiles(8);
        using var model = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
        Exception? failure = null;
        var thread = new Thread(() => {
            try
            {
                var view = new ResultsExecutionPage { DataContext = model };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(view.Dispatcher));
                void Layout()
                {
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    view.Measure(new Size(1100, 760)); view.Arrange(new Rect(0, 0, 1100, 760)); view.UpdateLayout();
                }
                Layout();
                var panel = (ResultsExecutionPanel)view.FindName("ExecutionPanel");
                var grid = (DataGrid)panel.FindName("RunParametersGrid");
                Assert.Equal(new[] { "File name", "Status", "Elapsed", "Outputs", "Executor", "MergeLibId", "SortableDataFormat" }, grid.Columns.Select(column => column.Header));
                Assert.Equal(3, grid.FrozenColumnCount);
                Assert.Equal(24, grid.Items.Count); Assert.True(grid.IsReadOnly); Assert.True(grid.EnableRowVirtualization);
                Assert.Null(grid.ItemContainerGenerator.ContainerFromItem(model.Rows[^1]));
                grid.SelectedItems.Add(model.Rows[0]); grid.SelectedItems.Add(model.Rows[^1]); Layout();
                Assert.Equal(2, model.SelectedCount); Assert.False(model.Rows[1].IsSelected);
                Assert.True(((Button)panel.FindName("ExecuteSelectedButton")).IsEnabled);
                var selectedPaths = grid.SelectedItems.Cast<WorkItemStatusRow>().Select(row => row.Path).Order().ToArray();
                var refresh = model.RefreshAsync();
                var timeout = DateTime.UtcNow.AddSeconds(10);
                while (!refresh.IsCompleted && DateTime.UtcNow < timeout) { Layout(); Thread.Sleep(1); }
                Assert.True(refresh.IsCompleted); refresh.GetAwaiter().GetResult(); Layout(); Assert.Empty(model.Error);
                Assert.Equal(selectedPaths, grid.SelectedItems.Cast<WorkItemStatusRow>().Select(row => row.Path).Order());
                grid.SelectAll(); Assert.Equal(24, model.SelectedCount);
                model.ClearSelectionCommand.Execute(null); Layout(); Assert.Empty(grid.SelectedItems);
                Assert.False(model.CanExecute);
                model.SelectAllCommand.Execute(null); Layout(); Assert.Equal(24, grid.SelectedItems.Count);
                grid.UnselectAll(); Layout(); Assert.Equal(0, model.SelectedCount);
                Assert.InRange(grid.ActualHeight, 140, 700); // Constrained viewport keeps the grid virtualized.
                if (Environment.GetEnvironmentVariable("SORTOPIA_RESULTS_SCREENSHOT") is { Length: > 0 } path)
                {
                    grid.SelectedItems.Add(model.Rows[0]); grid.SelectedItems.Add(model.Rows[2]); Layout();
                    var bitmap = new RenderTargetBitmap(1100, 760, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(path); encoder.Save(stream);
                }
            } catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        workspace.Cancel();
    }

    [Fact]
    public async Task Standalone_page_uses_folder_files_and_never_falls_back_to_other_runs()
    {
        var workspace = WorkspaceRunExecutionTests.Create(temporary.FullName, false);
        WorkspaceRunExecutionTests.SupplyQuerySpans(workspace);
        await workspace.SaveWorkspaceAsync(); WorkspaceRunExecutionTests.SelectRun(workspace);
        await workspace.GenerateRunParametersAsync();
        Assert.Empty(workspace.Error);
        var folder = workspace.RunParametersFolder;
        using var page = new ResultsExecutionPageViewModel(() => { }, _ => null);
        await page.SetFolderAsync(folder, Guid.NewGuid());
        Assert.Equal(0, page.FileCount);
        Assert.False(page.ExecuteCommand.CanExecute(null));
        await page.SetFolderAsync(folder);
        Assert.Equal(1, page.FileCount);
        Assert.Null(page.SelectedRun!.Id);
        Assert.Equal(outputState.Pending, Assert.Single(page.Rows).State);
        Assert.False(page.CanExecute);
        page.SelectAllCommand.Execute(null);
        await page.ExecuteAsync();
        Assert.Empty(page.Error);
        Assert.Equal(runExecutionStatus.Completed, page.LastReport!.Status);
        Assert.Equal(outputState.Complete, Assert.Single(page.Rows).State);
        var project = Path.GetDirectoryName(folder)!;
        Assert.False(Directory.Exists(Path.Combine(project, "preparations")));
        var dataFile = Directory.GetFiles(Path.Combine(project, "data", "results", "SortableTests"), "*.msgpack").Single();
        using var inspector = new FileInspectorPageViewModel(() => { }, _ => null);
        await inspector.OpenRootAsync(project);
        await inspector.InspectAsync(new InspectorFile(WorkspaceFileInspection.entry(dataFile)));
        Assert.Equal("Results data", inspector.Kind);
        Assert.Empty(inspector.Error);

        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = new ResultsExecutionPage { DataContext = page };
                view.Measure(new Size(1040, 780)); view.Arrange(new Rect(0, 0, 1040, 780)); view.UpdateLayout();
                if (Environment.GetEnvironmentVariable("SORTOPIA_RESULTS_SCREENSHOT") is { Length: > 0 } path)
                {
                    var bitmap = new RenderTargetBitmap(1040, 780, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(path); encoder.Save(file);
                }
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        workspace.Cancel();
    }
    public void Dispose() => temporary.Delete(true);
}
