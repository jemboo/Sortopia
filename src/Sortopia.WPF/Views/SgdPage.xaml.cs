using System.Windows.Controls;
using System.Windows.Input;
using Sortopia.WPF.ViewModels;

namespace Sortopia.WPF.Views;

public partial class SgdPage : UserControl
{
    public SgdPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as SgdPageViewModel)?.Stop();
    }

    private void DisplayModeChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (SwitchDiagram is not null && sender is RadioButton { Tag: string tag })
            SwitchDiagram.DisplayMode = int.Parse(tag);
    }

    private void IndexGotFocus(object sender, KeyboardFocusChangedEventArgs e) => ((TextBox)sender).SelectAll();

    private void IndexLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        (((TextBox)sender).DataContext as NetworkHistoryViewModel)?.FinishIndexEdit(commit: true);

    private void IndexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        var editor = (TextBox)sender;
        (editor.DataContext as NetworkHistoryViewModel)?.FinishIndexEdit(commit: e.Key == Key.Enter);
        editor.SelectAll();
        e.Handled = true;
    }

}
