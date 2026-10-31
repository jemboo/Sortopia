namespace Sortopia.Core.Tests

open FSharp.UMX
open FsUnit.Xunit
open Xunit
open Sortopia.Core
open Sortopia.Core.Combinatorics

type CombinatoricsTests() =

    // Tests for cartesianProductLists
    // Tests for cartesianProductOfArrays
    // Tests for cartesianProductMaps and findMapIndex
    let input: list<string * list<string>> = [
        ("key1", [("desc1"); ("desc2")])
        ("key2", [("desc3"); ("desc4")])
    ]

    [<Fact>]
    let ``cartesianProductMaps with empty input returns empty sequence`` () =
        let result = cartesianProductMaps [] |> Seq.toList
        result |> should be Empty

    [<Fact>]
    let ``cartesianProductMaps with single key returns single-entry maps`` () =
        let input = [("key1", [("desc1"); ("desc2")])]
        let expected = [
            Map.ofList [("key1", ("desc1"))]
            Map.ofList [("key1", ("desc2"))]
        ]
        let result = cartesianProductMaps input |> Seq.toList
        result |> should equal expected

    [<Fact>]
    let ``cartesianProductMaps with multiple keys returns correct combinations`` () =
        let expected = [
            Map.ofList [("key1", ("desc1")); ("key2", ("desc3"))]
            Map.ofList [("key1", ("desc1")); ("key2", ("desc4"))]
            Map.ofList [("key1", ("desc2")); ("key2", ("desc3"))]
            Map.ofList [("key1", ("desc2")); ("key2", ("desc4"))]
        ]
        let result = cartesianProductMaps input |> Seq.toList
        result |> should haveLength 4
        result |> should equal expected

    [<Fact>]
    let ``cartesianProductMaps with empty value list returns empty sequence`` () =
        let input = [("key1", []); ("key2", [("desc3")])]
        let result = cartesianProductMaps input |> Seq.toList
        result |> should be Empty
