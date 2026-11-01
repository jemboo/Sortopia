namespace Sortopia.Evaluation.Serialization

open System
open MessagePack
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Serialization.Inputs
open Sortopia.Evaluation

[<MessagePackObject>]
type sorterEvalV1Dto = {
    [<Key(0)>] SorterId : Guid
    [<Key(1)>] SortableTestsSubsetId : string
    [<Key(2)>] UnsortedCount : int option
    [<Key(3)>] SequenceHash : int
    [<Key(4)>] LastCeIndex : int
    [<Key(5)>] StageLength : int
    [<Key(6)>] CeLength : int
    [<Key(7)>] ChannelCount : int
    [<Key(8)>] ReflectionSymmetric : bool
    [<Key(9)>] StageCrossingsCount : int
    [<Key(10)>] ReflectiveCount : int
}

[<MessagePackObject>]
type sorterEvalV2Dto = {
    [<Key(0)>] SorterId : Guid
    [<Key(1)>] SortableTestsSubsetId : string
    [<Key(2)>] UnsortedCount : int option
    [<Key(3)>] SequenceHash : int
    [<Key(4)>] StageLength : int
    [<Key(5)>] CeUseArray : ceDataDto array
    [<Key(6)>] ChannelCount : int
    [<Key(7)>] ReflectionSymmetric : bool
    [<Key(8)>] StageCrossingsCount : int
    [<Key(9)>] ReflectiveCount : int
}

[<MessagePackObject>]
type sorterEvalV3Dto = {
    [<Key(0)>] SorterId : Guid
    [<Key(1)>] SortableTestsSubsetId : string
    [<Key(2)>] UnsortedCount : int option
    [<Key(3)>] SequenceHash : int
    [<Key(4)>] StageLength : int
    [<Key(5)>] CeUseArray : ceDataDto array
    [<Key(6)>] SortableTests : sortableTestsDto
    [<Key(7)>] ChannelCount : int
    [<Key(8)>] ReflectionSymmetric : bool
    [<Key(9)>] StageCrossingsCount : int
    [<Key(10)>] ReflectiveCount : int
}

// ---------------------------------------------------------------------
// 2. Main Sorter Evaluation DTO Union
// ---------------------------------------------------------------------

type sorterEvalDto =
    | V1 of sorterEvalV1Dto
    | V2 of sorterEvalV2Dto
    | V3 of sorterEvalV3Dto

// ---------------------------------------------------------------------
// 3. Conversion Module
// ---------------------------------------------------------------------

module SorterEvalDto =

    let private toSubsetId (value: string) : string<sortableTestsSubsetId> =
        if String.IsNullOrWhiteSpace value then SortableTestsSubsetId.Default
        else value |> UMX.tag<sortableTestsSubsetId>

    let fromDomain (domain: sorterEval) : sorterEvalDto =
        match domain with
        | sorterEval.V1 v1 ->
            V1 {
                SorterId = %v1.SorterId
                SortableTestsSubsetId = %v1.SortableTestsSubsetId
                UnsortedCount = Some (UMX.untag v1.UnsortedCount)
                SequenceHash = %v1.SequenceHash
                LastCeIndex = %v1.LastCeIndex
                StageLength = %v1.StageLength
                CeLength = %v1.CeLength
                ChannelCount = %v1.ChannelCount
                ReflectionSymmetric = %v1.ReflectionSymmetric
                StageCrossingsCount = %v1.StageCrossingsCount
                ReflectiveCount = %v1.ReflectiveCount
            }
        | sorterEval.V2 v2 ->
            V2 {
                SorterId = %v2.SorterId
                SortableTestsSubsetId = %v2.SortableTestsSubsetId
                UnsortedCount = Some (UMX.untag v2.UnsortedCount)
                SequenceHash = %v2.SequenceHash
                StageLength = %v2.StageLength
                CeUseArray = v2.CeUseArray |> Array.map CeDataDto.fromDomain
                ChannelCount = %v2.ChannelCount
                ReflectionSymmetric = %v2.IsReflectionSymmetric
                StageCrossingsCount = %v2.StageCrossingsCount
                ReflectiveCount = %v2.ReflectiveCount
            }
        | sorterEval.V3 v3 ->
            V3 {
                SorterId = %v3.SorterId
                SortableTestsSubsetId = %v3.SortableTestsSubsetId
                UnsortedCount = Some (UMX.untag v3.UnsortedCount)
                SequenceHash = %v3.SequenceHash
                StageLength = %v3.StageLength
                CeUseArray = v3.CeUseArray |> Array.map CeDataDto.fromDomain
                SortableTests = SortableTestsDto.fromDomain v3.SortableTests
                ChannelCount = %v3.ChannelCount
                ReflectionSymmetric = %v3.ReflectionSymmetric
                StageCrossingsCount = %v3.StageCrossingsCount
                ReflectiveCount = %v3.ReflectiveCount
            }

    let toDomain (dto: sorterEvalDto) : sorterEval =
        match dto with
        | V1 v1Dto ->
            sorterEvalV1.create
                (v1Dto.SorterId |> UMX.tag)
                (toSubsetId v1Dto.SortableTestsSubsetId)
                (v1Dto.ChannelCount |> UMX.tag)
                (v1Dto.UnsortedCount |> Option.defaultWith (fun () -> invalidArg "dto" "UnsortedCount is required; missing results cannot be treated as sorted.") |> UMX.tag)
                (v1Dto.SequenceHash |> UMX.tag)
                (v1Dto.LastCeIndex |> UMX.tag)
                (v1Dto.StageLength |> UMX.tag)
                (v1Dto.CeLength |> UMX.tag)
                (v1Dto.ReflectionSymmetric |> UMX.tag)
                (v1Dto.StageCrossingsCount |> UMX.tag)
                (v1Dto.ReflectiveCount |> UMX.tag)
            |> sorterEval.V1
        | V2 v2Dto ->
            sorterEvalV2.create
                (v2Dto.SorterId |> UMX.tag)
                (toSubsetId v2Dto.SortableTestsSubsetId)
                (v2Dto.ChannelCount |> UMX.tag)
                (v2Dto.UnsortedCount |> Option.defaultWith (fun () -> invalidArg "dto" "UnsortedCount is required; missing results cannot be treated as sorted.") |> UMX.tag)
                (v2Dto.SequenceHash |> UMX.tag)
                (v2Dto.StageLength |> UMX.tag)
                (v2Dto.CeUseArray |> Array.map CeDataDto.toDomain)
                (v2Dto.ReflectionSymmetric |> UMX.tag)
                (v2Dto.StageCrossingsCount |> UMX.tag)
                (v2Dto.ReflectiveCount |> UMX.tag)
            |> sorterEval.V2
        | V3 v3Dto ->
            sorterEvalV3.create
                (v3Dto.SorterId |> UMX.tag)
                (toSubsetId v3Dto.SortableTestsSubsetId)
                (v3Dto.ChannelCount |> UMX.tag)
                (v3Dto.UnsortedCount |> Option.defaultWith (fun () -> invalidArg "dto" "UnsortedCount is required; missing results cannot be treated as sorted.") |> UMX.tag)
                (v3Dto.SequenceHash |> UMX.tag)
                (v3Dto.StageLength |> UMX.tag)
                (v3Dto.CeUseArray |> Array.map CeDataDto.toDomain)
                (SortableTestsDto.toDomain v3Dto.SortableTests)
                (v3Dto.ReflectionSymmetric |> UMX.tag)
                (v3Dto.StageCrossingsCount |> UMX.tag)
                (v3Dto.ReflectiveCount |> UMX.tag)
            |> sorterEval.V3
