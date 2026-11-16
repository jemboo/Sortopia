using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Sortopia.Runs;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.ViewModels.Shared;
using Sortopia.WPF.Views.Shared;
using Xunit;

namespace Sortopia.WPF.Tests;

[Collection("Workspace editor rendering")]
public sealed class SetupQueryColumnsTests
{
    [Fact]
    public void Float_values_sort_numerically_and_duplicate_keys_keep_their_input_identity()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var first = sortopiaDataType.NewSorterModelSet("first");
                var second = sortopiaDataType.NewSorterModelSet("second");
                WorkspaceRunParameterFile Row(double number)
                {
                    var queries = new SetupQueryData();
                    queries.Inputs[first] = [ParameterBinding.fromValue("OrthoRate", parameterValue.NewFloatingValue(number)),
                        ParameterBinding.fromValue("SelfSymRate", parameterValue.NewFloatingValue(0.25))];
                    queries.Inputs[second] = [ParameterBinding.fromValue("OrthoRate", parameterValue.NewFloatingValue(20 - number))];
                    return new("unused", null, "", "Ready", "Pending", "") { SetupQueries = queries };
                }
                var missing = new SetupQueryData(); missing.Inputs[first] = null;
                WorkspaceRunParameterFile[] rows = [Row(10), Row(2), Row(1e-5), Row(0.5),
                    new("root", null, "", "Ready", "Pending", ""),
                    new("missing", null, "", "Ready", "Pending", "") { SetupQueries = missing }];
                var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false };
                SetupQueryColumns.SetRows(grid, rows);
                Assert.Equal(new[] { "Setup · SorterModelSet · first · OrthoRate", "Setup · SorterModelSet · second · OrthoRate", "Setup · SelfSymRate" },
                    grid.Columns.Select(column => column.Header));
                Assert.Equal("10", rows[0].SetupQueries.Cells[0].Text);
                Assert.Equal("18", rows[1].SetupQueries.Cells[1].Text);
                Assert.Equal("—", rows[^2].SetupQueries.Cells[0].Text);
                Assert.Equal("Unavailable", rows[^1].SetupQueries.Cells[0].Text);
                foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
                {
                    grid.Items.SortDescriptions.Clear();
                    grid.Items.SortDescriptions.Add(new(grid.Columns[0].SortMemberPath, direction));
                    var values = grid.Items.Cast<ISetupQueryRow>().Select(row => row.SetupQueries.Cells[0]).Where(cell => cell.SortValue is not null).Select(cell => cell.Text);
                    var expected = new[] { "1E-05", "0.5", "2", "10" };
                    Assert.Equal(direction == ListSortDirection.Ascending ? expected : expected.Reverse(), values);
                }
                // Replacing the visible run must remove columns and stale sort paths.
                grid.ItemsSource = new[] { rows[^2] }; SetupQueryColumns.SetRows(grid, grid.ItemsSource);
                Assert.Empty(grid.Columns); Assert.Empty(grid.Items.SortDescriptions);
                grid.ItemsSource = rows; SetupQueryColumns.SetRows(grid, rows);
                Assert.Equal(3, grid.Columns.Count);
                Assert.Equal("SetupQueries.Cells[0].Text", ((Binding)((DataGridTextColumn)grid.Columns[0]).Binding).Path.Path);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
