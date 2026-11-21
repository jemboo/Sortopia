using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Sortopia.WPF.Views.Shared;
namespace Sortopia.WPF.Views.Pages.ResultsExecution;
public partial class ResultsExecutionPanel : UserControl
{
    private ResultsExecutionPageViewModel? model;
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
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResultsExecutionPageViewModel.ParameterColumns) or "" or null) UpdateColumns();
    }
    private void UpdateColumns()
    {
        RunParametersGrid.Columns.Clear();
        RunParametersGrid.Columns.Add(new DataGridTemplateColumn { Header = "Run", CellTemplate = (DataTemplate)FindResource("ExecutionCheck"), Width = 50,
            SortMemberPath = nameof(WorkItemStatusRow.IsSelected) });
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "File name", Binding = new Binding("FileName"), Width = 325, MinWidth = 180 });
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = 90 });
        var generationStyle = new Style(typeof(TextBlock));
        generationStyle.Setters.Add(new Setter(ToolTipProperty, "Completed generations / target generations (SnapshotInterval × SnapshotCount for SGD). Updates at full snapshot intervals. A dash means no generation has been reported or this function does not evolve."));
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Generations", Binding = new Binding("GenerationsText"), SortMemberPath = "CompletedGenerations",
            Width = 135, ElementStyle = generationStyle });
        var elapsedStyle = new Style(typeof(TextBlock));
        elapsedStyle.Setters.Add(new Setter(ToolTipProperty, "Elapsed time for the latest attempt in this folder session (hours:minutes:seconds). Updates while running."));
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Elapsed", Binding = new Binding("ElapsedText"), Width = 110, ElementStyle = elapsedStyle });
        var remainingStyle = new Style(typeof(TextBlock));
        remainingStyle.Setters.Add(new Setter(ToolTipProperty, "Estimated remaining time (hours:minutes:seconds): elapsed × remaining generations / completed generations. Uses the latest reported generation count. Available after the first completed generation; a dash means no estimate is available."));
        RunParametersGrid.Columns.Add(new DataGridTextColumn { Header = "Est. remaining", Binding = new Binding("EstimatedRemainingText"), SortMemberPath = "EstimatedRemaining",
            Width = 140, ElementStyle = remainingStyle });
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
