using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Preview = Sortopia.WPF.ViewModels.Pages.Workspace.WorkspaceParameterSetsPreview;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class WorkspaceParameterSetsPreview : UserControl
{
    public WorkspaceParameterSetsPreview()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateColumns();
    }

    private void UpdateColumns()
    {
        ParameterSetsTable.Columns.Clear();
        ParameterSetsTable.Columns.Add(new DataGridTextColumn { Header = "Set", Binding = new Binding("Number"), Width = 48 });
        if (DataContext is not Preview preview) return;
        for (var index = 0; index < preview.Columns.Length; index++)
            ParameterSetsTable.Columns.Add(new DataGridTextColumn
            {
                Header = preview.Columns[index].Header, Binding = new Binding($"Values[{index}]") { Mode = BindingMode.OneWay },
                MinWidth = 135, MaxWidth = 280, Width = DataGridLength.SizeToCells
            });
    }
}
