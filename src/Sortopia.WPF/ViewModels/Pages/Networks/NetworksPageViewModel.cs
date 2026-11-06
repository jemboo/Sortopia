using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels.Shared.Controls;
namespace Sortopia.WPF.ViewModels.Pages.Networks;
public sealed class NetworksPageViewModel : NetworkPlaybackViewModel
{
    private NetworkDefinition colorNetwork;
    private int observedFrame;
    private double zoomFactor = 1;
    private IReadOnlyList<ComparatorState> comparatorStates;

    public NetworksPageViewModel(Action returnHome) : this(returnHome, CatalogNetwork.LoadAll()) { }
    private NetworksPageViewModel(Action returnHome, IReadOnlyList<CatalogNetwork> networks)
        : base(returnHome, networks.First(n => n.Width == 8))
    {
        Networks = networks;
        colorNetwork = Trace.Network;
        comparatorStates = Array.AsReadOnly(new ComparatorState[Trace.Comparators.Count]);
        ResetColorsCommand = new RelayCommand(ResetColors);
        PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(Trace))
            {
                // A new input keeps observations, but a different network has different CEs.
                if (!ReferenceEquals(colorNetwork, Trace.Network))
                {
                    colorNetwork = Trace.Network;
                    ComparatorStates = Array.AsReadOnly(new ComparatorState[Trace.Comparators.Count]);
                    ZoomFactor = 1;
                    OnPropertyChanged(nameof(CountsSummary));
                }
                observedFrame = 0;
            }
            else if (change.PropertyName == nameof(Position)) RecordComparisons();
            else if (change.PropertyName == nameof(ByStage)) observedFrame = 0;
        };
    }
    public IReadOnlyList<CatalogNetwork> Networks { get; }
    public new CatalogNetwork SelectedNetwork { get => (CatalogNetwork)base.SelectedNetwork; set => base.SelectedNetwork = value; }
    public string CountsSummary => $"CEs: {SelectedNetwork.ComparatorCount} · Stages: {SelectedNetwork.StageCount}";
    public RelayCommand NewInputCommand => ShuffleCommand;
    public RelayCommand ResetColorsCommand { get; }
    public IReadOnlyList<ComparatorState> ComparatorStates
    {
        get => comparatorStates;
        private set => SetProperty(ref comparatorStates, value);
    }

    // Zoom is relative to the available space, so its initial position always fits.
    public double ZoomFactor
    {
        get => zoomFactor;
        set
        {
            if (SetProperty(ref zoomFactor, double.IsFinite(value) ? Math.Clamp(value, 1, 3) : 1))
                OnPropertyChanged(nameof(ZoomLabel));
        }
    }
    public string ZoomLabel => ZoomFactor == 1 ? "Fit" : $"{ZoomFactor:0.#}×";

    private void ResetColors()
    {
        ComparatorStates = Array.AsReadOnly(new ComparatorState[Trace.Comparators.Count]);
        observedFrame = Trace.FrameIndex((int)Math.Floor(Position), ByStage);
    }

    private void RecordComparisons()
    {
        var frame = Trace.FrameIndex((int)Math.Floor(Position), ByStage);
        if (frame > observedFrame)
        {
            var next = ComparatorStates.ToArray();
            for (var index = observedFrame; index < frame; index++)
                next[index] = Trace.SwapCounts[index] > 0 ? ComparatorState.Swapped
                    : next[index] == ComparatorState.Swapped ? ComparatorState.Swapped : ComparatorState.Compared;
            ComparatorStates = Array.AsReadOnly(next);
        }
        observedFrame = frame;
    }
}
