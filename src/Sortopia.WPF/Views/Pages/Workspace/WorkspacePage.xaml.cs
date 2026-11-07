using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using Sortopia.WPF.ViewModels.Pages.Workspace;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class WorkspacePage : UserControl
{
    private readonly DispatcherTimer diskStatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        if (DataContext is WorkspacePageViewModel viewModel) viewModel.Selected = args.NewValue as WorkspaceTreeNode;
    }
    private void OnTreeRightClick(object sender, MouseButtonEventArgs args)
    {
        var source = args.OriginalSource as DependencyObject;
        while (source is not null && source is not TreeViewItem)
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        if (source is TreeViewItem item) { item.IsSelected = true; item.Focus(); }
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
