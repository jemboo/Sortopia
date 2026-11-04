using System.Collections.Immutable;
using System.Windows;

namespace Sortopia.WPF.Models;

/// <summary>Point frequencies and the latest plotted position for compacted sorter sizes.</summary>
public sealed class EvolutionVisits
{
    public static EvolutionVisits Empty { get; } = new(ImmutableDictionary<Point, long>.Empty, null, 0, Rect.Empty);

    private long? minimum;

    private EvolutionVisits(ImmutableDictionary<Point, long> counts, Point? current, long maximum, Rect bounds, long? knownMinimum = null)
    {
        Counts = counts;
        Current = current;
        Maximum = maximum;
        minimum = counts.Count <= 1 ? maximum : knownMinimum;
        Bounds = bounds;
    }

    public ImmutableDictionary<Point, long> Counts { get; }
    public Point? Current { get; }
    public long Maximum { get; }
    public long Minimum => minimum ??= Counts.Values.Min();
    public bool HasColorRange => Minimum < Maximum;
    public Rect Bounds { get; }

    internal EvolutionVisits Recount(Func<Point, long> countAt)
    {
        if (Counts.IsEmpty) return Empty;
        var counts = Counts.Keys.ToImmutableDictionary(point => point, countAt);
        return new(counts, Current, counts.Values.Max(), Bounds);
    }

    internal EvolutionVisits WithCurrent(Point position) =>
        Current == position ? this : new(Counts, position, Maximum, Bounds, minimum);

    internal static EvolutionVisits Merge(IEnumerable<EvolutionVisits> sources)
    {
        var counts = ImmutableDictionary.CreateBuilder<Point, long>();
        var bounds = Rect.Empty;
        Point? current = null;
        foreach (var source in sources)
        {
            foreach (var (point, count) in source.Counts) counts[point] = counts.GetValueOrDefault(point) + count;
            if (!source.Bounds.IsEmpty) bounds.Union(source.Bounds);
            if (source.Current is { } latest) current = latest;
        }
        return new(counts.ToImmutable(), current, counts.Count == 0 ? 0 : counts.Values.Max(), bounds);
    }

    public EvolutionVisits Record(Point position)
    {
        var count = Counts.GetValueOrDefault(position) + 1;
        var bounds = Bounds;
        bounds.Union(position);
        var knownMinimum = count - 1 == minimum ? null : minimum;
        if (count == 1) knownMinimum = 1;
        return new(Counts.SetItem(position, count), position, Math.Max(Maximum, count), bounds, knownMinimum);
    }
}
