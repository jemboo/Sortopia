namespace Sortopia.FileDb

open System
open System.Buffers
open MessagePack
open Sortopia.Functions
open Sortopia.Core.Serialization
open Sortopia.Sorting.Serialization.Inputs
open Sortopia.Models.Sorting.Serialization
open Sortopia.Evaluation.Serialization

[<MessagePackObject>]
type sortopiaStorageDataDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Kind: string
    [<Key(2)>] Data: byte array
}

module internal DataCodec =
    [<Literal>]
    let maxBytes = 268435456 // Bound the stored, compressed envelope to 256 MiB.
    [<Literal>]
    let maxRawBytes = 1073741824 // Large model sets (10,000 MSUF4 models) need more raw capacity.
    let compressedOptions = MessagePackSetup.options
                                .WithSecurity(MessagePackSetup.options.Security.WithMaximumDecompressedSize(maxRawBytes))
                                .WithCompression(MessagePackCompression.Lz4BlockArray)
    let supported kind = List.contains kind ["SortableTests"; "SorterModelSet"; "SorterSetEval"; "SorterPoolSet"; "SorterPoolSummarySet"]

    // The envelope is [Version, Kind, binary Data]. Count its small header without
    // serializing/copying the entire payload a second time just to get its length.
    let payloadLength (dto: sortopiaStorageDataDto) =
        let header = ArrayBufferWriter<byte>(64)
        let mutable writer = MessagePackWriter(header)
        writer.WriteArrayHeader 3
        writer.Write dto.Version
        writer.Write dto.Kind
        writer.WriteBinHeader dto.Data.Length
        writer.Flush()
        int64 header.WrittenCount + int64 dto.Data.Length

    let private encodePayload value =
        let data =
            match value with
            | sortopiaStorageData.SortableTests tests -> tests |> SortableTestsDto.fromDomain |> MessagePackSetup.serialize
            | sortopiaStorageData.SorterModelSet models -> models |> SorterModelSetDto.fromDomain |> MessagePackSetup.serialize
            | sortopiaStorageData.SorterSetEval evaluation -> evaluation |> SorterSetEvalDto.fromDomain |> MessagePackSetup.serialize
            | sortopiaStorageData.SorterPoolSet pools -> pools |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
            | sortopiaStorageData.SorterPoolSummarySet summaries -> SorterPoolSummarySetCodec.encode summaries
        let dto = { Version = 2; Kind = SortopiaStorageData.kind value; Data = data }
        let rawLength = payloadLength dto
        if rawLength > int64 maxRawBytes then invalidOp "The data exceeds the 1 GiB uncompressed storage limit."
        MessagePackSerializer.Serialize(dto, compressedOptions), int rawLength

    let private decodePayload kind rawLength (bytes: byte array) =
        if rawLength < 0 || rawLength > maxRawBytes then invalidOp "Invalid uncompressed data length."
        let dto = MessagePackSerializer.Deserialize<sortopiaStorageDataDto>(ReadOnlyMemory<byte>(bytes), compressedOptions)
        if isNull (box dto) || dto.Version <> 2 then invalidOp "Unsupported data payload schema."
        if dto.Kind <> kind || isNull dto.Data then invalidOp "Invalid data payload."
        if payloadLength dto <> int64 rawLength then invalidOp "Data payload length does not match its envelope."
        match kind with
        | "SortableTests" ->
            dto.Data |> MessagePackSetup.deserialize<sortableTestsDto> |> SortableTestsDto.toDomain |> sortopiaStorageData.SortableTests
        | "SorterModelSet" -> dto.Data |> MessagePackSetup.deserialize<sorterModelSetDto> |> SorterModelSetDto.toDomain |> sortopiaStorageData.SorterModelSet
        | "SorterSetEval" -> dto.Data |> MessagePackSetup.deserialize<sorterSetEvalDto> |> SorterSetEvalDto.toDomain |> sortopiaStorageData.SorterSetEval
        | "SorterPoolSet" -> dto.Data |> MessagePackSetup.deserialize<sorterPoolSetDto> |> SorterPoolSetDto.toDomain |> sortopiaStorageData.SorterPoolSet
        | "SorterPoolSummarySet" -> dto.Data |> SorterPoolSummarySetCodec.decode |> sortopiaStorageData.SorterPoolSummarySet
        | _ -> invalidOp $"Unsupported stored data kind '{kind}'."

    // Large model graphs coexist with their DTOs and uncompressed buffers during
    // conversion. Do not multiply that transient memory across parallel files.
    // Evolution itself and small reports remain independent.
    let private modelConversion = obj ()
    let private convert kind action =
        if kind = "SorterModelSet" || kind = "SorterPoolSet" then lock modelConversion action
        else action ()

    let encode value = convert (SortopiaStorageData.kind value) (fun () -> encodePayload value)
    let decode kind rawLength bytes = convert kind (fun () -> decodePayload kind rawLength bytes)
