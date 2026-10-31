using Sortopia.Sorting.Inputs;

namespace Sortopia.WPF.Models;

public sealed record ComparatorEndpoints(int Low, int High);

/// <summary>Immutable playback data. Building a trace is the only place the UI requests sorting.</summary>
public sealed class NetworkTrace
{
    public const int DisplayCeLimit = 4096;
    public const int DisplayStageLimit = 512;
    public bool IsPreview { get; }
    public int DisplayStageCount => StageBoundaries.Count - 1;
    public static NetworkTrace CreateForPlayback(NetworkDefinition network, int[] input) =>
        new(network, input, network.ComparatorCount > DisplayCeLimit || network.StageCount > DisplayStageLimit);

    private NetworkTrace(NetworkDefinition network, int[] input, bool preview = false)
    {
        Network = network;
        IsPreview = preview;
        var stageCount = 0;
        var ceCount = 0;
        foreach (var length in network.StageLengths)
        {
            if (preview && (stageCount >= DisplayStageLimit || ceCount + length > DisplayCeLimit)) break;
            ceCount += length;
            stageCount++;
        }
        var displayedCes = network.Comparators.Take(ceCount).ToArray();
        var sortable = sortableIntArray.create(input, network.Width, network.Width);
        var uses = new int[ceCount];
        var history = sortable.SortByCesWithHistory(displayedCes, uses);
        Comparators = Array.AsReadOnly(displayedCes.Select(ce => new ComparatorEndpoints(ce.LowChannel, ce.HighChannel)).ToArray());
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
        var boundaries = new int[stageCount + 1];
        for (var stage = 0; stage < stageCount; stage++)
            boundaries[stage + 1] = boundaries[stage] + network.StageLengths[stage];
        StageBoundaries = Array.AsReadOnly(boundaries);
    }

    public NetworkDefinition Network { get; }
    public IReadOnlyList<ComparatorEndpoints> Comparators { get; }
    public IReadOnlyList<int> SwapCounts { get; }
    public IReadOnlyList<IReadOnlyList<int>> Values { get; }
    public IReadOnlyList<IReadOnlyList<int>> TokenChannels { get; }
    public IReadOnlyList<int> StageBoundaries { get; }

    public int StepCount(bool byStage) => byStage ? DisplayStageCount : Comparators.Count;
    public int FrameIndex(int step, bool byStage) => byStage ? StageBoundaries[Math.Clamp(step, 0, DisplayStageCount)] : Math.Clamp(step, 0, Comparators.Count);

    public static NetworkTrace Create(NetworkDefinition network, int[] input)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(input);
        return new NetworkTrace(network, input);
    }
}
