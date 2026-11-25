using System.Windows;
using System.Windows.Threading;
using Sortopia.Evaluation.Sgd;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.ViewModels.Shared.Controls;

public sealed record HistogramPoolOption(int? Index, string Label);

/// <summary>Frames from one saved summary batch, with explicit population and pool selection.</summary>
public sealed class SorterBinMovieViewModel : ViewModelBase, IDisposable
{
    private sealed record Frame(EvolutionVisits Visits, int PoolCount, int MissingPools, long Members);
    private readonly IGrouping<int, sgdPoolStatistics>[] generations;
    private Frame[] frames = [];
    private string selectedHistogram = "Successful";
    private HistogramPoolOption selectedPool;
    private int selectedFrameIndex;
    private Point? selectedPosition;
    private bool isPlaying, disposed;
    private DispatcherTimer? timer;

    public SorterBinMovieViewModel(sorterPoolSummarySet summary)
    {
        SorterPoolSummarySet.validate(summary);
        generations = summary.Rows.GroupBy(row => row.Generation).OrderBy(group => group.Key).ToArray();
        Generations = generations.Select(group => group.Key).ToArray();
        PoolOptions = new[] { new HistogramPoolOption(null, "All pools") }.Concat(summary.Rows.Select(row => row.PoolIndex)
            .Distinct().Order().Select(index => new HistogramPoolOption(index, $"Pool {index}"))).ToArray();
        selectedPool = PoolOptions[0];
        var bounds = Rect.Empty;
        foreach (var row in summary.Rows)
            foreach (var histogram in new[] { row.SuccessfulLengthHistogram, row.UnsuccessfulLengthHistogram })
                if (histogram is not null)
                    foreach (var bin in histogram.Value) bounds.Union(new Point(bin.StageLength, bin.CeLength));
        AllBounds = bounds;
        PreviousCommand = new RelayCommand(() => SelectedFrameIndex--, () => !disposed && SelectedFrameIndex > 0);
        NextCommand = new RelayCommand(() => SelectedFrameIndex++, () => !disposed && SelectedFrameIndex < LastFrameIndex);
        PlayPauseCommand = new RelayCommand(TogglePlayback, () => !disposed && HasMultipleGenerations && frames.Any(frame => frame.MissingPools == 0 && frame.PoolCount > 0));
        SelectBinCommand = new RelayCommand(value =>
        {
            if (value is Point point)
            {
                SetProperty(ref selectedPosition, point, nameof(SelectedPosition));
                OnPropertyChanged(nameof(SelectionText));
            }
        }, () => !disposed && Current.MissingPools == 0 && Current.PoolCount > 0);
        RebuildFrames();
    }

    public IReadOnlyList<string> HistogramKinds { get; } = ["Successful", "Unsuccessful"];
    public IReadOnlyList<int> Generations { get; }
    public IReadOnlyList<HistogramPoolOption> PoolOptions { get; }
    public Rect AllBounds { get; }
    public long FrequencyMaximum { get; private set; }
    public string FrequencyMaximumLabel => FrequencyMaximum.ToString("N0");
    public int LastFrameIndex => Generations.Count - 1;
    public bool HasMultipleGenerations => Generations.Count > 1;
    public bool IsPlaying => isPlaying;
    public string PlayPauseLabel => IsPlaying ? "Pause" : "Play";
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand SelectBinCommand { get; }

