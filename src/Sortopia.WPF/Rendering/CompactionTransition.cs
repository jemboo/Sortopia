using Sortopia.WPF.Models;

namespace Sortopia.WPF.Rendering;

public sealed record CompactionMove(int Low, int High, double FromX, double ToX,
    bool WasVisible, bool IsVisible, int Uses)
{
    public double X(double progress) => FromX + (ToX - FromX) * progress;
    public double Opacity(double progress) => WasVisible ? IsVisible ? 1 : 1 - progress : progress;
}

/// <summary>Bounded visual transition; repeated CEs match by occurrence, not just endpoints.</summary>
public sealed class CompactionTransition
{
    public CompactionTransition(NetworkTrace before, NetworkTrace after,
        NetworkCheckResult? beforeUsage, NetworkCheckResult? afterUsage)
    {
        Before = new NetworkLayout(before);
        After = new NetworkLayout(after);
        BeforeTrace = before;
        AfterTrace = after;
        Tested = afterUsage?.Tested ?? 0;
        var oldPositions = Index(Before);
        var newPositions = Index(After);
        var moves = new List<CompactionMove>();
        foreach (var (key, oldCe) in oldPositions)
        {
            var found = newPositions.TryGetValue(key, out var newCe);
            moves.Add(new(oldCe.Low, oldCe.High, oldCe.X, newCe?.X ?? After.CutoffX - 20,
                true, found, found ? afterUsage?.CeUses[newCe!.Index] ?? 0 : beforeUsage?.CeUses[oldCe.Index] ?? 0));
        }
        foreach (var (key, newCe) in newPositions)
            if (!oldPositions.ContainsKey(key))
                moves.Add(new(newCe.Low, newCe.High, Before.CutoffX - 20, newCe.X,
                    false, true, afterUsage?.CeUses[newCe.Index] ?? 0));
        Moves = moves.AsReadOnly();
    }

    private static Dictionary<(int Low, int High, int Occurrence), ComparatorPosition> Index(NetworkLayout layout)
    {
        var occurrences = new Dictionary<(int, int), int>();
        var positions = new Dictionary<(int, int, int), ComparatorPosition>();
        foreach (var ce in layout.Comparators)
        {
            var key = (ce.Low, ce.High);
            var occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            positions.Add((ce.Low, ce.High, occurrence), ce);
        }
        return positions;
    }

    public NetworkLayout Before { get; }
    public NetworkLayout After { get; }
    public NetworkTrace BeforeTrace { get; }
    public NetworkTrace AfterTrace { get; }
    public IReadOnlyList<CompactionMove> Moves { get; }
    public int Tested { get; }
    public double Width => Math.Max(Before.Width, After.Width);
}
