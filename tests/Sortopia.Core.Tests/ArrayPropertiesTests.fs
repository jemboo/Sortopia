namespace Sortopia.Core.Tests

open System
open FsUnit.Xunit
open Xunit
open Sortopia.Core.ArrayUtils

type ArrayPropertiesTests() =

    // Tests for distanceSquared
    [<Fact>]
    let ``distanceSquared with equal float arrays returns zero`` () =
        let a = [|0.0; 0.0; 0.0|]
        let b = [|0.0; 0.0; 0.0|]
        let result = distanceSquared a b
        result |> should equal 0.0

    [<Fact>]
    let ``distanceSquared with different float arrays returns correct sum`` () =
        let a = [|1.0; 2.0; 3.0|]
        let b = [|0.0; 0.0; 0.0|]
        let result = distanceSquared a b
        result |> should equal 14.0  // 1^2 + 2^2 + 3^2 = 1 + 4 + 9 = 14

    [<Fact>]
    let ``distanceSquared with integer arrays returns correct sum`` () =
        let a = [|1; 2; 3|]
        let b = [|0; 0; 0|]
        let result = distanceSquared a b
        result |> should equal 14

    // Tests for distanceSquaredOffset
    [<Fact>]
    let ``distanceSquaredOffset with matching arrays returns zero array`` () =
        let longArray = [|0.0; 0.0; 0.0; 0.0|]
        let shortArray = [|0.0; 0.0|]
        let result = distanceSquaredOffset longArray shortArray
        result |> should equal <| [|0.0; 0.0|]

    [<Fact>]
    let ``distanceSquaredOffset with float arrays returns correct distances`` () =
        let longArray = [|1.0; 0.0; 0.0; 1.0|]
        let shortArray = [|0.0; 0.0|]
        let result = distanceSquaredOffset longArray shortArray
        result |> should equal [|1.0; 1.0|]  // [(1-0)^2 + (0-0)^2; (0-0)^2 + (1-0)^2]

    [<Fact>]
    let ``distanceSquaredOffset with integer arrays returns correct distances`` () =
        let longArray = [|1; 0; 0; 1|]
        let shortArray = [|0; 0|]
        let result = distanceSquaredOffset longArray shortArray
        result |> should equal [|1; 1|]

    // Tests for isSorted
    [<Fact>]
    let ``isSorted with sorted array returns true`` () =
        let values = [|1; 2; 3; 4|]
        isSorted values |> should equal true

    [<Fact>]
    let ``isSorted with unsorted array returns false`` () =
        let values = [|1; 3; 2; 4|]
        isSorted values |> should equal false

    [<Fact>]
    let ``isSorted with empty array returns true`` () =
        let values = [||]
        isSorted values |> should equal true

    [<Fact>]
    let ``isSorted with single element returns true`` () =
        let values = [|1|]
        isSorted values |> should equal true

    [<Fact>]
    let ``isSorted with null array throws exception`` () =
        let values: int[] = null
        (fun () -> isSorted values |> ignore) |> should throw typeof<System.Exception>
