using System.Windows;
using System.Windows.Controls;
using Sortopia.WPF.ViewModels.Shared.Controls;

namespace Sortopia.WPF.Views.Shared.Controls;

public partial class SorterBinMovie : UserControl
{
    public SorterBinMovie()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as SorterBinMovieViewModel)?.Pause();
        IsVisibleChanged += (_, _) => { if (!IsVisible) (DataContext as SorterBinMovieViewModel)?.Pause(); };
        DataContextChanged += (_, e) => (e.OldValue as SorterBinMovieViewModel)?.Pause();
    }
}
