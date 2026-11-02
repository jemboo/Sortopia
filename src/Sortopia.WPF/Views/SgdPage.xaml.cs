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

    private void CenterPlotOnResize(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentWidthChange == 0 && e.ViewportWidthChange == 0) return;
        var previousRange = Math.Max(0, (e.ExtentWidth - e.ExtentWidthChange) - (e.ViewportWidth - e.ViewportWidthChange));
        var previousOffset = e.HorizontalOffset - e.HorizontalChange;
        if (Math.Abs(previousOffset - previousRange / 2) <= 1)
            PlotScroll.ScrollToHorizontalOffset(PlotScroll.ScrollableWidth / 2);
    }
}
