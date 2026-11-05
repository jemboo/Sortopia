using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sortopia.WPF.ViewModels;

namespace Sortopia.WPF.Controls;

public partial class ParamEditor2d : UserControl
{
    private readonly List<ParameterCell> selectionOrder = [];
    private bool synchronizing;
    private static readonly DependencyPropertyKey FirstSelectedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(FirstSelected), typeof(ParameterCell), typeof(ParamEditor2d), new PropertyMetadata(null));
    public static readonly DependencyProperty FirstSelectedProperty = FirstSelectedPropertyKey.DependencyProperty;
    public ParameterCell? FirstSelected => (ParameterCell?)GetValue(FirstSelectedProperty);
    private static readonly DependencyPropertyKey SelectionLabelPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectionLabel), typeof(string), typeof(ParamEditor2d), new PropertyMetadata("Parameter value"));
    public static readonly DependencyProperty SelectionLabelProperty = SelectionLabelPropertyKey.DependencyProperty;
    public string SelectionLabel => (string)GetValue(SelectionLabelProperty);

    public ParamEditor2d() => InitializeComponent();

    private void ParameterClicked(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is ParameterCell cell)
            SelectCell(cell, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
    }

    internal void SelectCell(ParameterCell cell, bool extend)
    {
        if (!Cells.Items.Contains(cell)) return;
        if (!extend) Cells.UnselectAll();
        if (!Cells.SelectedItems.Contains(cell)) Cells.SelectedItems.Add(cell);
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        synchronizing = true;
        try
        {
            selectionOrder.RemoveAll(cell => !Cells.SelectedItems.Contains(cell));
            foreach (var cell in e.AddedItems.OfType<ParameterCell>())
                if (!selectionOrder.Contains(cell)) selectionOrder.Add(cell);
            var first = selectionOrder.FirstOrDefault();
            // Copy backing values, including for parameters whose sliders use a logarithmic scale.
            if (first is not null)
                foreach (var cell in e.AddedItems.OfType<ParameterCell>()) cell.Value = first.Value;
            SetValue(FirstSelectedPropertyKey, first);
            SetValue(SelectionLabelPropertyKey, first is null ? "Parameter value" : selectionOrder.Count == 1
                ? first.Label : $"{selectionOrder.Count} selected · {first.Label}");
        }
        finally { synchronizing = false; }
    }

    private void SliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // A table switch may update the slider limits before clearing its previous selection.
        if (synchronizing || FirstSelected is not { } first || DataContext is not ParameterArrayViewModel array
            || !array.Cells.Contains(first)) return;
        synchronizing = true;
        try
        {
            first.SliderValue = e.NewValue;
            foreach (var cell in selectionOrder)
                if (cell != first && array.Cells.Contains(cell)) cell.Value = first.Value;
        }
        finally { synchronizing = false; }
    }
}
