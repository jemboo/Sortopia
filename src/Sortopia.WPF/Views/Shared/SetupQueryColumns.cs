using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Sortopia.WPF.ViewModels.Shared;

namespace Sortopia.WPF.Views.Shared;

/// Shared column generation for saved run files and results execution.
public static class SetupQueryColumns
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.RegisterAttached(
        "Rows", typeof(IEnumerable), typeof(SetupQueryColumns), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty InsertIndexProperty = DependencyProperty.RegisterAttached(
        "InsertIndex", typeof(int), typeof(SetupQueryColumns), new PropertyMetadata(0, Changed));
    public static IEnumerable? GetRows(DependencyObject target) => (IEnumerable?)target.GetValue(RowsProperty);
    public static void SetRows(DependencyObject target, IEnumerable? value) => target.SetValue(RowsProperty, value);
    public static int GetInsertIndex(DependencyObject target) => (int)target.GetValue(InsertIndexProperty);
    public static void SetInsertIndex(DependencyObject target, int value) => target.SetValue(InsertIndexProperty, value);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _) { if (target is DataGrid grid) Refresh(grid); }
    private sealed class QueryColumn(SetupQueryKey key) : DataGridTextColumn { internal SetupQueryKey Key => key; }

    internal static void Refresh(DataGrid grid)
    {
        var rows = GetRows(grid)?.OfType<ISetupQueryRow>().ToArray() ?? [];
        var keys = rows.SelectMany(row => row.SetupQueries.Keys).Distinct()
            .OrderBy(key => key.Name, StringComparer.Ordinal)
            .ThenBy(key => Sortopia.Runs.SortopiaDataType.kindName(key.DataType), StringComparer.Ordinal)
            .ThenBy(key => Sortopia.Runs.SortopiaDataType.text(key.DataType), StringComparer.Ordinal).ToArray();
        foreach (var row in rows) row.SetupQueries.SetColumns(keys);
        var existing = grid.Columns.OfType<QueryColumn>().ToArray();
        if (existing.Select(column => column.Key).SequenceEqual(keys)) return;
        // Discard obsolete index paths when a different set of query keys is loaded.
        foreach (var sort in grid.Items.SortDescriptions.Where(sort => sort.PropertyName.StartsWith("SetupQueries.Cells[", StringComparison.Ordinal)).ToArray())
            grid.Items.SortDescriptions.Remove(sort);
        foreach (var column in existing) grid.Columns.Remove(column);
        var repeated = keys.GroupBy(key => key.Name).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < keys.Length; index++)
        {
            var key = keys[index];
            var path = $"SetupQueries.Cells[{index}]";
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("InheritedSetupQueryDetails")));
            var column = new QueryColumn(key)
            {
                Header = $"Setup · {(repeated.Contains(key.Name) ? key.DataTypeLabel + " · " : "")}{key.Name}",
                Binding = new Binding(path + ".Text") { Mode = BindingMode.OneWay },
                SortMemberPath = path + ".SortValue", CanUserSort = true,
                Width = DataGridLength.SizeToHeader, MinWidth = 120, MaxWidth = 360, ElementStyle = style
            };
            grid.Columns.Insert(Math.Min(GetInsertIndex(grid) + index, grid.Columns.Count), column);
        }
    }
}
