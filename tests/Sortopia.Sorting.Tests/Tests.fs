module Sortopia.Sorting.Tests.PrefixTests

open System
open FSharp.UMX
open Microsoft.FSharp.Reflection
open Xunit
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks

[<Fact>]
let ``Component prefix images preserve brute force results and ordering`` () =
    let networks = [|
        [||]
        [| ce.create 0 2; ce.create 1 3; ce.create 2 4 |]
        [| ce.create 0 1; ce.create 1 2; ce.create 2 3; ce.create 3 4; ce.create 4 5 |]
        [| ce.create 0 1; ce.create 2 3; ce.create 0 1; ce.create 5 5 |]
    |]
    for network in networks do
        let expected =
            SortableBoolArray.getAllSortableBoolArrays 6<channelCount>
            |> Seq.map (fun input -> input.SortByCes network)
            |> SortableBoolArray.removeDuplicates
            |> Seq.map (fun output -> output.Values) |> Seq.toArray
        let actual =
            SortableBoolArray.getAllPossibleResultsFromCeArray network 6<channelCount>
            |> Seq.map (fun output -> output.Values) |> Seq.toArray
        Assert.True(((expected = actual)))

[<Fact>]
let ``Interleaved component images keep first occurrence order`` () =
    let rng = Random(1804)
    for _ in 1 .. 30 do
        let network = Array.init 12 (fun _ ->
            let parity = rng.Next(2)
            let a, b = rng.Next(4) * 2 + parity, rng.Next(4) * 2 + parity
            ce.create (min a b) (max a b))
        let expected =
            SortableBoolArray.getAllSortableBoolArrays 8<channelCount>
            |> Seq.map (fun input -> input.SortByCes network)
            |> SortableBoolArray.removeDuplicates |> Seq.toArray
        let actual = SortableBoolArray.getAllPossibleResultsFromCeArray network 8<channelCount> |> Seq.toArray
        Assert.Equal<sortableBoolArray>(expected, actual)

[<Fact>]
let ``Huge component products can be consumed incrementally`` () =
    let prefix width = [| for low in 0 .. 2 .. width - 2 -> ce.create low (low + 1) |]
    let expected =
        SortableBoolArray.getAllPossibleResultsFromCeArray (prefix 6) 6<channelCount>
        |> Seq.take 20 |> Seq.map (fun row -> Array.append row.Values (Array.create 56 false)) |> Seq.toArray
    // 3^31 outputs cannot be materialized or globally sorted to obtain this prefix.
    let actual = SortableBoolArray.getAllPossibleResultsFromCeArray (prefix 62) 62<channelCount>
                 |> Seq.take 20 |> Seq.map _.Values |> Seq.toArray
    Assert.True((expected = actual))

[<Theory>]
[<InlineData(0)>]
[<InlineData(1)>]
[<InlineData(511)>]
[<InlineData(512)>]
[<InlineData(513)>]
[<InlineData(1103)>]
let ``Streaming packing preserves every bit duplicate and partial block`` count =
    let rows = Array.init count (fun i -> sortableBoolArray.create(Array.init 64 (fun c -> (i * 17 + c) % 7 < 3), 64<channelCount>))
    let mutable enumerations = 0
    let mutable disposed = false
    let source = seq {
        enumerations <- enumerations + 1
        try yield! rows finally disposed <- true
    }
    let packed = SortableBitv512Tests.fromBoolSequence (UMX.tag (Guid.NewGuid())) 64<channelCount> source
    let restored = packed.SimdSortBlocks |> Array.collect SortBlockBitv512.toSortableBoolArrays
    Assert.Equal(1, enumerations)
    Assert.True(disposed)
    Assert.Equal(count, int packed.SortableCount)
    Assert.Equal<sortableBoolArray>(rows, restored)
    for block in packed.SimdSortBlocks do
        let copy = block.Vectors
        block.CopyVectorsTo copy
        Array.Clear copy
        Assert.Equal<sortableBoolArray>(rows, packed.SimdSortBlocks |> Array.collect SortBlockBitv512.toSortableBoolArrays)

[<Fact>]
let ``Four stage 32 channel prefix has 28224 distinct outputs`` () =
    let network = [|
        for stride in [1; 2; 4; 8] do
            for channel in 0 .. 31 do
                if channel &&& stride = 0 then
                    yield ce.create channel (channel + stride)
    |]
    let outputs = SortableBoolArray.getAllPossibleResultsFromCeArray network 32<channelCount> |> Seq.toArray
    Assert.Equal(28224, outputs.Length)
    Assert.Equal(outputs.Length, outputs |> Array.distinct |> Array.length)

[<Fact>]
let ``All sortable data formats round trip through strings`` () =
    for case in FSharpType.GetUnionCases(typeof<sortableDataFormat>) do
        let format = FSharpValue.MakeUnion(case, [||]) :?> sortableDataFormat
        let restored = format |> SortableDataFormat.toString |> SortableDataFormat.fromString
        Assert.Equal<sortableDataFormat>(format, restored)

