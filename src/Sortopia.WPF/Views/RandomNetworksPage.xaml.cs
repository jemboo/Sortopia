using System.Windows.Controls;
using Sortopia.WPF.ViewModels;

namespace Sortopia.WPF.Views;

public partial class RandomNetworksPage : UserControl
{
    public RandomNetworksPage()
    {
        InitializeComponent();
        Diagram.CompactionCompleted += (_, _) => (DataContext as RandomNetworksPageViewModel)?.FinishCompaction();
        Diagram.PlayheadChanged += (_, _) => FollowPlayhead();
        // View lifetime only: never leave a playback timer running on a hidden page.
        Unloaded += (_, _) => (DataContext as RandomNetworksPageViewModel)?.Leave();
    }

    private void FollowPlayhead()
    {
        if (DataContext is not RandomNetworksPageViewModel { FollowPlayback: true, IsCompacting: false } || DiagramViewport.ViewportWidth <= 0) return;
        var x = Diagram.PlayheadX + DiagramViewport.Padding.Left;
        var offset = DiagramViewport.HorizontalOffset;
        const double margin = 40;
        if (x < offset + margin) DiagramViewport.ScrollToHorizontalOffset(Math.Max(0, x - margin));
        else if (x > offset + DiagramViewport.ViewportWidth - margin)
            DiagramViewport.ScrollToHorizontalOffset(x - DiagramViewport.ViewportWidth + margin);
    }
}
