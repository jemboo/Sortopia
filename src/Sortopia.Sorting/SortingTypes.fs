namespace Sortopia.Sorting

open FSharp.UMX

[<Measure>] type channelIndex

module ChannelIndex =
    /// Construct a zero-based channel position; a network supplies its upper bound.
    let create (value: int) : int<channelIndex> =
        if value < 0 then invalidArg (nameof value) "Channel index must be non-negative."
        UMX.tag value

[<Measure>] type channelCount
[<Measure>] type sorterCount
[<Measure>] type sorterId
[<Measure>] type stageLength
[<Measure>] type ceLength
[<Measure>] type isReflectionSymmetric
[<Measure>] type stageCrossings
[<Measure>] type ceIndex
[<Measure>] type symbolSetSize
[<Measure>] type sortableCount
[<Measure>] type sortableTestsId
[<Measure>] type sortableTestsCount

[<Measure>] type sorterSetId

[<Measure>] type excludeSelfCe
[<Measure>] type mergeDimension

[<Measure>] type sortableTestsPartitionId
[<Measure>] type sortableTestsSubsetId
[<Measure>] type partitionCount
[<Measure>] type generationsPerSubset
[<Measure>] type adjustCeLengthFrequency

type mergeSuffixType =
    | NoSuffix
    | VV_1

type sortableDataFormat = 
    | BitVector256
    | BitVector512
    | BoolArray
    | IntArray
    | PackedIntArray
    | Int8Vector256
    | Int8Vector512

module SortableDataFormat =

    let toString (t:sortableDataFormat) : string =
        match t with
        | BitVector256 -> "BitVector256"
        | BitVector512 -> "BitVector512"
        | Int8Vector256 -> "Int8Vector256"
        | Int8Vector512 -> "Int8Vector512"
        | IntArray -> "Ints"
        | PackedIntArray -> "PackedInts"
        | BoolArray -> "Bools"

    let fromString (s:string) : sortableDataFormat =
        match s with
        | "BitVector256" -> BitVector256
        | "BitVector512" -> BitVector512
        | "Ints" -> IntArray
        | "PackedInts" -> PackedIntArray
        | "Bools" -> BoolArray
        | "Int8Vector256" -> Int8Vector256
        | "Int8Vector512" -> Int8Vector512
        | _ -> failwithf "Unknown SortableDataFormat: %s" s

module SortableTestsPartitionId =
    let Default = "Default" |> UMX.tag<sortableTestsPartitionId>

module SortableTestsSubsetId =
    let Default = "Default" |> UMX.tag<sortableTestsSubsetId>

module GenerationsPerSubset =

    let toString (w: int<generationsPerSubset> option) : string =
       match w with
        | Some v -> %v |> string
        | None -> "None"

module ChannelCount =
    let toString (w: int<channelCount> option) : string =
       match w with
        | Some v -> sprintf "%d" %v
        | None -> "None"

module SorterCount =
    let toString (c: int<sorterCount> option) : string =
       match c with
        | Some v -> sprintf "%d" %v
        | None -> "None"

module MergeDimension =
    let toString (d: int<mergeDimension> option) : string =
        match d with
        | Some v -> sprintf "%d" %v
        | None -> "None"

module MergeSuffixType =

    let toString (t:mergeSuffixType) : string =
        match t with
        | NoSuffix -> "NoSuffix"
        | VV_1 -> "VV_1"

    let fromString (s:string) : mergeSuffixType =
        match s with
        | "NoSuffix" -> mergeSuffixType.NoSuffix
        | "VV_1" -> mergeSuffixType.VV_1
        | _ -> failwithf "Unknown MergeFillType: %s" s

module StageLength =

    let toCeLength (channelCount:int<channelCount>) (stageLength: int<stageLength>)
            : int<ceLength> =
        if %stageLength < 1 then
            failwith "StageLength must be at least 1"
        else
            ((%stageLength * %channelCount) / 2) |> UMX.tag<ceLength>

module AdjustCeLengthFrequency =

    let toString (w: int<adjustCeLengthFrequency> option) : string =
       match w with
        | Some v -> sprintf "%d" %v
        | None -> "None"
