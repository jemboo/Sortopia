using System.Windows.Controls;
using System.Windows.Input;
using Sortopia.WPF.ViewModels.Pages.FileInspector;

namespace Sortopia.WPF.Views.Pages.FileInspector;

public partial class FileInspectorPage : UserControl
{
    private FileInspectorPageViewModel? model;
    public FileInspectorPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        DataContextChanged += (_, _) => Attach();
        Unloaded += (_, _) => { model?.StopHistoryMonitoring(); model = null; };
    }
    private void Attach()
    {
        var current = DataContext as FileInspectorPageViewModel;
        if (!ReferenceEquals(model, current)) model?.StopHistoryMonitoring();
        model = current;
        if (IsLoaded) model?.StartHistoryMonitoring(action => Dispatcher.BeginInvoke(action));
    }
    private void OnOpenFolder(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(FileList, e.OriginalSource as System.Windows.DependencyObject) is ListViewItem)
            (DataContext as FileInspectorPageViewModel)?.OpenFolderCommand.Execute(null);
    }
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { (DataContext as FileInspectorPageViewModel)?.OpenFolderCommand.Execute(null); e.Handled = true; }
    }
}
