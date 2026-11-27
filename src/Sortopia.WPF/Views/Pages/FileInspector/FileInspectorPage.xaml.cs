using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Sortopia.WPF.ViewModels.Pages.FileInspector;

namespace Sortopia.WPF.Views.Pages.FileInspector;

public partial class FileInspectorPage : UserControl
{
    private FileInspectorPageViewModel? model;
    public FileInspectorPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        DataContextChanged += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }
    private void Detach()
    {
        if (model is null) return;
        model.StopHistoryMonitoring();
        model.PropertyChanged -= OnModelChanged;
        model = null;
    }
    private void Attach()
    {
        var current = DataContext as FileInspectorPageViewModel;
        if (!ReferenceEquals(model, current))
        {
            Detach();
            model = current;
            if (model is not null) model.PropertyChanged += OnModelChanged;
            RefreshColumns();
        }
        if (IsLoaded) model?.StartHistoryMonitoring(action => Dispatcher.BeginInvoke(action));
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileInspectorPageViewModel.Entries)) RefreshColumns();
    }
    private sealed class QueryColumn(InspectorQueryKey key) : DataGridTextColumn { internal InspectorQueryKey Key => key; }
    private DataGridTextColumn? generationColumn;
    private void RefreshColumns()
    {
        var rows = model?.Entries ?? [];
        var keys = rows.SelectMany(row => row.QueryParameters.Keys).Distinct()
            .OrderBy(key => key.Name, StringComparer.Ordinal).ThenBy(key => key.DataTypeLabel, StringComparer.Ordinal).ToArray();
        foreach (var row in rows) row.SetColumns(keys);
        var existing = FileList.Columns.OfType<QueryColumn>().ToArray();
        if (!existing.Select(column => column.Key).SequenceEqual(keys))
        {
            foreach (var sort in FileList.Items.SortDescriptions.Where(sort => sort.PropertyName.StartsWith("QueryCells[", StringComparison.Ordinal)).ToArray())
                FileList.Items.SortDescriptions.Remove(sort);
            foreach (var column in existing) FileList.Columns.Remove(column);
            var repeated = keys.GroupBy(key => key.Name).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            for (var index = 0; index < keys.Length; index++)
            {
                var key = keys[index];
                FileList.Columns.Add(new QueryColumn(key)
                {
                    Header = repeated.Contains(key.Name) ? $"{key.DataTypeLabel} · {key.Name}" : key.Name,
                    Binding = new Binding($"QueryCells[{index}].Text") { Mode = BindingMode.OneWay },
                    SortMemberPath = $"QueryCells[{index}].SortValue", CanUserSort = true,
                    Width = DataGridLength.SizeToHeader, MinWidth = 100, MaxWidth = 360
                });
            }
        }
        if (rows.Any(row => row.IsEvolution))
        {
            if (generationColumn is null)
            {
                generationColumn = new DataGridTextColumn { Header = "GenerationCurrent", Binding = new Binding("GenerationCurrent"),
                    SortMemberPath = "GenerationCurrent", Width = DataGridLength.SizeToHeader, MinWidth = 130 };
                FileList.Columns.Insert(3, generationColumn);
            }
        }
        else if (generationColumn is not null)
        {
            foreach (var sort in FileList.Items.SortDescriptions.Where(sort => sort.PropertyName == "GenerationCurrent").ToArray()) FileList.Items.SortDescriptions.Remove(sort);
            FileList.Columns.Remove(generationColumn);
            generationColumn = null;
        }
    }
    private void OnOpenFolder(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(FileList, e.OriginalSource as DependencyObject) is DataGridRow)
            (DataContext as FileInspectorPageViewModel)?.OpenFolderCommand.Execute(null);
    }
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { (DataContext as FileInspectorPageViewModel)?.OpenFolderCommand.Execute(null); e.Handled = true; }
    }
}
