using System.Windows;
using Sortopia.WPF.ViewModels;

namespace Sortopia.WPF.Models;

/// <summary>Running size measurements for one graph axis.</summary>
public readonly struct BufferStatistics
{
    private readonly double sumSquaredDeviations;
    public int Min { get; }
    public int Max { get; }
    public double Mean { get; }
    public double Sd { get; }

    internal BufferStatistics(BufferStatistics? previous, int value, long count)
    {
        Min = Math.Min(previous?.Min ?? value, value);
        Max = Math.Max(previous?.Max ?? value, value);
        // Welford's update, using the full buffer population for the standard deviation.
        var oldMean = previous?.Mean ?? value;
        var delta = value - oldMean;
        Mean = oldMean + delta / count;
        sumSquaredDeviations = (previous?.sumSquaredDeviations ?? 0) + delta * (value - Mean);
        Sd = Math.Sqrt(sumSquaredDeviations / count);
    }
}

public sealed class BufferPopulation : ViewModelBase
{
    public long Count { get; private set; }
    public BufferStatistics? Ces { get; private set; }
    public BufferStatistics? Stages { get; private set; }

    internal void Record(SorterScore score)
    {
        Count++;
        Ces = new BufferStatistics(Ces, score.ComparatorCount, Count);
        Stages = new BufferStatistics(Stages, score.StageCount, Count);
        OnPropertyChanged(string.Empty);
    }

    internal void Clear()
    {
        Count = 0;
        Ces = Stages = null;
        OnPropertyChanged(string.Empty);
    }
}

public sealed class GraphBufferGroup(string name, bool isChild = false)
{
    public string Name { get; } = name;
    public GraphNetworkBuffer Success { get; } = new(isChild, true);
    public GraphNetworkBuffer Failure { get; } = new(isChild, false);
    internal NetworkHistoryEntry? Latest => Success.LastOrder > Failure.LastOrder ? Success.Latest : Failure.Latest;
    internal GraphNetworkBuffer For(SorterScore score) => score.Failures == 0 ? Success : Failure;

    internal NetworkHistoryEntry? LatestVisibleAt(Point position)
    {
        var success = Success.IsVisible ? Success.Find(position) : null;
        var failure = Failure.IsVisible ? Failure.Find(position) : null;
        if (success is null) return failure?.Entry;
        if (failure is null || success.Value.Order > failure.Value.Order) return success.Value.Entry;
        return failure.Value.Entry;
    }
}

/// <summary>Retains one latest snapshot per graph coordinate for a single outcome class.</summary>
public sealed class GraphNetworkBuffer : ViewModelBase
{
    public GraphNetworkBuffer(bool isChild = false, bool successful = true)
    {
        IsChild = isChild;
        Successful = successful;
    }
    public bool IsChild { get; }
    public bool Successful { get; }
    private bool isVisible = true;
    public bool IsVisible { get => isVisible; set => SetProperty(ref isVisible, value); }
    public BufferPopulation VisitStats { get; } = new();
    public BufferPopulation UniqueStats { get; } = new();
    public long TotalVisits => VisitStats.Count;
    public int UniqueCount => checked((int)UniqueStats.Count);
    internal long LastOrder { get; private set; }
    internal NetworkHistoryEntry? Latest { get; private set; }
    internal SorterScore? BestScore { get; private set; }
    private sealed class Cell
    {
        internal readonly HashSet<SorterFingerprint> Fingerprints = [];
        internal (long Order, NetworkHistoryEntry Entry) Snapshot;
    }
    private readonly Dictionary<Point, Cell> cells = [];
    internal int Count => cells.Count;
    internal EvolutionVisits Visits { get; private set; } = EvolutionVisits.Empty;
    internal Rect Bounds => Visits.Bounds;
    internal bool Record(SorterScore score, long order, NetworkHistoryEntry entry, SorterFingerprint fingerprint)
    {
        if (!cells.TryGetValue(score.Position, out var cell))
        {
            cell = new Cell();
            cells.Add(score.Position, cell);
        }
        cell.Snapshot = (order, entry);
        Latest = entry;
        var unique = cell.Fingerprints.Add(fingerprint);
        var previousBounds = Bounds;
        Visits = Visits.Record(score.Position);
        LastOrder = order;
        if (score.Failures == 0 && (BestScore is not { } best || score.Improves(best))) BestScore = score;
        VisitStats.Record(score);
        if (unique)
        {
            UniqueStats.Record(score);
            OnPropertyChanged(nameof(UniqueCount));
        }
        if (Bounds != previousBounds) OnPropertyChanged(nameof(Bounds));
        OnPropertyChanged(nameof(TotalVisits));
        return unique;
    }
    internal (long Order, NetworkHistoryEntry Entry)? Find(Point position) =>
        cells.TryGetValue(position, out var cell) ? cell.Snapshot : null;
    internal bool Contains(Point position, SorterFingerprint fingerprint) =>
        cells.TryGetValue(position, out var cell) && cell.Fingerprints.Contains(fingerprint);

    internal static long CountDistinctAt(Point point, GraphNetworkBuffer[] buffers)
    {
        var sets = buffers.Select(b => b.cells.GetValueOrDefault(point)?.Fingerprints)
            .OfType<HashSet<SorterFingerprint>>().OrderByDescending(set => set.Count).ToArray();
        if (sets.Length == 0) return 0;
        // Start with the largest set. Only the smaller sets need cross-buffer membership checks.
        long count = sets[0].Count;
        for (var i = 1; i < sets.Length; i++)
            foreach (var fingerprint in sets[i])
            {
                var prior = 0;
                while (prior < i && !sets[prior].Contains(fingerprint)) prior++;
                if (prior == i) count++;
            }
        return count;
    }
    internal void Clear()
    {
        cells.Clear();
        UniqueStats.Clear();
        OnPropertyChanged(nameof(UniqueCount));
        Visits = EvolutionVisits.Empty;
        OnPropertyChanged(nameof(Bounds));
        LastOrder = 0;
        Latest = null;
        BestScore = null;
        VisitStats.Clear();
        OnPropertyChanged(nameof(TotalVisits));
    }
}
