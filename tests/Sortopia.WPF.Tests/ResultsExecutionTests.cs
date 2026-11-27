using System.Diagnostics;
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
        page.Selected!.Parent!.Editor!.ResultsQueryOutputs.Single().Keys.Single().Selected = true;
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error); WorkspaceRunExecutionTests.SelectRun(page);
        await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
        return page;
    }

    [Fact]
    public async Task Command_line_button_launches_the_displayed_selection_independently_and_reports_launch_errors()
    {
        var workspace = await GenerateFiles();
        var invalid = Path.Combine(workspace.RunParametersFolder, Guid.NewGuid() + ".msgpack"); File.WriteAllBytes(invalid, [1, 2, 3]);
        var launches = new List<(string Command, string Folder)>();
        var fail = false;
        using var model = new ResultsExecutionPageViewModel(() => { }, _ => null, launchTerminal: (command, folder) =>
        {
            if (fail) throw new IOException("Terminal unavailable");
            launches.Add((command, folder));
        });
        Assert.False(model.ExecuteCommandLineCommand.CanExecute(null));
        await model.SetFolderAsync(workspace.RunParametersFolder);
        model.SelectAllCommand.Execute(null);
        Assert.False(model.ExecuteCommandLineCommand.CanExecute(null));
        model.ExecuteCommandLineCommand.Execute(null); Assert.Empty(launches);
        model.ClearSelectionCommand.Execute(null);
        foreach (var row in model.Rows.Where(row => row.CanExecute).Take(2)) row.IsSelected = true;
        Assert.True(model.ExecuteCommandLineCommand.CanExecute(null));
        var displayed = model.CommandLine;
        model.ExecuteCommandLineCommand.Execute(null);
        Assert.Equal((displayed, workspace.RunParametersFolder), Assert.Single(launches));
        Assert.Empty(model.Error); Assert.Contains("separate terminal", model.Status);
        Assert.False(model.IsBusy);
        Assert.All(model.Rows.Where(row => row.CanExecute), row => Assert.Equal(outputState.Pending, row.State));
        model.ClearSelectionCommand.Execute(null);
        model.ExecuteCommandLineCommand.Execute(null); Assert.Single(launches);
        model.Rows.First(row => row.CanExecute).IsSelected = true;
        fail = true;
        model.ExecuteCommandLineCommand.Execute(null);
        Assert.Contains("Terminal unavailable", model.Error); Assert.False(model.IsBusy);
        workspace.Cancel();
    }

    [Fact]
    public async Task Terminal_handoff_runs_the_displayed_command_and_progress_changes_only_on_request()
    {
        var workspace = await GenerateFiles();
        Process? terminal = null;
        using var model = new ResultsExecutionPageViewModel(() => { }, _ => null, launchTerminal: (command, folder) =>
            ResultsCommandTerminal.Launch(command, folder, start =>
            {
                Assert.True(start.UseShellExecute);
                Assert.Equal(ProcessWindowStyle.Normal, start.WindowStyle);
                Assert.Contains("-NoExit", start.ArgumentList);
                // Run the actual PowerShell bootstrap and CLI without opening a test window.
                start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden;
                start.ArgumentList.Remove("-NoExit");
                start.RedirectStandardOutput = true; start.RedirectStandardError = true;
                terminal = Process.Start(start);
            }));
        try
        {
            await model.SetFolderAsync(workspace.RunParametersFolder);
            model.CpuPercent = "50"; model.MemoryLimit = "0.5GiB"; model.ParallelFiles = "2";
            var selected = model.Rows.Take(2).ToArray();
            foreach (var row in selected) row.IsSelected = true;
            model.ExecuteCommandLineCommand.Execute(null);
            Assert.Empty(model.Error); Assert.NotNull(terminal); Assert.False(model.IsBusy);
            var output = terminal.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var errors = terminal.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            model.ClearSelectionCommand.Execute(null); model.Cancel();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await terminal.WaitForExitAsync(timeout.Token);
            var terminalOutput = await output + await errors;
            Assert.True(terminal.ExitCode == 0, terminalOutput);
            Assert.Contains("Batch limits: 50% CPU, 0.50 GiB", terminalOutput);
            Assert.Contains("2 results work items processed", terminalOutput);
            Assert.All(model.Rows, row => Assert.Equal(outputState.Pending, row.State));
            await model.UpdateProgressAsync();
            Assert.Empty(model.Error);
            Assert.All(selected, row => Assert.True(model.Rows.Single(current => current.Path == row.Path).State == outputState.Complete, terminalOutput));
            Assert.All(model.Rows.Except(selected), row => Assert.Equal(outputState.Pending, row.State));
            Assert.Equal(0, model.SelectedCount);
        }
        finally
        {
            if (terminal is not null)
            {
                if (!terminal.HasExited) { terminal.Kill(entireProcessTree: true); await terminal.WaitForExitAsync(CancellationToken.None); }
                terminal.Dispose();
            }
            workspace.Cancel();
        }
    }

    [Fact]
    public async Task Quick_status_checks_integrity_and_full_validation_checks_payload_contents_before_execution()
    {
        var workspace = await GenerateFiles();
        using var page = await WorkspaceRunExecutionTests.OpenExecutionAsync(workspace);
        page.SelectAllCommand.Execute(null);
        await WorkspaceRunExecutionTests.ExecuteCheckedFilesAsync(page); Assert.Empty(page.Error);
        var row = page.Rows[0];
        var item = row.Parameters!;
        var key = FileResultsHost.plan(item.ProjectId, item.Results, item.Parameters, item.SetupInputs).Values.Single().Key;
        var file = new SortopiaFileDb(Path.GetDirectoryName(workspace.RunParametersFolder)!, item.ProjectId).GetPath(key.Reference);
        var envelope = Sortopia.Core.Serialization.MessagePackSetup.deserialize<dataEnvelopeDto>(File.ReadAllBytes(file));
        byte[] malformed = [1, 2, 3];
        var damaged = new dataEnvelopeDto(envelope.Version, envelope.ProjectId, envelope.Id, envelope.QueryId,
            envelope.IsSetup, envelope.Kind, envelope.Label, envelope.QueryBytes, envelope.IdentityBytes,
            envelope.Compression, envelope.RawLength, System.Security.Cryptography.SHA256.HashData(malformed), malformed);
        File.WriteAllBytes(file, Sortopia.Core.Serialization.MessagePackSetup.serialize(damaged));
        await page.RefreshAsync();
        Assert.Equal("Complete", page.Rows.Single(value => value.Path == row.Path).Status);
        Assert.Contains("File integrity checked", page.Status);
        Assert.True(page.ValidateFullyCommand.CanExecute(null));
        Assert.Equal(runExecutionStatus.Failed, RunParameterExecution.executeFile(row.Path, null!, CancellationToken.None).Status);
        await page.ValidateFullyAsync();
        Assert.Equal("Invalid", page.Rows.Single(value => value.Path == row.Path).Status);
        Assert.Contains("Full validation finished", page.Status);
        await page.RefreshAsync();
        Assert.Equal("Invalid", page.Rows.Single(value => value.Path == row.Path).Status);
        workspace.Cancel();
    }

    [Fact]
    public async Task Parameter_rows_are_visible_before_output_inspection_and_update_in_place()
    {
        var workspace = await GenerateFiles();
        using var page = new ResultsExecutionPageViewModel(() => { }, _ => null);
        WorkItemStatusRow[]? published = null;
        var remaining = new List<int>();
        page.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(page.Rows) || page.Rows.Count == 0) return;
            Assert.Null(published);
            published = page.Rows.ToArray();
            Assert.True(page.IsBusy);
            Assert.All(published, row =>
            {
                Assert.Equal("Checking", row.Status);
                Assert.Equal("Checking outputs…", row.OutputSummary);
                Assert.False(row.CanExecute);
                Assert.NotNull(row.Parameters);
                row.PropertyChanged += (_, property) =>
                {
                    if (property.PropertyName == nameof(row.Status) && !row.IsChecking)
                        remaining.Add(page.Rows.Count(other => other.IsChecking));
                };
            });
        };
        await page.SetFolderAsync(workspace.RunParametersFolder, workspace.Selected!.Id);
        Assert.Empty(page.Error);
        Assert.NotNull(published);
        Assert.Equal(3, page.FileCount);
        Assert.Equal(new[] { 2, 1, 0 }, remaining);
        Assert.All(published, row =>
        {
            Assert.Contains(row, page.Rows);
            Assert.Equal("Pending", row.Status);
            Assert.False(row.IsChecking);
            Assert.True(row.CanExecute);
        });
        Assert.DoesNotContain("Checking", page.Status);
        workspace.Cancel();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_inspection_keeps_rows_visible_and_cannot_overwrite_a_new_folder(bool navigate)
    {
        var workspace = await GenerateFiles();
        using var page = new ResultsExecutionPageViewModel(() => { }, _ => null);
        WorkItemStatusRow[]? published = null;
        Task navigation = Task.CompletedTask;
        var interrupted = false;
        WorkItemStatusRow? firstChecked = null;
        var otherFolder = Path.Combine(temporary.FullName, "Other", "runParameters");
        page.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(page.Rows) || page.Rows.Count == 0) return;
            published = page.Rows.ToArray();
            foreach (var row in published) row.PropertyChanged += (_, property) =>
            {
                if (interrupted || property.PropertyName != nameof(WorkItemStatusRow.Status) || row.IsChecking) return;
                interrupted = true;
                firstChecked = row;
                if (navigate) navigation = page.SetFolderAsync(otherFolder);
                else page.Cancel();
            };
        };
        await page.SetFolderAsync(workspace.RunParametersFolder);
        await navigation;
        Assert.Empty(page.Error);
        Assert.True(interrupted);
        Assert.NotNull(published);
        Assert.NotNull(firstChecked);
        Assert.Equal("Pending", firstChecked.Status);
        Assert.All(published.Where(row => !ReferenceEquals(row, firstChecked)), row =>
        {
            Assert.Equal("Not checked", row.Status);
            Assert.False(row.CanExecute);
        });
        if (navigate)
        {
            Assert.Equal(otherFolder, page.Folder);
            Assert.Empty(page.Rows);
            Assert.Contains("No runParameters folder", page.Status);
        }
        else
        {
            Assert.Equal(3, page.FileCount);
            Assert.Contains("cancelled", page.Status);
            await page.RefreshAsync();
            Assert.All(page.Rows, row => Assert.Equal("Pending", row.Status));
        }
        workspace.Cancel();
    }

    [Fact]
    public async Task Grid_inspects_files_and_preserves_selection_and_inherited_bindings_after_outputs_are_written()
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
        page.SelectAllCommand.Execute(null); Assert.Equal(4, page.SelectedCount); Assert.False(page.CanExecuteCommandLine);
        page.ClearSelectionCommand.Execute(null);
        var chosen = page.Rows.Single(row => row.Parameters?.Parameters.TryFindText("SortableDataFormat")?.Value == "Bools");
        chosen.IsSelected = true;
        Assert.True(page.CanExecuteCommandLine); Assert.Contains(chosen.FileName, page.CommandLine);
        Assert.DoesNotContain("--parameters-folder", page.CommandLine);
        var report = await WorkspaceRunExecutionTests.ExecuteCheckedFilesAsync(page); Assert.Empty(page.Error); Assert.Single(report.Results);
        Assert.Equal(outputState.Complete, page.Rows.Single(row => row.Path == chosen.Path).State);
        Assert.Equal(2, page.Rows.Count(row => row.State == outputState.Pending));
        Assert.Single(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(folder)!, "data", "results", "SortableTests"), "*.msgpack"));
        Assert.Single(page.Rows, row => row.IsSelected);
        page.SelectPendingCommand.Execute(null); Assert.Equal(2, page.SelectedCount);
        report = await WorkspaceRunExecutionTests.ExecuteCheckedFilesAsync(page); Assert.Empty(page.Error); Assert.Equal(2, report.Results.Length);
        Assert.Equal(3, page.Rows.Count(row => row.State == outputState.Complete));
        page.SelectedRun = page.Runs.Single(row => row.Id == workspace.Selected!.Id);
        Assert.Equal(3, page.FileCount); Assert.Equal(0, page.SelectedCount);
        page.SelectAllCommand.Execute(null); report = await WorkspaceRunExecutionTests.ExecuteCheckedFilesAsync(page); Assert.Empty(page.Error);
        Assert.All(report.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        await page.SetFolderAsync(Path.Combine(temporary.FullName, "Missing", "runParameters"));
        Assert.Empty(page.Rows); Assert.Empty(page.ParameterColumns); Assert.False(page.CanExecuteCommandLine);
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
        Assert.True(workspace.ExecutePageCommand.CanExecute(null)); Assert.True(workspace.GenerateRunParametersCommand.CanExecute(null));
        await workspace.OpenExecutePageAsync();
        var execution = Assert.IsType<ResultsExecutionPageViewModel>(shell.CurrentPage);
        Assert.Equal(folder, execution.Folder); Assert.Equal(runId, execution.SelectedRun!.Id);
        Assert.Equal(3, execution.Rows.Count); Assert.All(execution.Rows, row => Assert.Equal(outputState.Pending, row.State));
        Assert.Contains("workspace", execution.ReturnLabel);
        Assert.Null(execution.SelectedFile);
        Assert.True(execution.OpenRunCommand.CanExecute(null));
        execution.OpenRunCommand.Execute(null); Assert.Same(workspace, shell.CurrentPage);
        Assert.Equal(runId, workspace.Selected!.Id);
        Assert.True(workspace.HasUnsavedChanges); Assert.Equal("Unsaved title", workspace.WorkspaceName);
        generated.Cancel();
    }

    [Fact]
    public async Task Wpf_grid_checks_execution_independently_of_row_selection_and_keeps_scrolling()
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
                var terminalButton = (Button)panel.FindName("ExecuteCommandLineButton");
                Assert.Same(model.ExecuteCommandLineCommand, terminalButton.Command);
                Assert.Same(model.ExecuteCommandLineCommand, ((Button)panel.FindName("ExecuteSelectedButton")).Command);
                Assert.Same(model.UpdateProgressCommand, ((Button)panel.FindName("UpdateProgressButton")).Command);
                Assert.Same(model.OpenEvolutionHistoryCommand, ((Button)panel.FindName("OpenEvolutionHistoryButton")).Command);
                Assert.Same(model.OpenRunCommand, ((Button)panel.FindName("OpenRunButton")).Command);
                Assert.False(terminalButton.IsEnabled);
                var grid = (DataGrid)panel.FindName("RunParametersGrid");
                Assert.Equal(new[] { "Details", "Run", "File name", "Status", "Generations", "Outputs", "Executor", "MergeLibId", "SortableDataFormat" }, grid.Columns.Select(column => column.Header));
                Assert.Equal(5, grid.FrozenColumnCount);
                Assert.Equal(24, grid.Items.Count); Assert.True(grid.IsReadOnly); Assert.True(grid.EnableRowVirtualization);
                Assert.Null(grid.ItemContainerGenerator.ContainerFromItem(model.Rows[^1]));
                grid.SelectedItems.Add(model.Rows[0]); grid.SelectedItems.Add(model.Rows[^1]); Layout();
                Assert.Equal(0, model.SelectedCount);
                Assert.False(model.Rows[0].IsExpanded);
                CheckBox Check(WorkItemStatusRow row)
                {
                    grid.ScrollIntoView(row); Layout();
                    var container = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(row));
                    Assert.Equal(Visibility.Collapsed, container.DetailsVisibility);
                    return Descendants(grid.Columns[1].GetCellContent(container)).OfType<CheckBox>().Single();
                }
                Check(model.Rows[0]).SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
                var first = model.Rows[0];
                Assert.False(first.HasSetupInputs); Assert.False(first.HasError);
                var firstContainer = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(first);
                var expander = Descendants(grid.Columns[0].GetCellContent(firstContainer)).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single();
                Assert.Equal(Visibility.Visible, expander.Visibility);
                var toggle = (System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(expander)
                    .GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)!;
                toggle.Toggle(); Layout();
                Assert.True(first.IsExpanded); Assert.Equal(Visibility.Visible, firstContainer.DetailsVisibility);
                var details = Descendants(firstContainer).OfType<TextBox>()
                    .Single(text => System.Windows.Automation.AutomationProperties.GetName(text) == "Run parameter file details");
                Assert.True(details.IsReadOnly); Assert.Contains(first.Path, details.Text); Assert.Contains("SortableDataFormat =", details.Text);
                Assert.Equal(1, model.SelectedCount);
                toggle.Toggle(); Layout();
                Assert.False(first.IsExpanded); Assert.Equal(Visibility.Collapsed, firstContainer.DetailsVisibility);
                model.Rows[^1].IsSelected = true;
                Layout(); Assert.Equal(2, model.SelectedCount); Assert.False(model.Rows[1].IsSelected);
                Assert.True(((Button)panel.FindName("ExecuteSelectedButton")).IsEnabled);
                Assert.True(terminalButton.IsEnabled);
                var selectedPaths = model.Rows.Where(row => row.IsSelected).Select(row => row.Path).Order().ToArray();
                var refresh = model.RefreshAsync();
                var timeout = DateTime.UtcNow.AddSeconds(10);
                while (!refresh.IsCompleted && DateTime.UtcNow < timeout) { Layout(); Thread.Sleep(1); }
                Assert.True(refresh.IsCompleted); refresh.GetAwaiter().GetResult(); Layout(); Assert.Empty(model.Error);
                Assert.Equal(selectedPaths, model.Rows.Where(row => row.IsSelected).Select(row => row.Path).Order());
                grid.SelectAll(); Assert.Equal(2, model.SelectedCount);
                model.ClearSelectionCommand.Execute(null); Layout(); Assert.Equal(0, model.SelectedCount);
                Assert.False(model.CanExecuteCommandLine);
                model.SelectAllCommand.Execute(null); Layout(); Assert.Equal(24, model.SelectedCount);
                grid.UnselectAll(); Layout(); Assert.Equal(24, model.SelectedCount);
                model.ClearSelectionCommand.Execute(null);
                model.Rows[0].IsSelected = true;
                Assert.InRange(grid.ActualHeight, 140, 700); // Constrained viewport keeps the grid virtualized.
                if (Environment.GetEnvironmentVariable("SORTOPIA_RESULTS_SCREENSHOT") is { Length: > 0 } path)
                {
                    model.Rows[0].IsSelected = model.Rows[2].IsSelected = true;
                    model.Rows[0].IsExpanded = true;
                    grid.ScrollIntoView(model.Rows[0]); Layout();
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
        Assert.Contains("folder contains 1 files", page.Status);
        Assert.Contains("choose All runs", page.Status);
        Assert.False(page.ExecuteCommandLineCommand.CanExecute(null));
        await page.SetFolderAsync(folder);
        Assert.Equal(1, page.FileCount);
        Assert.Null(page.SelectedRun!.Id);
        Assert.Equal(outputState.Pending, Assert.Single(page.Rows).State);
        Assert.False(page.CanExecuteCommandLine);
        page.SelectAllCommand.Execute(null);
        var report = await WorkspaceRunExecutionTests.ExecuteCheckedFilesAsync(page);
        Assert.Empty(page.Error);
        Assert.Equal(runExecutionStatus.Completed, report.Status);
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
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    public void Dispose() => temporary.Delete(true);
}
