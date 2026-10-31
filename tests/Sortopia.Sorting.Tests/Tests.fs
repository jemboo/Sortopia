module Sortopia.Sorting.Tests.PrefixTests

open System
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

