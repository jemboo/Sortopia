using System.Collections.Immutable;
using System.Windows;

namespace Sortopia.WPF.Models;

/// <summary>Visit counts for compacted sorter sizes since the graph was cleared.</summary>
public sealed class EvolutionVisits
{
    public static EvolutionVisits Empty { get; } = new(ImmutableDictionary<Point, long>.Empty, null, 0, Rect.Empty);

    private EvolutionVisits(ImmutableDictionary<Point, long> counts, Point? current, long maximum, Rect bounds)
    {
        Counts = counts;
        Current = current;
        Maximum = maximum;
        Bounds = bounds;
    }

    public ImmutableDictionary<Point, long> Counts { get; }
    public Point? Current { get; }
    public long Maximum { get; }
    public Rect Bounds { get; }

    public EvolutionVisits Record(Point position)
    {
        var count = Counts.GetValueOrDefault(position) + 1;
        var bounds = Bounds;
        bounds.Union(position);
        return new(Counts.SetItem(position, count), position, Math.Max(Maximum, count), bounds);
    }
}
