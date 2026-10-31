namespace Sortopia.Permutations.Tests
open System
open FsUnit.Xunit
open Xunit
open Sortopia.Core
open Sortopia.Permutations

open Sortopia.Permutations.Permutation

type PermutationTests() =

    [<Fact>]
    let ``Identity permutation is valid`` () =
        let id = identity 4
        Assert.Equal<int>([|0; 1; 2; 3|], id.Array)
        Assert.True(Array.forall2 (fun i v -> i = v) [|0 .. 3|] (Array.sort id.Array))

    [<Fact>]
    let ``Rotated identity by 1 is correct`` () =
        let r = rotated 1 4
        Assert.Equal<int>([|1; 2; 3; 0|], r.Array) // Expected: (0 1 2 3)

    [<Fact>]
    let ``Rotated identity by -1 is correct`` () =
        let r = rotated -1 4
        Assert.Equal<int>([|3; 0; 1; 2|], r.Array) // Expected: (0 3 2 1)

    [<Fact>]
    let ``Rotated identity by 0 is identity`` () =
        let r = rotated 0 4
        Assert.Equal<int>([|0; 1; 2; 3|], r.Array) // Expected: (0)(1)(2)(3)

    [<Fact>]
    let ``Rotated identity by n is identity`` () =
        let r = rotated 4 4
        Assert.Equal<int>([|0; 1; 2; 3|], r.Array) // Expected: (0)(1)(2)(3)

    [<Fact>]
    let ``Rotated with invalid order throws exception`` () =
        Assert.Throws<System.Exception>(fun () -> rotated 1 0 |> ignore)

    [<Fact>]
    let ``Composition of two permutations is correct`` () =
        let p = permutation.create [|1; 2; 0; 3|] // (0 1 2)(3)
        let q = permutation.create [|1; 0; 3; 2|] // (0 1)(2 3)
        let result = compose p q
        Assert.Equal<int>([|0; 3; 1; 2|], result.Array) // Expected: (0)(1 3 2)

    [<Fact>]
    let ``Composition with mismatched orders throws exception`` () =
        let p = permutation.create [|1; 2; 0|]
        let q = permutation.create [|1; 0; 3; 2|]
        Assert.Throws<System.Exception>(fun () -> compose p q |> ignore)

    [<Fact>]
    let ``Inverse of permutation is correct`` () =
        let p = permutation.create [|1; 2; 0; 3|] // (0 1 2)(3)
        let inv = inverse p
        Assert.Equal<int>([|2; 0; 1; 3|], inv.Array) // Expected: (0 2 1)(3)
        Assert.Equal<int>((identity 4).Array, (compose p inv).Array)
        Assert.Equal<int>((identity 4).Array, (compose inv p).Array)

    [<Fact>]
    let ``Conjugate of permutation is correct`` () =
        let p = permutation.create [|1; 2; 0; 3|] // (0 1 2)(3)
        let q = permutation.create [|1; 0; 3; 2|] // (0 1)(2 3)
        let result = Permutation.conjugate p q
        Assert.Equal<int>([|3; 0; 2; 1|], result.Array) // Expected: (0 3 1)(2)

    [<Fact>]
    let ``Conjugate with mismatched orders throws exception`` () =
        let p = permutation.create [|1; 2; 0|]
        let q = permutation.create [|1; 0; 3; 2|]
        Assert.Throws<System.Exception>(fun () -> Permutation.conjugate p q |> ignore)
