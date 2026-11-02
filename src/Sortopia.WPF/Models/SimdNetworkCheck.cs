using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Sortopia.Evaluation;
using Sortopia.Sorting.Inputs;

namespace Sortopia.WPF.Models;

// Pack the same ordered inputs as NetworkCheck once for all sorters in a run.
internal sealed class SimdNetworkCheck
{
    private readonly sortableBitv512Tests inputs;
    private readonly int count;
    private readonly bool exhaustive;

    internal SimdNetworkCheck(int width, int? samples, CancellationToken token)
    {
        count = NetworkCheck.InputCount(width, samples);
        exhaustive = samples is null;
        var random = exhaustive ? null : new Random(NetworkCheck.SampleSeed);
        var input = new int[width];
        var blocks = new sortBlockBitv512[(count + 511) / 512];
        var vectors = new Vector512<ulong>[width];
        var words = MemoryMarshal.Cast<Vector512<ulong>, ulong>(vectors.AsSpan());
        for (var block = 0; block < blocks.Length; block++)
        {
            token.ThrowIfCancellationRequested();
            words.Clear();
            var lanes = Math.Min(512, count - block * 512);
            for (var lane = 0; lane < lanes; lane++)
            {
                NetworkCheck.FillInput(input, block * 512 + lane, random);
                for (var wire = 0; wire < width; wire++)
                    words[wire * 8 + lane / 64] |= (ulong)input[wire] << (lane % 64);
            }
            blocks[block] = sortBlockBitv512.createFromVectors(vectors, lanes);
        }
        inputs = sortableBitv512Tests.create(Guid.NewGuid(), width, blocks);
    }

    internal NetworkCheckResult Run(NetworkDefinition network, CancellationToken token)
    {
        var block = ceBlock.create(Guid.NewGuid(), network.Width, network.Comparators);
        var (result, firstFailure) = CeBlockOpsBitv512.evalWithCounterexample(inputs, block, token);
        return new(count, exhaustive, firstFailure?.Value, result.UnsortedCount, Array.AsReadOnly(result.UseCountArray));
    }
}
