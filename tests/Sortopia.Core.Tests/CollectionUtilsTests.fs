namespace Sortopia.Core.Tests

open FsUnit.Xunit
open Xunit
open Sortopia.Core.CollectionUtils

type CollectionUtilsTests() =

    //pairWithNext
    [<Fact>]
    let ``pairWithNext with two integers returns a pair`` () =
        let input = seq [1; 2;]
        let result = pairWithNext input |> Seq.toList
        let expected = [(1, Some 2)]
        Assert.Equal<(int * int option) list>(expected, result)

    [<Fact>]
    let ``pairWithNext with three integers returns correct pairs`` () =
        let input = seq [1; 2; 3]
        let result = pairWithNext input |> Seq.toList
        let expected = [(1, Some 2); (3, None)]
        Assert.Equal<(int * int option) list>(expected, result)

    [<Fact>]
    let ``pairWithNext with single integer returns single pair with None`` () =
        let input = seq [42]
        let result = pairWithNext input |> Seq.toList
        let expected = [(42, None)]
        Assert.Equal<(int * int option) list>(expected, result)

    [<Fact>]
    let ``pairWithNext with empty sequence returns empty sequence`` () =
        let input = Seq.empty<int>
        let result = pairWithNext input |> Seq.toList
        Assert.Empty(result)

    [<Fact>]
    let ``pairWithNext with strings returns correct pairs`` () =
        let input = seq ["a"; "b"; "c"; "d"]
        let result = pairWithNext input |> Seq.toList
        let expected = [("a", Some "b"); ("c", Some "d");]
        Assert.Equal<(string * string option) list>(expected, result)

    [<Fact>]
    let ``pairWithNext with large sequence returns correct pairs`` () =
        let input = seq { 1 .. 11 }
        let result = pairWithNext input |> Seq.toList
        let expected = [ for i in 1 .. 5 -> (2*i - 1, Some (2*i)) ] @ [(11, None)]
        Assert.Equal<(int * int option) list>(expected, result)
