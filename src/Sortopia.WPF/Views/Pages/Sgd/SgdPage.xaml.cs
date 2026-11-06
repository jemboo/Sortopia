using System.Windows.Controls;
using Sortopia.WPF.ViewModels.Pages.Sgd;

namespace Sortopia.WPF.Views.Pages.Sgd;

public partial class SgdPage : UserControl
{
    public SgdPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as SgdPageViewModel)?.Stop();
    }
}
