using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Sortopia.WPF.Rendering;
using Sortopia.WPF.ViewModels.Pages.Networks;

namespace Sortopia.WPF.Views.Pages.Networks;

public partial class NetworksPage : UserControl
{
    private NetworksPageViewModel? viewModel;
    private NetworkLayout? geometry;

    public NetworksPage()
    {
        InitializeComponent();
        Diagram.AnimationSchemeChanged += (_, _) =>
        {
            if (viewModel is null) return;
            geometry = new NetworkLayout(viewModel.Trace, Diagram.AnimationScheme);
            FitDiagram();
        };
        Diagram.PlayheadChanged += (_, _) => FollowPlayhead();
        DiagramViewport.SizeChanged += (_, _) => FitDiagram();
        Loaded += (_, _) => AttachViewModel();
        DataContextChanged += (_, _) => AttachViewModel();
        Unloaded += (_, _) =>
        {
            viewModel?.Pause();
            if (viewModel is not null) viewModel.PropertyChanged -= OnViewModelChanged;
            viewModel = null;
        };
    }

    private void AttachViewModel()
    {
        if (viewModel is not null) viewModel.PropertyChanged -= OnViewModelChanged;
        viewModel = DataContext as NetworksPageViewModel;
        if (viewModel is null) return;
        viewModel.PropertyChanged += OnViewModelChanged;
        geometry = new NetworkLayout(viewModel.Trace, Diagram.AnimationScheme);
        FitDiagram();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NetworksPageViewModel.Trace))
        {
            geometry = new NetworkLayout(viewModel!.Trace, Diagram.AnimationScheme);
            FitDiagram();
            DiagramViewport.ScrollToHome();
        }
        else if (e.PropertyName == nameof(NetworksPageViewModel.ZoomFactor)) FitDiagram();
    }

    private void FitDiagram()
    {
        if (geometry is null || viewModel is null) return;
        // Use the outer viewport size so scrollbars appearing while zoomed cannot change the fit scale.
        var width = DiagramViewport.ActualWidth - DiagramViewport.Padding.Left - DiagramViewport.Padding.Right - 2;
        var height = DiagramViewport.ActualHeight - DiagramViewport.Padding.Top - DiagramViewport.Padding.Bottom - 2;
        if (width <= 0 || height <= 0) return;
        var fit = Math.Min(1.5, Math.Min(width / geometry.Width, height / geometry.Height));
        Diagram.Zoom = fit * viewModel.ZoomFactor;
        if (viewModel.ZoomFactor == 1) DiagramViewport.ScrollToHome();
    }

    private void FollowPlayhead()
    {
        if (DataContext is not NetworksPageViewModel { FollowPlayback: true, ZoomFactor: > 1 } || DiagramViewport.ViewportWidth <= 0) return;
        var x = Diagram.PlayheadX + DiagramViewport.Padding.Left;
        var offset = DiagramViewport.HorizontalOffset;
        const double margin = 40;
        if (x < offset + margin) DiagramViewport.ScrollToHorizontalOffset(Math.Max(0, x - margin));
        else if (x > offset + DiagramViewport.ViewportWidth - margin)
            DiagramViewport.ScrollToHorizontalOffset(x - DiagramViewport.ViewportWidth + margin);
    }
}
