using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Sortopia.WPF.Views.Shared;
namespace Sortopia.WPF.Views.Pages.ResultsExecution;
public partial class ResultsExecutionPanel : UserControl
{
    private ResultsExecutionPageViewModel? model;
    public ResultsExecutionPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { if (model is not null) { model.PropertyChanged -= Changed; model.StopSnapshotMonitoring(); } model = null; };
    }
    private void Attach()
    {
        if (model is not null) { model.PropertyChanged -= Changed; model.StopSnapshotMonitoring(); }
        model = DataContext as ResultsExecutionPageViewModel;
        if (model is not null) model.PropertyChanged += Changed;
        if (IsLoaded) model?.StartSnapshotMonitoring(action => Dispatcher.BeginInvoke(action));
        UpdateColumns();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResultsExecutionPageViewModel.ParameterColumns) or "" or null) UpdateColumns();
    }
    private void UpdateColumns()
    {
        RunParametersGrid.Columns.Clear();
        RunParametersGrid.Columns.Add(new DataGridTemplateColumn { Header = "Details", CellTemplate = (DataTemplate)FindResource("ExecutionExpander"), Width = 64,
            CanUserSort = false, CanUserResize = false, CanUserReorder = false });
        RunParametersGrid.Columns.Add(new DataGridTemplateColumn { Header = "Run", CellTemplate = (DataTemplate)FindResource("ExecutionCheck"), Width = 50,
            SortMemberPath = nameof(WorkItemStatusRow.IsSelected) });
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "File name", Binding = new Binding("FileName"), Width = 325, MinWidth = 180 });
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 90 });
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Generations", Binding = new Binding("GenerationsText"), SortMemberPath = "CompletedGenerations",
            Width = 135 });
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Outputs", Binding = new Binding("OutputSummary"), Width = 100 });
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Executor", Binding = new Binding("FunctionName"), Width = 230 });
        SetupQueryColumns.Refresh(RunParametersGrid);
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
}
