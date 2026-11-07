using System.Windows;
using Sortopia.WPF.ViewModels.Pages.Workspace;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class WorkspaceUnsavedChangesDialog : Window
{
    public WorkspaceUnsavedChoice Choice { get; private set; } = WorkspaceUnsavedChoice.Cancel;
    public WorkspaceUnsavedChangesDialog() => InitializeComponent();
    private void OnSave(object sender, RoutedEventArgs e) { Choice = WorkspaceUnsavedChoice.Save; DialogResult = true; }
    private void OnDiscard(object sender, RoutedEventArgs e) { Choice = WorkspaceUnsavedChoice.Discard; DialogResult = true; }
}
