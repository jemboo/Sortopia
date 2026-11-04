using System.Buffers.Binary;
using System.Security.Cryptography;
using Sortopia.Sorting.Networks;

namespace Sortopia.WPF.Models;

/// <summary>First 128 bits of SHA-256 over a versioned, stage-packed canonical network.</summary>
internal readonly record struct SorterFingerprint(ulong Low, ulong High)
{
    internal static SorterFingerprint Create(NetworkDefinition network) =>
        FromPacked(StageBuilderSequence.toStageSequence(network.Width, network.Comparators));

    // Search candidates already contain a packed sequence with canonically ordered stages.
    internal static SorterFingerprint FromPacked(stageSequence sequence)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, 1); // Encoding version.
        Append(hash, sequence.ChannelCount);
        var stages = sequence.Stages;
        Append(hash, stages.Length);
        foreach (var stage in stages)
        {
            var ces = stage.Ces;
            Append(hash, ces.Length);
            foreach (var ce in ces)
            {
                Append(hash, ce.LowChannel);
                Append(hash, ce.HighChannel);
            }
        }
        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return new(BinaryPrimitives.ReadUInt64LittleEndian(digest),
            BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]));
    }

    private static void Append(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }
}

/// <summary>Run-local duplicate detection. Retains fingerprints, never sorter models or networks.</summary>
internal sealed class SgdDuplicateTracker
{
    private readonly HashSet<SorterFingerprint> fingerprints = [];
    internal int UniqueCount => fingerprints.Count;
    internal long DuplicateCount { get; private set; }
    internal bool Observe(NetworkDefinition packedNetwork)
        => Observe(SorterFingerprint.FromPacked(packedNetwork.Sequence));

    internal bool Observe(SorterFingerprint fingerprint)
    {
        if (fingerprints.Add(fingerprint)) return false;
        DuplicateCount++;
        return true;
    }
}
