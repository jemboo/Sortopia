using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sortopia.WPF.Controls;
using Sortopia.WPF.ViewModels;

namespace Sortopia.WPF.Views;

/// <summary>Shared interactions for the original and Sandbox SGD presentations.</summary>
public class SgdWorkspacePage : UserControl
{
    public SgdWorkspacePage() => Unloaded += (_, _) => (DataContext as SgdPageViewModel)?.Stop();

    protected void DisplayModeChanged(object sender, RoutedEventArgs e)
    {
        if (FindName("SwitchDiagram") is NetworkDiagram diagram && sender is RadioButton { Tag: string tag })
            diagram.DisplayMode = int.Parse(tag);
    }

    protected void IndexGotFocus(object sender, KeyboardFocusChangedEventArgs e) => ((TextBox)sender).SelectAll();

    protected void IndexLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        (((TextBox)sender).DataContext as NetworkHistoryViewModel)?.FinishIndexEdit(commit: true);

    protected void IndexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        var editor = (TextBox)sender;
        (editor.DataContext as NetworkHistoryViewModel)?.FinishIndexEdit(commit: e.Key == Key.Enter);
        editor.SelectAll();
        e.Handled = true;
    }
}
