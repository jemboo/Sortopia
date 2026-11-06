using System.Windows.Controls;
using Sortopia.WPF.ViewModels;

namespace Sortopia.WPF.Views;

public partial class SgdPage : UserControl
{
    public SgdPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as SgdPageViewModel)?.Stop();
    }
}
