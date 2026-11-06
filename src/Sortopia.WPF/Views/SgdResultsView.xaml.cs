using System.Windows.Controls;
using System.Windows.Input;
using Sortopia.WPF.ViewModels;

namespace Sortopia.WPF.Views;

public partial class SgdResultsView : UserControl
{
    public SgdResultsView() => InitializeComponent();

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
