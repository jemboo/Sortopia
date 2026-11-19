namespace Sortopia.FileDb

open System
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
    let encode value =
        let data =
            match value with
            | sortopiaStorageData.SortableTests tests -> tests |> SortableTestsDto.fromDomain |> MessagePackSetup.serialize
            | sortopiaStorageData.SorterModelSet models -> models |> SorterModelSetDto.fromDomain |> MessagePackSetup.serialize
            | sortopiaStorageData.SorterSetEval evaluation -> evaluation |> SorterSetEvalDto.fromDomain |> MessagePackSetup.serialize
            | sortopiaStorageData.SorterPoolSet pools -> pools |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
            | sortopiaStorageData.SorterPoolSummarySet summaries -> summaries |> Sortopia.Evaluation.Sgd.SorterPoolSummarySet.validate |> MessagePackSetup.serialize
        let dto = { Version = 2; Kind = SortopiaStorageData.kind value; Data = data }
        let raw = MessagePackSetup.serialize dto
        if raw.Length > maxRawBytes then invalidOp "The data exceeds the 1 GiB uncompressed storage limit."
        MessagePackSerializer.Serialize(dto, compressedOptions), raw.Length

    let decode kind rawLength (bytes: byte array) =
        if rawLength < 0 || rawLength > maxRawBytes then invalidOp "Invalid uncompressed data length."
        let dto = MessagePackSerializer.Deserialize<sortopiaStorageDataDto>(ReadOnlyMemory<byte>(bytes), compressedOptions)
        if isNull (box dto) || dto.Version <> 2 then invalidOp "Unsupported data payload schema."
        if dto.Kind <> kind || isNull dto.Data then invalidOp "Invalid data payload."
        if (MessagePackSetup.serialize dto).Length <> rawLength then invalidOp "Data payload length does not match its envelope."
        match kind with
        | "SortableTests" ->
            dto.Data |> MessagePackSetup.deserialize<sortableTestsDto> |> SortableTestsDto.toDomain |> sortopiaStorageData.SortableTests
        | "SorterModelSet" -> dto.Data |> MessagePackSetup.deserialize<sorterModelSetDto> |> SorterModelSetDto.toDomain |> sortopiaStorageData.SorterModelSet
        | "SorterSetEval" -> dto.Data |> MessagePackSetup.deserialize<sorterSetEvalDto> |> SorterSetEvalDto.toDomain |> sortopiaStorageData.SorterSetEval
        | "SorterPoolSet" -> dto.Data |> MessagePackSetup.deserialize<sorterPoolSetDto> |> SorterPoolSetDto.toDomain |> sortopiaStorageData.SorterPoolSet
        | "SorterPoolSummarySet" -> dto.Data |> MessagePackSetup.deserialize<Sortopia.Evaluation.Sgd.sorterPoolSummarySet> |> Sortopia.Evaluation.Sgd.SorterPoolSummarySet.validate |> sortopiaStorageData.SorterPoolSummarySet
        | _ -> invalidOp $"Unsupported stored data kind '{kind}'."
