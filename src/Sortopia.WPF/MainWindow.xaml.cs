using System.Windows;

namespace Sortopia.WPF;

public partial class MainWindow : Window
{
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }

    public MainWindow()
    {
        InitializeComponent();
    }
}
