using System.Windows;
using Microsoft.Win32;
using Sortopia.WPF.ViewModels;

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
        DataContext = new MainWindowViewModel(PickProjectFolder);
    }

    private string? PickProjectFolder(string? currentFolder)
    {
        var dialog = new OpenFolderDialog { Title = "Open Project Folder" };
        if (currentFolder != null) dialog.InitialDirectory = currentFolder;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }
}
