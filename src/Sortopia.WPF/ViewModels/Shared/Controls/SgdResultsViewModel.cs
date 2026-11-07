using System.Windows;
using System.Windows.Media;
using Sortopia.Models.Sorting;
using Sortopia.Sorting.Catalog;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;
using Sortopia.WPF.Rendering;

namespace Sortopia.WPF.ViewModels.Shared.Controls;

/// <summary>Shared results-control state for the Sampling and Evolution subpages.</summary>
public abstract class SgdResultsViewModel : ViewModelBase
{
    private EvolutionVisits visits = EvolutionVisits.Empty;
    private EvolutionVisits uniqueSortersByPoint = EvolutionVisits.Empty;
    private long snapshotOrder;
    private NetworkCheckResult? lastPlottedUsage;
    private bool showVisitStats;
    private bool showStageStats;
    private int displayMode = 3;
    private string status = "";

    protected SgdResultsViewModel(string firstName, string secondName, bool secondIsChild = false)
    {
        FirstGroup = new(firstName);
        SecondGroup = new(secondName, isChild: secondIsChild);
        SelectGraphPointCommand = new RelayCommand(value => { if (value is Point point) ShowGraphNetwork(point); });
        ClearGraphCommand = new RelayCommand(() =>
        {
            foreach (var buffer in GraphBuffers) buffer.Clear();
            NetworkHistory.Replace(Array.Empty<NetworkHistoryEntry>());
            RefreshGraph();
            OnBuffersCleared();
        }, () => GraphBuffers.Any(buffer => buffer.TotalVisits > 0) || NetworkHistory.Entries.Count > 0);
        ClearGroupCommand = new RelayCommand(value =>
        {
            if (value is not GraphBufferGroup group || (group != FirstGroup && group != SecondGroup)) return;
            group.Success.Clear();
            group.Failure.Clear();
            var remaining = group == FirstGroup ? SecondGroup : FirstGroup;
            NetworkHistory.RemoveGroup(group, FollowGroupHistory ? remaining.Latest : null, clearSelection: FollowGroupHistory);
            RefreshGraph();
            RefreshGraphSelection();
            ClearGraphCommand.RaiseCanExecuteChanged();
            OnBuffersCleared();
        });
        foreach (var buffer in GraphBuffers)
            buffer.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(GraphNetworkBuffer.Bounds)) OnPropertyChanged(nameof(AllBufferBounds));
                if (change.PropertyName != nameof(GraphNetworkBuffer.IsVisible)) return;
                OnPropertyChanged(nameof(PlotFailures));
                OnPropertyChanged(nameof(PlotChildren));
                RefreshGraph();
                RefreshGraphSelection();
            };
        NetworkHistory.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(NetworkHistory.Current)) NotifyNetworkDisplay();
        };
        SelectedAnnotation.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(SorterAnnotation.VisiblePosition)) OnPropertyChanged(nameof(GraphSelectionPosition));
        };
    }

    protected virtual bool FollowGroupHistory => false;
    private protected virtual SearchCandidate? DisplayFallback => null;
    protected virtual void OnSelectionChanged() { }
    protected virtual void OnBuffersCleared() { }
    public abstract bool ShowResultPanes { get; }
    public GraphBufferGroup FirstGroup { get; }
    public GraphBufferGroup SecondGroup { get; }
    private GraphNetworkBuffer[] GraphBuffers => [FirstGroup.Success, FirstGroup.Failure, SecondGroup.Success, SecondGroup.Failure];
    public Rect AllBufferBounds
    {
        get
        {
            var bounds = Rect.Empty;
            foreach (var buffer in GraphBuffers) bounds.Union(buffer.Bounds);
            return bounds;
        }
    }
    public bool PlotFailures
    {
        get => FirstGroup.Failure.IsVisible && SecondGroup.Failure.IsVisible;
        set { FirstGroup.Failure.IsVisible = value; SecondGroup.Failure.IsVisible = value; }
    }
    public bool PlotChildren
    {
        get => SecondGroup.Success.IsVisible && SecondGroup.Failure.IsVisible;
        set { SecondGroup.Success.IsVisible = value; SecondGroup.Failure.IsVisible = value; }
    }
    public bool ShowVisitStats
    {
        get => showVisitStats;
        set
        {
            if (!SetProperty(ref showVisitStats, value)) return;
            OnPropertyChanged(nameof(PlotData));
            OnPropertyChanged(nameof(PlotLegendTitle));
        }
    }
    public bool ShowStageStats { get => showStageStats; set => SetProperty(ref showStageStats, value); }
    public int DisplayMode { get => displayMode; set => SetProperty(ref displayMode, Math.Clamp(value, 0, 3)); }
    public EvolutionVisits PlotData => ShowVisitStats ? Visits : uniqueSortersByPoint;
    public string PlotLegendTitle => ShowVisitStats ? "Visits per point (log scale)" : "Unique sorters per point (log scale)";
    public int? PlottedFailures => lastPlottedUsage?.Failed;
    public string Status { get => status; protected set => SetProperty(ref status, value); }
    public NetworkHistoryViewModel NetworkHistory { get; } = new();
    private SearchCandidate? Fallback => NetworkHistory.GraphPosition.HasValue ? null : DisplayFallback;
    public NetworkDefinition? Network => NetworkHistory.Current?.Network ?? Fallback?.RawNetwork;
    public NetworkCheckResult? DisplayUsage => NetworkHistory.Current is { } entry ? entry.Usage : Fallback?.RawUsage;
    public Point? SelectedNetworkPosition => NetworkHistory.Current is { } entry
        ? entry.Position ?? new Point(entry.Network.ComparatorCount, entry.Network.StageCount) : Fallback?.Position;
    public Point? GraphSelectionPosition => NetworkHistory.GraphPosition is { } point && NetworkHistory.Current is null
        ? point : SelectedAnnotation.VisiblePosition;
    public string DisplayedNetworkDescription => NetworkHistory.Current?.Description ?? "";
    public RelayCommand SelectGraphPointCommand { get; }
    public RelayCommand ClearGraphCommand { get; }
    public RelayCommand ClearGroupCommand { get; }
    public EvolutionVisits Visits
    {
        get => visits;
        private set
        {
            SetProperty(ref visits, value);
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(PlottedFailures));
            ClearGraphCommand.RaiseCanExecuteChanged();
        }
    }
    public IReadOnlyList<UsageBand> UsageBands => UsagePalette.Bands;
    public SorterAnnotation BestAnnotation { get; } = new("Best", Brushes.LimeGreen);
    public SorterAnnotation CurrentAnnotation { get; } = new("Current", Brushes.HotPink);
    public SorterAnnotation SelectedAnnotation { get; } = new("Selected", Brushes.White);
    private readonly Dictionary<sorterLibVariant, SorterAnnotation> catalogAnnotations = [];
    public IReadOnlyList<SorterAnnotation> Annotations { get; private set; } = [];
    public IReadOnlyList<CatalogNetwork> BestKnownNetworks { get; private set; } = [];
    public IReadOnlyList<CatalogNetwork> VisibleCatalogNetworks { get; private set; } = [];
    public Point? BestPosition => BestAnnotation.Position;
    public string Summary
    {
        get
        {
            if (lastPlottedUsage is not { } usage) return "";
            var inputs = usage.Exhaustive ? $"all {usage.Tested:N0} {usage.InputKind}" : $"{usage.Tested:N0} sampled {usage.InputKind}";
            return $"{usage.Failed:N0} failures across {inputs}";
        }
    }

    public void ShowGraphNetwork(Point position)
    {
        var selected = FirstGroup.LatestVisibleAt(position) ?? SecondGroup.LatestVisibleAt(position);
        if (selected is null)
        {
            var known = VisibleCatalogNetworks.LastOrDefault(network => network.Position == position);
            if (known is not null) selected = new(known, null, known.BestKnownTitle, position);
        }
        NetworkHistory.Inspect(position, selected);
    }

    private void RefreshGraphSelection()
    {
        if (NetworkHistory.GraphPosition is { } point) ShowGraphNetwork(point);
    }

    protected void NotifyNetworkDisplay()
    {
        SelectedAnnotation.Position = SelectedNetworkPosition;
        OnPropertyChanged(nameof(Network));
        OnPropertyChanged(nameof(DisplayUsage));
        OnPropertyChanged(nameof(DisplayedNetworkDescription));
        OnPropertyChanged(nameof(SelectedNetworkPosition));
        OnPropertyChanged(nameof(GraphSelectionPosition));
        OnSelectionChanged();
    }

    protected void UpdateBestKnownNetworks(int width)
    {
        BestKnownNetworks = CatalogNetwork.LoadForWidth(width);
        var items = new List<SorterAnnotation> { BestAnnotation, CurrentAnnotation, SelectedAnnotation };
        foreach (var network in BestKnownNetworks)
        {
            var variant = network.Id.SorterVariant;
            if (!catalogAnnotations.TryGetValue(variant, out var annotation))
            {
                annotation = new(network.VariantName, network.VariantBrush);
                annotation.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SorterAnnotation.IsShown)) UpdateVisibleCatalogNetworks();
                };
                catalogAnnotations.Add(variant, annotation);
            }
            annotation.Position = network.Position;
            items.Add(annotation);
        }
        Annotations = items;
        OnPropertyChanged(nameof(Annotations));
        OnPropertyChanged(nameof(BestKnownNetworks));
        UpdateVisibleCatalogNetworks();
    }

    private void UpdateVisibleCatalogNetworks()
    {
        VisibleCatalogNetworks = BestKnownNetworks.Where(n => catalogAnnotations[n.Id.SorterVariant].IsShown).ToArray();
        OnPropertyChanged(nameof(VisibleCatalogNetworks));
        RefreshGraphSelection();
    }

    private protected void RecordSnapshot(SearchCandidate candidate, GraphBufferGroup group, NetworkHistoryEntry entry)
    {
        var buffer = group.For(candidate.Score);
        var added = buffer.Record(candidate.Score, ++snapshotOrder, entry, candidate.Fingerprint);
        if (buffer.IsVisible && (group == FirstGroup || group == SecondGroup))
        {
            var point = candidate.Position;
            var newToPlot = added && !GraphBuffers.Any(other => other != buffer && other.IsVisible && other.Contains(point, candidate.Fingerprint));
            uniqueSortersByPoint = newToPlot ? uniqueSortersByPoint.Record(point) : uniqueSortersByPoint.WithCurrent(point);
            RefreshGraph(Visits.Record(point));
            if (NetworkHistory.GraphPosition == point && NetworkHistory.Current is null) ShowGraphNetwork(point);
        }
        ClearGraphCommand.RaiseCanExecuteChanged();
    }

    private void RefreshGraph(EvolutionVisits? updatedVisits = null)
    {
        var visible = GraphBuffers.Where(buffer => buffer.IsVisible).OrderBy(buffer => buffer.LastOrder).ToArray();
        lastPlottedUsage = visible.LastOrDefault()?.Latest?.Usage;
        SorterScore? bestPlotted = null;
        foreach (var buffer in visible)
            if (buffer.BestScore is { } score && (bestPlotted is not { } best || score.Improves(best))) bestPlotted = score;
        if (updatedVisits is null)
        {
            updatedVisits = EvolutionVisits.Merge(visible.Select(buffer => buffer.Visits));
            uniqueSortersByPoint = updatedVisits.Recount(point => GraphNetworkBuffer.CountDistinctAt(point, visible));
        }
        BestAnnotation.Position = bestPlotted?.Position;
        CurrentAnnotation.Position = updatedVisits.Current;
        Visits = updatedVisits;
        OnPropertyChanged(nameof(PlotData));
        OnPropertyChanged(nameof(BestPosition));
    }
}
