namespace Sortopia.Evaluation

open System
open System.IO
open System.Security.Cryptography
open System.Buffers.Binary
open Sortopia.Sorting.Networks

module internal EvaluationHash =
    /// Versioned, deterministic fingerprint of width, stage boundaries and CE order.
    let stageSequence (sequence: stageSequence) =
        use bytes = new MemoryStream()
        use writer = new BinaryWriter(bytes)
        writer.Write(1)
        writer.Write(int sequence.ChannelCount)
        let stages = sequence.Stages
        writer.Write(stages.Length)
        for stage in stages do
            let ces = stage.Ces
            writer.Write(ces.Length)
            for ce in ces do writer.Write(Ce.toIndex ce)
        writer.Flush()
        BinaryPrimitives.ReadInt32LittleEndian(ReadOnlySpan<byte>(SHA256.HashData(bytes.ToArray())))
