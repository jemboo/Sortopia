namespace Sortopia.FileDb

open System
open MessagePack
open Sortopia.Functions
open Sortopia.Core.Serialization
open Sortopia.Sorting.Serialization.Inputs
open Sortopia.Sorting.Serialization.Networks
open Sortopia.Models.Sorting.Serialization
open Sortopia.Evaluation.Serialization

[<MessagePackObject>]
type functionPayloadDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Kind: string
    [<Key(2)>] Data: byte array
    [<Key(3)>] Prefix: ceDto array
}

module internal DataCodec =
    [<Literal>]
    let maxBytes = 268435456 // Explicit bound for the initial whole-object codecs.
    let compressedOptions = MessagePackSetup.options
                                .WithSecurity(MessagePackSetup.options.Security.WithMaximumDecompressedSize(maxBytes))
                                .WithCompression(MessagePackCompression.Lz4BlockArray)
    let supported kind = List.contains kind ["SortableTests"; "SorterModelSet"; "SorterSetEval"]
    let encode value =
        let data, prefix =
            match value with
            | functionData.SortableTests (tests, prefix) ->
                tests |> SortableTestsDto.fromDomain |> MessagePackSetup.serialize, prefix |> Array.map CeDto.fromDomain
            | functionData.SorterModelSet models -> models |> SorterModelSetDto.fromDomain |> MessagePackSetup.serialize, [||]
            | functionData.SorterSetEval evaluation -> evaluation |> SorterSetEvalDto.fromDomain |> MessagePackSetup.serialize, [||]
        let dto = { Version = 1; Kind = FunctionData.kind value; Data = data; Prefix = prefix }
        let raw = MessagePackSetup.serialize dto
        if raw.Length > maxBytes then invalidOp "The data exceeds the 256 MiB uncompressed storage limit."
        MessagePackSerializer.Serialize(dto, compressedOptions), raw.Length

    let decode kind rawLength (bytes: byte array) =
        if rawLength < 0 || rawLength > maxBytes then invalidOp "Invalid uncompressed data length."
        let dto = MessagePackSerializer.Deserialize<functionPayloadDto>(ReadOnlyMemory<byte>(bytes), compressedOptions)
        if isNull (box dto) || dto.Version <> 1 then invalidOp "Unsupported data payload schema."
        if dto.Kind <> kind || isNull dto.Data || isNull dto.Prefix then invalidOp "Invalid data payload."
        if (MessagePackSetup.serialize dto).Length <> rawLength then invalidOp "Data payload length does not match its envelope."
        if kind <> "SortableTests" && dto.Prefix.Length <> 0 then invalidOp "Unexpected prefix data."
        match kind with
        | "SortableTests" ->
            functionData.SortableTests (dto.Data |> MessagePackSetup.deserialize<sortableTestsDto> |> SortableTestsDto.toDomain, dto.Prefix |> Array.map CeDto.toDomain)
        | "SorterModelSet" -> dto.Data |> MessagePackSetup.deserialize<sorterModelSetDto> |> SorterModelSetDto.toDomain |> functionData.SorterModelSet
        | "SorterSetEval" -> dto.Data |> MessagePackSetup.deserialize<sorterSetEvalDto> |> SorterSetEvalDto.toDomain |> functionData.SorterSetEval
        | _ -> invalidOp $"Unsupported stored data kind '{kind}'."
