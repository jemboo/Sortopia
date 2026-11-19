using System.Windows;
using Sortopia.WPF.ViewModels.Pages.Workspace;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class WorkspaceResetDialog : Window
{
    public WorkspaceResetDialog(WorkspaceResetRequest request)
    {
        InitializeComponent();
        DataContext = request;
    }
    private void OnReset(object sender, RoutedEventArgs e) => DialogResult = true;
}
