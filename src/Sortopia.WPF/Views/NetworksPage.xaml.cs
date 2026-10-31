using System.Windows.Controls;
using Sortopia.WPF.ViewModels;

namespace Sortopia.WPF.Views;

public partial class NetworksPage : UserControl
{
    public NetworksPage()
    {
        InitializeComponent();
        Diagram.PlayheadChanged += (_, _) => FollowPlayhead();
        // View lifetime only: never leave a playback timer running on a hidden page.
        Unloaded += (_, _) => (DataContext as NetworksPageViewModel)?.Pause();
    }

    private void FollowPlayhead()
    {
        if (DataContext is not NetworksPageViewModel { FollowPlayback: true } || DiagramViewport.ViewportWidth <= 0) return;
        var x = Diagram.PlayheadX + DiagramViewport.Padding.Left;
        var offset = DiagramViewport.HorizontalOffset;
        const double margin = 40;
        if (x < offset + margin) DiagramViewport.ScrollToHorizontalOffset(Math.Max(0, x - margin));
        else if (x > offset + DiagramViewport.ViewportWidth - margin)
            DiagramViewport.ScrollToHorizontalOffset(x - DiagramViewport.ViewportWidth + margin);
    }
}
