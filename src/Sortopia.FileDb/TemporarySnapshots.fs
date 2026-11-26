namespace Sortopia.FileDb

open System
open System.IO
open System.Buffers
open System.Threading
open System.Security.Cryptography
open MessagePack
open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Evaluation.Sgd

[<MessagePackObject>]
type temporarySnapshotEnvelope = {
    [<Key(0)>] Version: int
    [<Key(1)>] Id: Guid
    [<Key(2)>] Generation: int
    [<Key(3)>] Checksum: byte array
    [<Key(4)>] Payload: byte array
}

[<CLIMutable>]
type temporarySnapshotPayload = {
    Snapshot: byte array
    SnapshotLength: int
    Summaries: byte array option
    SummariesLength: int
}

/// One atomically replaced, resumable snapshot per invocation and summary schedule.
module TemporarySnapshots =
    let identity (anchor: dataKey) reporting =
        let schedule = reporting |> Option.map _.SummarySchedule |> Option.defaultValue "Constant(1)"
        Array.append anchor.IdentityBytes (System.Text.Encoding.UTF8.GetBytes("temporary-snapshot/1:" + schedule))
        |> DataKey.hashId |> UMX.untag

    let path folder id =
        let root = Path.GetFullPath folder
        let result = Path.Combine(root, "data", "results", "temporarySnapshots", string id + ".msgpack")
        let mutable current = result
        while not (String.IsNullOrEmpty current) && current.Length >= root.Length do
            if (File.Exists current || Directory.Exists current) && File.GetAttributes(current).HasFlag FileAttributes.ReparsePoint then
                invalidOp "Temporary snapshot storage does not follow filesystem links."
            current <- Path.GetDirectoryName current
        result

    let private openRead file = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read ||| FileShare.Delete)

    /// Progress reads only a small header from an atomic file, never the population payload.
    let tryGeneration folder id (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        try
            use stream = openRead (path folder id)
            let bytes = Array.zeroCreate<byte> (int (min 512L stream.Length))
            stream.ReadExactly bytes
            let mutable reader = MessagePackReader(ReadOnlyMemory<byte>(bytes))
            if reader.ReadArrayHeader() <> 5 || reader.ReadInt32() <> 1 then invalidOp "Invalid temporary snapshot header."
            if MessagePackSerializer.Deserialize<Guid>(&reader, MessagePackSetup.options) <> id then invalidOp "Temporary snapshot identity mismatch."
            let generation = reader.ReadInt32()
            let checksum = reader.ReadBytes().Value.ToArray()
            let mutable length, headerLength = 0u, 0
            if MessagePackPrimitives.TryReadBinHeader(ReadOnlySpan<byte>(bytes, int reader.Consumed, bytes.Length - int reader.Consumed), &length, &headerLength) <> MessagePackPrimitives.DecodeResult.Success
               || generation < 0 || checksum.Length <> 32 || stream.Length > int64 DataCodec.maxBytes
               || int64 length + reader.Consumed + int64 headerLength <> stream.Length then
                invalidOp "Invalid temporary snapshot size or generation."
            Some generation
        with :? FileNotFoundException | :? DirectoryNotFoundException -> None

    let load folder id parameters (plan: evolutionSamplingPlan) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        try
            use stream = openRead (path folder id)
            if stream.Length > int64 DataCodec.maxBytes then invalidOp "Temporary snapshot is too large."
            let bytes = Array.zeroCreate<byte> (int stream.Length)
            stream.ReadExactly bytes
            let envelope = MessagePackSetup.deserialize<temporarySnapshotEnvelope> bytes
            if envelope.Version <> 1 || envelope.Id <> id || envelope.Generation < 0 || isNull envelope.Payload
               || isNull envelope.Checksum || SHA256.HashData envelope.Payload <> envelope.Checksum then
                invalidOp "Temporary snapshot identity or checksum is invalid."
            let payload = MessagePackSetup.deserialize<temporarySnapshotPayload> envelope.Payload
            let snapshot =
                match DataCodec.decode "SorterPoolSet" payload.SnapshotLength payload.Snapshot with
                | sortopiaStorageData.SorterPoolSet snapshot -> snapshot
                | _ -> invalidOp "Expected a temporary pool snapshot."
            EvolutionSampling.checkSnapshot parameters envelope.Generation snapshot
            let rows =
                match payload.Summaries with
                | None -> [||]
                | Some bytes ->
                    match DataCodec.decode "SorterPoolSummarySet" payload.SummariesLength bytes with
                    | sortopiaStorageData.SorterPoolSummarySet summary when summary.TestsId = UMX.untag snapshot.TestsId -> summary.Rows
                    | _ -> invalidOp "Temporary summaries do not match their snapshot."
            let expected = plan.Summaries |> Array.filter (fun (generations, _) -> Array.last generations > snapshot.Generation)
                           |> Array.collect (fst >> Array.filter (fun generation -> generation <= snapshot.Generation))
            if expected.Length = 0 then
                if rows.Length <> 0 then invalidOp "Unexpected pending temporary summaries."
            else EvolutionSampling.checkSummary parameters expected { TestsId = UMX.untag snapshot.TestsId; Rows = rows }
            token.ThrowIfCancellationRequested()
            Some { Snapshot = snapshot; PendingSummaries = rows }
        with :? FileNotFoundException | :? DirectoryNotFoundException -> None

    let create folder id parameters resume (token: CancellationToken) : ITemporaryEvolutionSnapshots =
        let destination = path folder id
        let withWriter action =
            Directory.CreateDirectory(Path.GetDirectoryName destination) |> ignore
            // Serializes replacement and retirement across independent terminal processes.
            let lockPath = destination + ".lock"
            if File.Exists lockPath && File.GetAttributes(lockPath).HasFlag FileAttributes.ReparsePoint then
                invalidOp "Temporary snapshot storage does not follow filesystem links."
            let timer = Diagnostics.Stopwatch.StartNew()
            let mutable handle = None
            while handle.IsNone do
                token.ThrowIfCancellationRequested()
                try handle <- Some (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                with :? IOException when timer.Elapsed < TimeSpan.FromSeconds 30.0 -> token.WaitHandle.WaitOne(25) |> ignore
            use writer = handle.Value
            action ()
        { new ITemporaryEvolutionSnapshots with
            member _.Resume = resume
            member _.SaveTemporary value =
                token.ThrowIfCancellationRequested()
                EvolutionSampling.checkSnapshot parameters value.Snapshot.Generation value.Snapshot
                let snapshot, snapshotLength = DataCodec.encode (sortopiaStorageData.SorterPoolSet value.Snapshot)
                let summaries, summariesLength =
                    if value.PendingSummaries.Length = 0 then None, 0 else
                    let bytes, size = DataCodec.encode (sortopiaStorageData.SorterPoolSummarySet { TestsId = UMX.untag value.Snapshot.TestsId; Rows = value.PendingSummaries })
                    Some bytes, size
                let payload = MessagePackSetup.serialize { Snapshot = snapshot; SnapshotLength = snapshotLength; Summaries = summaries; SummariesLength = summariesLength }
                let bytes = MessagePackSetup.serialize { Version = 1; Id = id; Generation = value.Snapshot.Generation; Checksum = SHA256.HashData payload; Payload = payload }
                if bytes.Length > DataCodec.maxBytes then invalidOp "Temporary snapshot exceeds the storage size limit."
                withWriter (fun () ->
                    if tryGeneration folder id token |> Option.forall (fun generation -> generation <= value.Snapshot.Generation) then
                        let staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp"
                        try
                            do
                                use stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                                stream.Write bytes
                                stream.Flush(true)
                            token.ThrowIfCancellationRequested()
                            File.Move(staging, destination, true)
                        finally
                            if File.Exists staging then File.Delete staging)
            member _.ClearTemporary generation =
                if File.Exists destination then
                    withWriter (fun () ->
                        if tryGeneration folder id token |> Option.exists (fun saved -> saved <= generation) then File.Delete destination) }
