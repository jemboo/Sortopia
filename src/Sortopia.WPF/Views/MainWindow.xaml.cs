using System.Windows;
using System.ComponentModel;
using Microsoft.Win32;
using Sortopia.WPF.ViewModels;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.Views.Pages.Workspace;

namespace Sortopia.WPF.Views;

public partial class MainWindow : Window
{
    private bool closeConfirmed;
    private bool checkingClose;
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || closeConfirmed) return;
        e.Cancel = true;
        if (checkingClose) return;
        checkingClose = true;
        try
        {
            if (DataContext is not MainWindowViewModel shell || await shell.TryCloseAsync())
                _ = Dispatcher.BeginInvoke(new Action(() => { closeConfirmed = true; Close(); }));
        }
        finally { checkingClose = false; }
    }
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(PickWorkspaceFolder, confirmWorkspaceChanges: ConfirmWorkspaceChanges,
            pickExistingWorkspaceFolder: PickExistingWorkspaceFolder);
    }

    private WorkspaceUnsavedChoice ConfirmWorkspaceChanges()
    {
        var dialog = new WorkspaceUnsavedChangesDialog { Owner = this };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private string? PickExistingWorkspaceFolder(WorkspaceFolderRequest request)
    {
        var dialog = new OpenFolderDialog
        {
            Title = $"Select existing {request.Kind.ToString().ToLowerInvariant()} folder to add to {request.ContainerFolder}",
            InitialDirectory = request.InitialDirectory,
            Multiselect = false
        };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private string? PickWorkspaceFolder(string? currentFolder)
    {
        var dialog = new OpenFolderDialog { Title = "Select Workspace Folder", Multiselect = false };
        if (System.IO.Directory.Exists(currentFolder)) dialog.InitialDirectory = currentFolder;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }
}
