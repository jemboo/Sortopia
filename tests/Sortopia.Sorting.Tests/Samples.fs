module Sortopia.Sorting.Tests.Samples

open System
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

let datasetId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff") |> UMX.tag<sortableTestsId>
let width = 4<channelCount>

let rows count =
    Array.init count (fun i -> Array.init 4 (fun j -> (i >>> j) &&& 1))

let datasets count =
    let ints = rows count |> Array.map (fun a -> sortableIntArray.create(a, width, 2<symbolSetSize>))
    let bools = rows count |> Array.map (fun a -> sortableBoolArray.create(Array.map ((<>) 0) a, width))
    [| sortableIntTests.create datasetId width ints |> Ints
       sortableBinaryTests.create datasetId width bools |> Bools
       packedSortableIntTests.createWithId datasetId width (rows count |> Array.concat) |> PackedInts
       SortableUint8v256Tests.fromIntArrays datasetId width ints |> Uint8v256
       SortableUint8v512Tests.fromIntArrays datasetId width ints |> Uint8v512
       SortableBitv512Tests.fromBoolArrays datasetId width bools |> Bitv512 |]

let values tests =
    match tests with
    | Ints t -> t.SortableIntArrays |> Array.map (fun a -> a.Values)
    | Bools t -> t.SortableBinaryArrays |> Array.map (fun a -> a.Values |> Array.map (fun v -> if v then 1 else 0))
    | PackedInts t -> t.PackedValues |> Array.chunkBySize (int t.ChannelCount)
    | Uint8v256 t -> t.SimdSortBlocks |> Array.collect SortBlockUint8v256.toSortableIntArrays |> Array.map (fun a -> a.Values)
    | Uint8v512 t -> t.SimdSortBlocks |> Array.collect SortBlockUint8v512.toSortableIntArrays |> Array.map (fun a -> a.Values)
    | Bitv512 t -> t.SimdSortBlocks |> Array.collect SortBlockBitv512.toSortableIntArrays |> Array.map (fun a -> a.Values)

let newSorter width ces = Sortopia.Sorting.Networks.sorter.create (Guid.NewGuid() |> UMX.tag) width ces
let newSorterSet networks = Sortopia.Sorting.Networks.sorterSet.create (Guid.NewGuid() |> UMX.tag) networks
