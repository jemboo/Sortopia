using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
namespace Sortopia.WPF.Views.Pages.ResultsExecution;
public partial class ResultsExecutionPanel : UserControl
{
    private ResultsExecutionPageViewModel? model;
    private bool synchronizingSelection;
    private readonly DispatcherTimer elapsedTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    public ResultsExecutionPanel()
    {
        InitializeComponent();
        elapsedTimer.Tick += (_, _) => model?.RefreshElapsedTimes();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => { Attach(); elapsedTimer.Start(); };
        Unloaded += (_, _) => { elapsedTimer.Stop(); if (model is not null) model.PropertyChanged -= Changed; model = null; };
    }
    private void Attach()
    {
        if (model is not null) model.PropertyChanged -= Changed;
        model = DataContext as ResultsExecutionPageViewModel;
        if (model is not null) model.PropertyChanged += Changed;
        UpdateColumns();
        UpdateSelection();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResultsExecutionPageViewModel.ParameterColumns) or "" or null) UpdateColumns();
        if (e.PropertyName is nameof(ResultsExecutionPageViewModel.SelectedCount) or "" or null) UpdateSelection();
    }
    private void UpdateColumns()
    {
        RunParametersGrid.Columns.Clear();
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "File name", Binding = new Binding("FileName"), Width = 325, MinWidth = 180 });
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 90 });
        var elapsedStyle = new Style(typeof(TextBlock));
        elapsedStyle.Setters.Add(new Setter(ToolTipProperty, "Elapsed time for the latest attempt in this folder session (hours:minutes:seconds). Updates while running."));
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Elapsed", Binding = new Binding("ElapsedText"), Width = 110, ElementStyle = elapsedStyle });
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Outputs", Binding = new Binding("OutputSummary"), Width = 100 });
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Executor", Binding = new Binding("FunctionName"), Width = 230 });
        if (model is null) return;
        for (var i = 0; i < model.ParameterColumns.Length; i++)
        {
            var display = new Style(typeof(TextBlock));
            display.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            display.Setters.Add(new Setter(ToolTipProperty, new Binding($"Cells[{i}]")));
            RunParametersGrid.Columns.Add(new DataGridTextColumn {
                Header = model.ParameterColumns[i], Binding = new Binding($"Cells[{i}]") { Mode = BindingMode.OneWay },
                ElementStyle = display, Width = 150, MinWidth = 90
            });
        }
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizingSelection) return;
        synchronizingSelection = true;
        try
        {
            model?.UpdateSelection(e.AddedItems.Cast<WorkItemStatusRow>(), e.RemovedItems.Cast<WorkItemStatusRow>());
        }
        finally { synchronizingSelection = false; }
    }
    private void UpdateSelection()
    {
        if (synchronizingSelection || model is null) return;
        synchronizingSelection = true;
        try
        {
            // Sync the selection itself, including rows outside the virtualized viewport.
            var wanted = model.Rows.Where(row => row.IsSelected).ToHashSet();
            var current = RunParametersGrid.SelectedItems.Cast<WorkItemStatusRow>().ToHashSet();
            foreach (var row in current.Except(wanted)) RunParametersGrid.SelectedItems.Remove(row);
            foreach (var row in wanted.Except(current)) RunParametersGrid.SelectedItems.Add(row);
        }
        finally { synchronizingSelection = false; }
    }
}
