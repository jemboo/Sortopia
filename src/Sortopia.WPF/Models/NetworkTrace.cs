using Sortopia.Sorting.Inputs;

namespace Sortopia.WPF.Models;

public sealed record ComparatorEndpoints(int Low, int High);

/// <summary>Immutable playback data. Building a trace is the only place the UI requests sorting.</summary>
public sealed class NetworkTrace
{
    private NetworkTrace(CatalogNetwork network, int[] input)
    {
        Network = network;
        var sortable = sortableIntArray.create(input, network.Width, network.Width);
        var uses = new int[network.ComparatorCount];
        var history = sortable.SortByCesWithHistory(network.Comparators, uses);
        Comparators = Array.AsReadOnly(network.Comparators.Select(ce => new ComparatorEndpoints(ce.LowChannel, ce.HighChannel)).ToArray());
        SwapCounts = Array.AsReadOnly(uses);
        Values = Array.AsReadOnly(history.Select(s => (IReadOnlyList<int>)Array.AsReadOnly(s.Values)).ToArray());

        // Track identity from recorded swaps, including when multiple tokens have equal values.
        // This is a projection of the trace; no values are compared or sorted here.
        var tokens = Enumerable.Range(0, network.Width).ToArray();
        var positions = new IReadOnlyList<int>[history.Length];
        positions[0] = Array.AsReadOnly((int[])tokens.Clone());
        for (var step = 0; step < Comparators.Count; step++)
        {
            var ce = Comparators[step];
            if (uses[step] != 0)
                (tokens[ce.Low], tokens[ce.High]) = (tokens[ce.High], tokens[ce.Low]);
            var channels = new int[network.Width];
            for (var channel = 0; channel < tokens.Length; channel++)
                channels[tokens[channel]] = channel;
            positions[step + 1] = Array.AsReadOnly(channels);
        }
        TokenChannels = Array.AsReadOnly(positions);
        var boundaries = new int[network.StageCount + 1];
        for (var stage = 0; stage < network.StageCount; stage++)
            boundaries[stage + 1] = boundaries[stage] + network.StageLengths[stage];
        StageBoundaries = Array.AsReadOnly(boundaries);
    }

    public CatalogNetwork Network { get; }
    public IReadOnlyList<ComparatorEndpoints> Comparators { get; }
    public IReadOnlyList<int> SwapCounts { get; }
    public IReadOnlyList<IReadOnlyList<int>> Values { get; }
    public IReadOnlyList<IReadOnlyList<int>> TokenChannels { get; }
    public IReadOnlyList<int> StageBoundaries { get; }

    public int StepCount(bool byStage) => byStage ? Network.StageCount : Comparators.Count;
    public int FrameIndex(int step, bool byStage) => byStage ? StageBoundaries[Math.Clamp(step, 0, Network.StageCount)] : Math.Clamp(step, 0, Comparators.Count);

    public static NetworkTrace Create(CatalogNetwork network, int[] input)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(input);
        return new NetworkTrace(network, input);
    }
}
