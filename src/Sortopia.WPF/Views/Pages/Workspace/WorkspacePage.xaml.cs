using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using Sortopia.WPF.ViewModels.Pages.Workspace;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class WorkspacePage : UserControl
{
    private void OnConfigurationItemClick(object sender, RoutedEventArgs args) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { EntityInspectorViewport.ScrollToTop(); RevealActiveItem(); }));
    private void RevealActiveItem()
    {
        if (DataContext is not WorkspacePageViewModel viewModel) return;
        var item = viewModel.InspectorItems.FirstOrDefault(item => item.IsActive);
        if (item is not null && ConfigurationItems.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement row)
            row.BringIntoView();
    }
    private void OnReviewSaveIssue(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkContentElement { DataContext: WorkspaceSaveIssue issue }) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            FrameworkElement? target = DataContext is WorkspacePageViewModel { ActiveItemKey: "resultsFunction" or "setupFunction" }
                ? ConfigurationFields.FindName("FunctionsSection") as FrameworkElement : issue.Section switch
            {
                "Name" => issue.Node.IsWorkspace ? WorkspaceNameInput : EntityNameInput,
                "Location" => EntityLocationExpander,
                "Results settings" => ConfigurationFields.FindName("ResultsSettingsSection") as FrameworkElement,
                "Setup settings" => ConfigurationFields.FindName("SetupSettingsSection") as FrameworkElement,
                "Results parameter defaults" => ConfigurationFields.FindName("ResultsDefaultsSection") as FrameworkElement,
                "Setup parameter defaults" => ConfigurationFields.FindName("SetupDefaultsSection") as FrameworkElement,
                "Results parameter spans" => ConfigurationFields.FindName("ResultsSpansSection") as FrameworkElement,
                "Setup parameter spans" => ConfigurationFields.FindName("SetupSpansSection") as FrameworkElement,
                _ => null
            };
            if (target is Expander section) section.IsExpanded = true;
            target?.BringIntoView(new Rect(0, 0, target.ActualWidth, Math.Min(32, target.ActualHeight)));
            if (target is TextBox input) input.Focus();
        }));
    }
    private readonly DispatcherTimer diskStatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        if (DataContext is WorkspacePageViewModel viewModel) viewModel.Selected = args.NewValue as WorkspaceTreeNode;
        EntityInspectorViewport.ScrollToTop();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RevealActiveItem));
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
