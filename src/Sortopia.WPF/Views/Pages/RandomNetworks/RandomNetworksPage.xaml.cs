using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels.Pages.RandomNetworks;
using Sortopia.WPF.Views.Shared.Controls;

namespace Sortopia.WPF.Views.Pages.RandomNetworks;

public partial class RandomNetworksPage : UserControl
{
    private RandomNetworksPageViewModel? viewModel;
    private NetworkDefinition? fittedNetwork;
    private int fittedMode;
    private bool zoomAnimating;
    private int zoomVersion;
    private Point? panStart;
    private Point panOffset;

    public RandomNetworksPage()
    {
        InitializeComponent();
        DiagramViewport.SizeChanged += (_, _) => FitDiagram(animate: zoomAnimating);
        Loaded += (_, _) => AttachViewModel();
        DataContextChanged += (_, _) => AttachViewModel();
        Unloaded += (_, _) =>
        {
            ReleasePan();
            StopZoomAnimation();
            viewModel?.Leave();
            if (viewModel is not null) viewModel.PropertyChanged -= OnViewModelChanged;
            viewModel = null;
        };
    }

    private void AttachViewModel()
    {
        if (viewModel is not null) viewModel.PropertyChanged -= OnViewModelChanged;
        StopZoomAnimation();
        viewModel = DataContext as RandomNetworksPageViewModel;
        if (viewModel is null) return;
        fittedNetwork = viewModel.SelectedNetwork;
        fittedMode = viewModel.DisplayMode;
        viewModel.PropertyChanged += OnViewModelChanged;
        FitDiagram();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RandomNetworksPageViewModel.DisplayLayout))
        {
            var animate = ReferenceEquals(fittedNetwork, viewModel!.SelectedNetwork) && fittedMode != viewModel.DisplayMode;
            fittedNetwork = viewModel.SelectedNetwork;
            fittedMode = viewModel.DisplayMode;
            ReleasePan();
            FitDiagram(animate);
        }
        else if (e.PropertyName == nameof(RandomNetworksPageViewModel.ZoomFactor)) FitDiagram(preserveCenter: true);
    }

    private void StopZoomAnimation()
    {
        var current = Diagram.Zoom;
        Diagram.BeginAnimation(NetworkDiagram.ZoomProperty, null);
        Diagram.Zoom = current;
        zoomAnimating = false;
        zoomVersion++;
    }

    private void FitDiagram(bool animate = false, bool preserveCenter = false)
    {
        if (viewModel is null) return;
        var geometry = viewModel.DisplayLayout;
        var width = DiagramViewport.ActualWidth - DiagramViewport.Padding.Left - DiagramViewport.Padding.Right - 2;
        var height = DiagramViewport.ActualHeight - DiagramViewport.Padding.Top - DiagramViewport.Padding.Bottom - 2;
        if (width <= 0 || height <= 0) return;
        var target = Math.Min(1.5, Math.Min(width / geometry.Width, height / geometry.Height)) * viewModel.ZoomFactor;
        var from = Diagram.Zoom;
        var anchor = new Point(DiagramViewport.ActualWidth / 2, DiagramViewport.ActualHeight / 2);
        var oldCenter = DiagramViewport.TranslatePoint(anchor, Diagram);
        StopZoomAnimation();
        Diagram.Zoom = target;
        if (animate && IsLoaded && SystemParameters.ClientAreaAnimation && Math.Abs(target - from) > 0.0001)
        {
            Diagram.Zoom = from;
            // Let CEs begin moving before slowly reframing the remaining network.
            var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(target > from ? 1800 : 1200))
            {
                BeginTime = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            var version = zoomVersion;
            zoomAnimating = true;
            animation.Completed += (_, _) =>
            {
                if (version != zoomVersion) return;
                zoomAnimating = false;
                Diagram.BeginAnimation(NetworkDiagram.ZoomProperty, null);
                Diagram.Zoom = target;
                if (viewModel?.ZoomFactor == 1) DiagramViewport.ScrollToHome();
            };
            Diagram.BeginAnimation(NetworkDiagram.ZoomProperty, animation);
            DiagramViewport.ScrollToHome();
        }
        else if (preserveCenter && viewModel.ZoomFactor > 1)
        {
            Diagram.UpdateLayout();
            var newCenter = Diagram.TranslatePoint(new Point(oldCenter.X * target / from, oldCenter.Y * target / from), DiagramViewport);
            DiagramViewport.ScrollToHorizontalOffset(DiagramViewport.HorizontalOffset + newCenter.X - anchor.X);
            DiagramViewport.ScrollToVerticalOffset(DiagramViewport.VerticalOffset + newCenter.Y - anchor.Y);
        }
        else DiagramViewport.ScrollToHome();
    }

    private void StartPan(object sender, MouseButtonEventArgs e)
    {
        // Keep the scrollbars' own mouse handling intact.
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
            if (source is ScrollBar) return;
        if (DiagramViewport.ScrollableWidth <= 0 && DiagramViewport.ScrollableHeight <= 0) return;
        if (!DiagramViewport.CaptureMouse()) return;
        panStart = e.GetPosition(DiagramViewport);
        panOffset = new Point(DiagramViewport.HorizontalOffset, DiagramViewport.VerticalOffset);
        DiagramViewport.Cursor = Cursors.ScrollAll;
        ToolTipService.SetIsEnabled(Diagram, false);
        e.Handled = true;
    }

    private void MovePan(object sender, MouseEventArgs e)
    {
        if (panStart is not { } start) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleasePan(); return; }
        var current = e.GetPosition(DiagramViewport);
        DiagramViewport.ScrollToHorizontalOffset(panOffset.X + start.X - current.X);
        DiagramViewport.ScrollToVerticalOffset(panOffset.Y + start.Y - current.Y);
        e.Handled = true;
    }

    private void EndPan(object sender, MouseButtonEventArgs e)
    {
        if (panStart is null) return;
        ReleasePan();
        e.Handled = true;
    }

    private void LostPanCapture(object sender, MouseEventArgs e) => ReleasePan();

    private void ReleasePan()
    {
        panStart = null;
        DiagramViewport.Cursor = null;
        ToolTipService.SetIsEnabled(Diagram, true);
        if (DiagramViewport.IsMouseCaptured) DiagramViewport.ReleaseMouseCapture();
    }

    private void ZoomWithWheel(object sender, MouseWheelEventArgs e)
    {
        if (viewModel is null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        viewModel.ZoomFactor *= Math.Pow(1.15, e.Delta / 120d);
        e.Handled = true;
    }
}
