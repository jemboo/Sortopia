namespace Sortopia.Storage

open System
open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Serialization.Inputs
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Serialization
open Sortopia.Evaluation
open Sortopia.Evaluation.Serialization

/// Values also name the on-disk type directories.
[<RequireQualifiedAccess>]
type outputDataType =
    | SortableTestsWithPrefix = 2
    | SorterModelSet = 3
    | SorterSetEval = 4

[<RequireQualifiedAccess>]
type ResultData =
    /// The prefix contains the CEs already applied to the tests, in order. Use [||] for ordinary tests.
    | SortableTestsWithPrefix of sortableTests * ce array
    | SorterModelSet of sorterModelSet
    | SorterSetEval of sorterSetEval

    member data.DataType =
        match data with
        | ResultData.SortableTestsWithPrefix _ -> outputDataType.SortableTestsWithPrefix
        | ResultData.SorterModelSet _ -> outputDataType.SorterModelSet
        | ResultData.SorterSetEval _ -> outputDataType.SorterSetEval

    member data.Id : Guid =
        match data with
        | ResultData.SortableTestsWithPrefix(tests, _) -> UMX.untag (SortableTests.getId tests)
        | ResultData.SorterModelSet models -> UMX.untag models.Id
        | ResultData.SorterSetEval evaluation -> UMX.untag evaluation.SorterSetEvalId

module internal ResultCodec =
    let encode = function
        | ResultData.SortableTestsWithPrefix(tests, prefix) ->
            SortableTestsWithPrefixDto.fromDomain (tests, prefix) |> MessagePackSetup.serialize
        | ResultData.SorterModelSet models -> SorterModelSetDto.fromDomain models |> MessagePackSetup.serialize
        | ResultData.SorterSetEval evaluation -> SorterSetEvalDto.fromDomain evaluation |> MessagePackSetup.serialize

    let decode kind bytes =
        match kind with
        | outputDataType.SortableTestsWithPrefix ->
            MessagePackSetup.deserialize bytes |> SortableTestsWithPrefixDto.toDomain |> ResultData.SortableTestsWithPrefix
        | outputDataType.SorterModelSet ->
            MessagePackSetup.deserialize bytes |> SorterModelSetDto.toDomain |> ResultData.SorterModelSet
        | outputDataType.SorterSetEval ->
            MessagePackSetup.deserialize bytes |> SorterSetEvalDto.toDomain |> ResultData.SorterSetEval
        | _ -> invalidArg "kind" "Unknown Result data type."
