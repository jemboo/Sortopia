namespace Sortopia.Models.Sorting

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
[<Measure>] type sorterModelId
[<Measure>] type sorterModelSetId
[<Measure>] type sorterPairModelId
[<Measure>] type sorterModelGenId
[<Measure>] type sorterModelMutatorId
[<Measure>] type sortingGenIndex
[<Measure>] type sortingGenId
[<Measure>] type sortingMutatorId
[<Measure>] type sortingGenSegmentId
[<Measure>] type sortingMutationSegmentId
[<Measure>] type parentSorterSetEvalId
[<Measure>] type sortingParamsId
[<Measure>] type modificationRate
[<Measure>] type seedModificationRate
[<Measure>] type prioritizeNewMutants
[<Measure>] type mutationMod
[<Measure>] type mutationIndex
[<Measure>] type mutationRate
[<Measure>] type insertionRate
[<Measure>] type deletionRate
[<Measure>] type orthoRate
[<Measure>] type paraRate
[<Measure>] type selfSymRate


type mutatorVariant =
    | V1
    | V2
    | V3

module MutatorVariant =

    let toString = function
        | V1 -> "V1"
        | V2 -> "V2"
        | V3 -> "V3"

    let fromString = function
        | "V1" -> V1
        | "V2" -> V2
        | "V3" -> V3
        | s -> failwithf "Unknown mutatorVariant: %s" s


module MutationMod =
    let toString (w: int<mutationMod> option) : string =
        UmxExt.intOptionToString w

module SeedModificationRate =
    let toString (w: float<seedModificationRate> option) : string =
        UmxExt.floatOptionToString w

module ModificationRate =
    let toString (w: float<modificationRate> option) : string =
        UmxExt.floatOptionToString w

module ParaRate =
    let toString (w: float<paraRate> option) : string =
        UmxExt.floatOptionToString w

module SelfSymRate =
    let toString (w: float<selfSymRate> option) : string =
        UmxExt.floatOptionToString w

module CommonGen =
    let makeSorterModelId
                (id: Guid<sorterModelGenId>)
                (index: int<sortingGenIndex>) : Guid<sorterModelId> =
        [
            box "CommonGen.makeSorterModelId"
            box (id |> UMX.untag)
            box (index |> UMX.untag)
        ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelId>

module CommonMutator =
    let makeSorterModelId
                (parentId: Guid<sorterModelId>)
                (id: Guid<sorterModelMutatorId>)
                (index: int<mutationIndex>)
                (modd: int<mutationMod>): Guid<sorterModelId> =
        [
            box "CommonMutator.makeSorterModelId"
            box (parentId |> UMX.untag)
            box (id |> UMX.untag)
            box (index |> UMX.untag)
            box (modd |> UMX.untag)
        ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelId>