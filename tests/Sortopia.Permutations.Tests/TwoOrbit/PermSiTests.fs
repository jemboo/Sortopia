namespace Sortopia.Permutations.Tests.TwoOrbit
open System
open FsUnit.Xunit
open Xunit
open Sortopia.Core
open Sortopia.Permutations


type Perm_SiTests() =
    // Tests for PermSi.getTwoOrbits
    [<Fact>]
    let ``PermSi.getTwoOrbits with valid order 4 permutation returns correct TwoOrbits`` () =
        let perm_Rs = permSi.create [|1; 0; 3; 2|]
        let result = PermSi.getTwoOrbits perm_Rs
        let expected = [|
            twoOrbit.create [0; 1]
            twoOrbit.create [2; 3]
        |]
        Assert.Equal<twoOrbit array>(expected, result)

    [<Fact>]
    let ``PermSi.getTwoOrbits with different valid order 4 permutation returns correct TwoOrbits`` () =
        let perm_Rs = permSi.create  [|2; 3; 0; 1|]
        let result = PermSi.getTwoOrbits perm_Rs
        let expected = [|
            twoOrbit.create [0; 2]
            twoOrbit.create [1; 3]
        |]
        Assert.Equal<twoOrbit array>(expected, result)

    [<Fact>]
    let ``PermSi.getTwoOrbits with odd order throws exception`` () =
        let perm_Rs = permSi.create [|1; 0; 2|]
        let ex = Assert.Throws<Exception>(fun () -> PermSi.getTwoOrbits perm_Rs |> ignore)
        Assert.Equal("PermSi order must be non-negative and even", ex.Message)


    [<Fact>]
    let ``PermSi.makeReflection`` () =
        let perm_Rs = permSi.create [|1; 0; 2; 3; 5; 4|]
        let reflPerm = perm_Rs |> PermSi.makeReflection
        Assert.Equal(reflPerm |> PermSi.isReflectionSymmetric, true)