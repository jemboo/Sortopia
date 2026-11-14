namespace Sortopia.FileDb

open System
open System.Buffers
open System.IO
open System.Threading
open MessagePack
open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Runs
open Sortopia.Functions

[<MessagePackObject>]
type dataEnvelopeDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] ProjectId: Guid
    [<Key(2)>] Id: Guid
    [<Key(3)>] QueryId: Guid
    [<Key(4)>] IsSetup: bool
    [<Key(5)>] Kind: string
    [<Key(6)>] Label: string
    [<Key(7)>] QueryBytes: byte array
    [<Key(8)>] IdentityBytes: byte array
    [<Key(9)>] Compression: string
    [<Key(10)>] RawLength: int
    [<Key(11)>] Checksum: byte array
    [<Key(12)>] Payload: byte array
}

type internal dataHeader = { Envelope: dataEnvelopeDto; PayloadLength: uint32 }

/// Query lookup and inspection share a bounded envelope reader. Payloads stay on disk.
module internal DataEnvelope =
    let dependencies (value: dataEnvelopeDto) =
        use stream = new MemoryStream(value.IdentityBytes, false)
        stream.Position <- int64 value.QueryBytes.Length
        use reader = new BinaryReader(stream, System.Text.Encoding.UTF8)
        let count = reader.ReadInt32()
        if count < 0 || count > 4096 then invalidOp "Invalid stored dependency count."
        let inputs = [ for _ in 1..count do
                        let project = Guid(reader.ReadBytes 16)
                        let id = Guid(reader.ReadBytes 16)
                        let query = Guid(reader.ReadBytes 16)
                        let setup = reader.ReadBoolean()
                        let kind = reader.ReadString()
                        let label = reader.ReadString()
                        yield { ProjectId = UMX.tag project; Id = UMX.tag id; QueryId = UMX.tag query
                                IsSetup = setup; DataType = SortopiaDataType.create kind label } ]
        if stream.Position <> stream.Length then invalidOp "Unexpected stored dependency metadata."
        inputs

    let readHeader path (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        let bytes = Array.zeroCreate<byte> (int (min stream.Length (1024L * 1024L)))
        stream.ReadExactly bytes
        let mutable reader = MessagePackReader(ReadOnlyMemory<byte>(bytes))
        if reader.ReadArrayHeader() <> 13 then invalidOp $"Unsupported data envelope: {path}"
        let version = reader.ReadInt32()
        let projectId = MessagePackSerializer.Deserialize<Guid>(&reader, MessagePackSetup.options)
        let id = MessagePackSerializer.Deserialize<Guid>(&reader, MessagePackSetup.options)
        let queryId = MessagePackSerializer.Deserialize<Guid>(&reader, MessagePackSetup.options)
        let setup = reader.ReadBoolean()
        let kind = reader.ReadString()
        let label = reader.ReadString()
        let query = reader.ReadBytes().Value.ToArray()
        let identity = reader.ReadBytes().Value.ToArray()
        let compression = reader.ReadString()
        let rawLength = reader.ReadInt32()
        let checksum = reader.ReadBytes().Value.ToArray()
        let mutable length = 0u
        let mutable headerLength = 0
        if MessagePackPrimitives.TryReadBinHeader(ReadOnlySpan<byte>(bytes, int reader.Consumed, bytes.Length - int reader.Consumed), &length, &headerLength) <> MessagePackPrimitives.DecodeResult.Success then
            invalidOp $"Incomplete data envelope or metadata exceeding the 1 MiB header limit: {path}"
        if version <> 1 || compression <> "messagepack-lz4-array/1" then invalidOp $"Unsupported data schema or compression: {path}"
        if rawLength < 0 || checksum.Length <> 32 || int64 length + reader.Consumed + int64 headerLength <> stream.Length then
            invalidOp $"Invalid stored payload size or checksum metadata: {path}"
        if UMX.untag (DataKey.hashId query) <> queryId || UMX.untag (DataKey.hashId identity) <> id
           || not (identity.AsSpan().StartsWith(query.AsSpan())) then invalidOp $"Stored data identity hashes are invalid: {path}"
        token.ThrowIfCancellationRequested()
        { Envelope = { Version = version; ProjectId = projectId; Id = id; QueryId = queryId; IsSetup = setup
                       Kind = kind; Label = label; QueryBytes = query; IdentityBytes = identity
                       Compression = compression; RawLength = rawLength; Checksum = checksum; Payload = [||] }
          PayloadLength = length }
