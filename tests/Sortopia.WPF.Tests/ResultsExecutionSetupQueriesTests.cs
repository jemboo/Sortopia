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

    private async Task<WorkspacePageViewModel> Generate()
    {
        var root = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        page.AddBranchModuleCommand.Execute(null);
        var module = page.Selected!.Editor!;
        module.SetupFunctionName = SetupFunctionNames.evaluationStandardMssiSimple;
        module.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        module.AddRequiredSetupDefaultsCommand.Execute(null); module.AddRequiredResultsDefaultsCommand.Execute(null);
        module.SetupParameterDefaults.Single(row => row.Key == "SorterCount").Value.Text = "1";
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "Repl", true, "2", "10");
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "SorterEvalType", false, "V1", "V2");
        project.Editor!.SetupQueryOutputs.Single(row => row.Kind == "SorterModelSet").Keys.Single(key => key.Key == "Repl").Selected = true;
        Assert.Single(Assert.Single(project.Editor.ResultsQueryOutputs).Keys).Selected = true;
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error); page.Selected = run;
        await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
        Assert.Equal(4, page.SelectedExecution!.Files.Rows.Count);
        return page;
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
            await model.ExportReportsAsync(destination); Assert.Contains("not overwritten", model.Error);
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
                    Assert.Equal(3, grid.FrozenColumnCount); Assert.True(grid.IsReadOnly);
                    workspace.SelectConfigurationItem("execution");
                    var workspaceView = new Sortopia.WPF.Views.Pages.Workspace.WorkspacePage { DataContext = workspace };
                    void WorkspaceLayout() { workspaceView.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); workspaceView.Measure(new Size(1400, 1000)); workspaceView.Arrange(new Rect(0, 0, 1400, 1000)); workspaceView.UpdateLayout(); }
                    WorkspaceLayout();
                    var files = (DataGrid)workspaceView.FindName("RunParameterFilesTable");
                    var setupColumn = files.Columns.Single(column => Equals(column.Header, "Setup · Repl"));
                    Assert.Equal(4, files.Items.Count);
                    CheckSorting(files, setupColumn, WorkspaceLayout);
                }
                catch (Exception error) { failure = error; }
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
                item.SetNumber, item.SetupSetNumber, item.SetupSuffixStages, item.ResultsSuffixStages);
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