    public string SelectedHistogram
    {
        get => selectedHistogram;
        set { if (HistogramKinds.Contains(value) && SetProperty(ref selectedHistogram, value)) { Pause(); RebuildFrames(); } }
    }
    public HistogramPoolOption SelectedPool
    {
        get => selectedPool;
        set { if (value is not null && PoolOptions.Contains(value) && SetProperty(ref selectedPool, value)) { Pause(); RebuildFrames(); } }
    }
    public int SelectedFrameIndex
    {
        get => selectedFrameIndex;
        set
        {
            var next = Math.Clamp(value, 0, LastFrameIndex);
            if (next == selectedFrameIndex) return;
            Pause(); SetFrame(next);
        }
    }
    public int SelectedGeneration
    {
        get => Generations[SelectedFrameIndex];
        set
        {
            var index = Array.FindIndex(generations, group => group.Key == value);
            if (index >= 0) SelectedFrameIndex = index;
        }
    }
    private Frame Current => frames[SelectedFrameIndex];
    public EvolutionVisits Visits => Current.Visits;
    public Point? SelectedPosition => selectedPosition;
    public string EmptyMessage => Current.MissingPools > 0 ? "Histograms unavailable."
        : Current.PoolCount == 0 ? "No sample for this pool." : $"No {SelectedHistogram.ToLowerInvariant()} sorters.";
    public string Status => Current.PoolCount == 0 ? $"Generation {SelectedGeneration:N0}: no saved sample for {SelectedPool.Label}."
        : Current.MissingPools > 0 ? $"Generation {SelectedGeneration:N0}: histograms were not recorded for {Current.MissingPools:N0} of {Current.PoolCount:N0} selected pools."
        : $"Generation {SelectedGeneration:N0} · {SelectedFrameIndex + 1:N0} of {Generations.Count:N0} saved frames · {Current.PoolCount:N0} pools · {Current.Members:N0} {SelectedHistogram.ToLowerInvariant()} sorters · {Visits.Counts.Count:N0} occupied bins";
    public string SelectionText => SelectedPosition is { } point
        ? $"Stage count {point.X:N0} · CE count {point.Y:N0} · {Visits.Counts.GetValueOrDefault(point):N0} sorters"
        : "Select a bin to see its count. Axes stay fixed across generations and populations.";

    private void RebuildFrames()
    {
        var successful = SelectedHistogram == "Successful";
        frames = generations.Select(group =>
        {
            var rows = group.Where(row => SelectedPool.Index is null || row.PoolIndex == SelectedPool.Index).ToArray();
            var histograms = rows.Select(row => successful ? row.SuccessfulLengthHistogram : row.UnsuccessfulLengthHistogram).ToArray();
            var missing = histograms.Count(histogram => histogram is null);
            // Missing historical data must not appear as a zero count or a partial population.
            var visits = missing > 0 ? EvolutionVisits.Empty : EvolutionVisits.FromCounts(histograms.SelectMany(histogram => histogram!.Value)
                .Select(bin => KeyValuePair.Create(new Point(bin.StageLength, bin.CeLength), (long)bin.Count)));
            return new Frame(visits, rows.Length, missing, rows.Sum(row => (long)(successful ? row.SuccessfulCount : row.MemberCount - row.SuccessfulCount)));
        }).ToArray();
        FrequencyMaximum = Math.Max(1, frames.Max(frame => frame.Visits.Maximum));
        OnPropertyChanged(nameof(FrequencyMaximum)); OnPropertyChanged(nameof(FrequencyMaximumLabel));
        NotifyFrame();
    }
    private void SetFrame(int index)
    {
        if (index == selectedFrameIndex) return;
        selectedFrameIndex = index;
        NotifyFrame();
    }
    private void NotifyFrame()
    {
        selectedPosition = null;
        foreach (var name in new[] { nameof(SelectedFrameIndex), nameof(SelectedGeneration), nameof(Visits), nameof(Status), nameof(EmptyMessage), nameof(SelectedPosition), nameof(SelectionText) })
            OnPropertyChanged(name);
        NotifyCommands();
    }
    private void NotifyCommands()
    {
        PreviousCommand.RaiseCanExecuteChanged(); NextCommand.RaiseCanExecuteChanged();
        PlayPauseCommand.RaiseCanExecuteChanged(); SelectBinCommand.RaiseCanExecuteChanged();
    }
    private void TogglePlayback()
    {
        if (IsPlaying) { Pause(); return; }
        if (SelectedFrameIndex == LastFrameIndex) SetFrame(0);
        if (timer is null)
        {
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += OnTick;
        }
        isPlaying = true;
        OnPropertyChanged(nameof(IsPlaying)); OnPropertyChanged(nameof(PlayPauseLabel));
        timer.Start();
    }
    private void OnTick(object? sender, EventArgs e) => AdvanceFrame();
    internal void AdvanceFrame()
    {
        if (!IsPlaying || disposed) return;
        if (SelectedFrameIndex < LastFrameIndex) SetFrame(SelectedFrameIndex + 1);
        if (SelectedFrameIndex == LastFrameIndex) Pause();
    }
    public void Pause()
    {
        timer?.Stop();
        if (!isPlaying) return;
        isPlaying = false;
        OnPropertyChanged(nameof(IsPlaying)); OnPropertyChanged(nameof(PlayPauseLabel));
    }
    public void Dispose()
    {
        Pause(); disposed = true;
        if (timer is not null) { timer.Tick -= OnTick; timer = null; }
        NotifyCommands();
    }
}
