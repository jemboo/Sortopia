using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Sortopia.Evaluation;
using Sortopia.Models.Inputs;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Inputs;
using Sortopia.Sorting.Networks;

namespace Sortopia.WPF.Models;

/// <summary>One input context, reduced by its prefix once and reused throughout a sampling or evolution run.</summary>
internal sealed class PreparedNetworkCheck
{
    private readonly int width;
    private readonly int? samples;
    private readonly ce[] prefix;
    private readonly ulong[] presorted = [];
    private readonly sortableBitv512Tests? simdInputs;
    private readonly int inputCount;

    internal int Count => inputCount;

    internal PreparedNetworkCheck(int width, int? samples, ce[] prefix, bool useSimd, CancellationToken token)
    {
        if (width is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(width));
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Any(ce => ce.HighChannel >= width)) throw new ArgumentException("Prefix width does not match the inputs.", nameof(prefix));
        token.ThrowIfCancellationRequested();
        this.width = width;
        this.samples = samples;
        this.prefix = (ce[])prefix.Clone();
        var count = NetworkCheck.InputCount(width, samples);
        if (samples is null && prefix.Length > 0)
        {
            var model = FindPrefixModel();
            if (useSimd && model is { } simdModel)
            {
                simdInputs = simdModel.MakeSortableBitv512Test(Guid.NewGuid());
                inputCount = simdInputs.Value.SortableCount;
            }
            else
            {
                var rows = model is { } scalarModel
                    ? scalarModel.MakeSortableBoolTest(Guid.NewGuid()).SortableBinaryArrays
                    : SortableBoolArray.getAllPossibleResultsFromCeArray(this.prefix, width);
                presorted = rows.Select(row =>
                {
                    token.ThrowIfCancellationRequested();
                    ulong value = 0;
                    for (var wire = 0; wire < width; wire++) if (row[wire]) value |= 1UL << wire;
                    return value;
                }).ToArray();
                inputCount = presorted.Length;
                if (useSimd) simdInputs = Pack(token);
            }
            token.ThrowIfCancellationRequested();
            return;
        }

        presorted = new ulong[count];
        var distinct = prefix.Length == 0 ? null : new HashSet<ulong>();
        var retained = 0;
        var random = samples is null ? null : new Random(NetworkCheck.SampleSeed);
        var input = new int[width];
        for (var index = 0; index < count; index++)
        {
            if ((index & 511) == 0) token.ThrowIfCancellationRequested();
            NetworkCheck.FillInput(input, index, random);
            ulong value = 0;
            for (var wire = 0; wire < width; wire++) value |= (ulong)input[wire] << wire;
            for (var ceIndex = 0; ceIndex < this.prefix.Length; ceIndex++)
                Apply(ref value, this.prefix[ceIndex]);
            if (distinct is null || distinct.Add(value)) presorted[retained++] = value;
        }
        if (retained != count) Array.Resize(ref presorted, retained);
        inputCount = retained;
        if (useSimd) simdInputs = Pack(token);
    }

    private msasPfx? FindPrefixModel()
    {
        foreach (var id in PrefixLib.ids().Where(id => id.ChannelCount == width))
            if (SorterDataParse.getCeArrayFromPrefixLib(id).Value.SequenceEqual(prefix)) return msasPfx.create(id);
        return null;
    }

    internal bool Matches(int channels, int? sampleCount, ce[] expectedPrefix) =>
        width == channels && samples == sampleCount && prefix.SequenceEqual(expectedPrefix);

    private sortableBitv512Tests Pack(CancellationToken token)
    {
        var blocks = new sortBlockBitv512[(presorted.Length + 511) / 512];
        var vectors = new Vector512<ulong>[width];
        var words = MemoryMarshal.Cast<Vector512<ulong>, ulong>(vectors.AsSpan());
        for (var block = 0; block < blocks.Length; block++)
        {
            token.ThrowIfCancellationRequested();
            words.Clear();
            var lanes = Math.Min(512, presorted.Length - block * 512);
            for (var lane = 0; lane < lanes; lane++)
            {
                var value = presorted[block * 512 + lane];
                for (var wire = 0; wire < width; wire++)
                    words[wire * 8 + lane / 64] |= ((value >> wire) & 1UL) << (lane % 64);
            }
            blocks[block] = sortBlockBitv512.createFromVectors(vectors, lanes);
        }
        return sortableBitv512Tests.create(Guid.NewGuid(), width, blocks);
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

    internal NetworkCheckResult EvaluateSuffix(sorter suffix, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (suffix.ChannelCount != width) throw new ArgumentException("Sorter width does not match the prepared inputs.", nameof(suffix));
        var ces = suffix.Ces;
        int failed;
        int[] uses;
        if (simdInputs is { } inputs)
        {
            var block = ceBlock.create(suffix.SorterId, width, ces);
            var result = CeBlockOpsBitv512.evalCounts(inputs, block, token);
            failed = result.UnsortedCount;
            uses = result.UseCountArray;
        }
        else
        {
            failed = 0;
            uses = new int[ces.Length];
            var adjacentMask = (1UL << (width - 1)) - 1;
            bool IsSorted(ulong value) => (value & ~(value >> 1) & adjacentMask) == 0;
            for (var index = 0; index < presorted.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var working = presorted[index];
                for (var ceIndex = 0; ceIndex < ces.Length; ceIndex++)
                {
                    if ((ceIndex & 255) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (IsSorted(working)) break;
                    }
                    if (Apply(ref working, ces[ceIndex])) uses[ceIndex]++;
                }
                if (IsSorted(working)) continue;
                failed++;
            }
        }
        // Only the suffix is tested. Prefix slots are unmeasured placeholders, retained by FixedCes.
        return new(inputCount, samples is null, null, failed, Array.AsReadOnly(new int[prefix.Length].Concat(uses).ToArray()))
        {
            UsesReducedInputs = prefix.Length > 0,
            FixedCes = Array.AsReadOnly(Enumerable.Repeat(true, prefix.Length).Concat(Enumerable.Repeat(false, ces.Length)).ToArray())
        };
    }
}
