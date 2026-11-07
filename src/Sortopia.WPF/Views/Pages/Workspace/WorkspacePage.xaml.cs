using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using Sortopia.WPF.ViewModels.Pages.Workspace;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class WorkspacePage : UserControl
{
    private readonly DispatcherTimer diskStatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        if (DataContext is WorkspacePageViewModel viewModel) viewModel.Selected = args.NewValue as WorkspaceTreeNode;
    }
    public WorkspacePage()
    {
        InitializeComponent();
        diskStatusTimer.Tick += (_, _) => RefreshDiskStatus();
        Loaded += (_, _) => { RefreshDiskStatus(); diskStatusTimer.Start(); };
        Unloaded += (_, _) => diskStatusTimer.Stop();
    }
    private void RefreshDiskStatus()
    {
        if (DataContext is WorkspacePageViewModel viewModel) viewModel.RefreshDiskStatus();
    }
}
