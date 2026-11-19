using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Sortopia.Evaluation;
using Sortopia.Sorting.Inputs;
using Sortopia.Sorting.Networks;

namespace Sortopia.WPF.Models;

public sealed record NetworkCheckResult(int Tested, bool Exhaustive, int[]? Counterexample,
    int Failed, IReadOnlyList<int> CeUses)
{
    public IReadOnlyList<bool> FixedCes { get; init; } = Array.Empty<bool>();
    public bool IsFixedCe(int index) => FixedCes.Count == CeUses.Count && FixedCes[index];
    public string Coverage => Exhaustive ? "Exhaustive" : "Sampled";
    public string Description => Failed > 0
        ? $"Failed: {Failed:N0} of {Tested:N0} {(Exhaustive ? "all" : "sampled")} binary inputs were unsorted."
            + (Counterexample is null ? "" : " The first failing input is loaded below.")
        : Exhaustive ? $"Verified: sorted all {Tested:N0} binary inputs (0–1 rule)."
        : $"Passed {Tested:N0} sampled binary inputs. This is not a proof that the network sorts every input.";
    public string UsageSummary => $"CE usage: {CeUses.Count(n => n > 0):N0} of {CeUses.Count:N0} CEs swapped values across {Tested:N0} inputs. Hover over a CE for its count and percentage.";
}

/// <summary>Prepares reusable binary inputs and evaluates complete sorters, optionally retaining the first failing input.</summary>
public sealed class NetworkCheck
{
    public const int MaxExhaustiveWidth = 20;
    public const int MaxSamples = 1_000_000;
    internal const int SampleSeed = 8675309;

    private readonly int width;
    private readonly int? samples;
    private readonly ulong[] inputs;
    private readonly sortableBitv512Tests? simdInputs;

    public int Count => inputs.Length;

    // null means all combinations; a count means random draws with replacement.
    public NetworkCheck(int width, int? samples, bool useSimd, CancellationToken token)
    {
        if (width is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(width));
        if (samples is null && width > MaxExhaustiveWidth)
            throw new ArgumentOutOfRangeException(nameof(samples), $"All inputs is available only through {MaxExhaustiveWidth} channels.");
        if (samples is < 1 or > MaxSamples) throw new ArgumentOutOfRangeException(nameof(samples));
        token.ThrowIfCancellationRequested();
        this.width = width;
        this.samples = samples;
        inputs = new ulong[samples ?? (1 << width)];
        var random = samples is null ? null : new Random(SampleSeed);
        for (var index = 0; index < inputs.Length; index++)
        {
            if ((index & 511) == 0) token.ThrowIfCancellationRequested();
            ulong value = 0;
            for (var wire = 0; wire < width; wire++)
                value |= (ulong)(random is null ? (index >> wire) & 1 : random.Next(2)) << wire;
            inputs[index] = value;
        }
        if (useSimd) simdInputs = Pack(token);
        token.ThrowIfCancellationRequested();
    }

    internal bool Matches(int channels, int? sampleCount) => width == channels && samples == sampleCount;

    private sortableBitv512Tests Pack(CancellationToken token)
    {
        var blocks = new sortBlockBitv512[(inputs.Length + 511) / 512];
        var vectors = new Vector512<ulong>[width];
        var words = MemoryMarshal.Cast<Vector512<ulong>, ulong>(vectors.AsSpan());
        for (var block = 0; block < blocks.Length; block++)
        {
            token.ThrowIfCancellationRequested();
            words.Clear();
            var lanes = Math.Min(512, inputs.Length - block * 512);
            for (var lane = 0; lane < lanes; lane++)
            {
                var value = inputs[block * 512 + lane];
                for (var wire = 0; wire < width; wire++)
                    words[wire * 8 + lane / 64] |= ((value >> wire) & 1UL) << (lane % 64);
            }
            blocks[block] = sortBlockBitv512.createFromVectors(vectors, lanes);
        }
        return sortableBitv512Tests.create(Guid.NewGuid(), width, blocks);
    }

    private int[] Decode(ulong value)
    {
        var input = new int[width];
        for (var wire = 0; wire < width; wire++) input[wire] = (int)((value >> wire) & 1UL);
        return input;
    }

    // Packed scalar inputs keep the cache small even for one million 64-channel samples.
    private static bool Apply(ref ulong value, ce comparator)
    {
        var low = 1UL << comparator.LowChannel;
        var high = 1UL << comparator.HighChannel;
        if ((value & low) == 0 || (value & high) != 0) return false;
        value ^= low | high;
        return true;
    }

    public NetworkCheckResult Evaluate(sorter network, CancellationToken token, bool includeCounterexample = true,
        IProgress<int>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        if (network.ChannelCount != width) throw new ArgumentException("Sorter width does not match the prepared inputs.", nameof(network));
        var ces = network.Ces;
        int[]? counterexample = null;
        int failed;
        int[] uses;
        if (simdInputs is { } packedInputs)
        {
            var block = ceBlock.create(network.SorterId, width, ces);
            ceBlockEval result;
            if (includeCounterexample)
            {
                var checkedNetwork = CeBlockOpsBitv512.evalWithCounterexample(packedInputs, block, token);
                result = checkedNetwork.Item1;
                counterexample = checkedNetwork.Item2?.Value;
            }
            else result = CeBlockOpsBitv512.evalCounts(packedInputs, block, token);
            failed = result.UnsortedCount;
            uses = result.UseCountArray;
            progress?.Report(Count);
        }
        else
        {
            failed = 0;
            uses = new int[ces.Length];
            var adjacentMask = (1UL << (width - 1)) - 1;
            bool IsSorted(ulong value) => (value & ~(value >> 1) & adjacentMask) == 0;
            for (var index = 0; index < inputs.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var working = inputs[index];
                for (var ceIndex = 0; ceIndex < ces.Length; ceIndex++)
                {
                    if ((ceIndex & 255) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (IsSorted(working)) break;
                    }
                    if (Apply(ref working, ces[ceIndex])) uses[ceIndex]++;
                }
                if (!IsSorted(working))
                {
                    failed++;
                    if (includeCounterexample) counterexample ??= Decode(inputs[index]);
                }
                if ((index + 1) % 1024 == 0 || index + 1 == Count) progress?.Report(index + 1);
            }
        }
        token.ThrowIfCancellationRequested();
        return new(Count, samples is null, counterexample, failed, Array.AsReadOnly(uses));
    }
}
