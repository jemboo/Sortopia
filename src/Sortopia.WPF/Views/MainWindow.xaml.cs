using System.Windows;
using Microsoft.Win32;
using Sortopia.WPF.ViewModels;

namespace Sortopia.WPF.Views;

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
        DataContext = new MainWindowViewModel(PickProjectsFolder);
    }

    private string? PickProjectsFolder(string? currentFolder)
    {
        var dialog = new OpenFolderDialog { Title = "Select Projects Folder", Multiselect = false };
        if (System.IO.Directory.Exists(currentFolder)) dialog.InitialDirectory = currentFolder;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }
}
