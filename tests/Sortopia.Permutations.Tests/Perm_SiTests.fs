namespace Sortopia.Permutations.Tests
open System
open FSharp.UMX
open FsUnit.Xunit
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Core.Combinatorics

open Sortopia.Permutations.PermSi
open Sortopia.Permutations.Permutation

type PermSiTests() =

    let parseIntArray (s: string) : int[] =
        s.Split(',')
        |> Array.map (fun n -> int (n.Trim()))

    [<Fact>]
    let ``Perm_Rs counts are preserved by conjugation`` () =
        let value = permSi.create [|1; 0; 3; 2; 4|]
        let by = permutation.create [|2; 3; 4; 0; 1|]
        let result = PermSi.conjugate value by
        let fixedPoints = result.Array |> Array.indexed |> Array.filter (fun (i, image) -> i = image)
        Assert.Single(fixedPoints) |> ignore
        Assert.True(Permutation.isSelfInverse result.Permutation)

    [<Fact>]
    let ``Even order creates maximum transpositions correctly`` () =
        let perm_Rs = saturatedWithTwoCycles 4
        // Expected: [(0,1), (2,3)] -> [1; 0; 3; 2]
        let expected = permSi.createUnsafe [|1; 0; 3; 2|]
        perm_Rs.Permutation.Array |> should equal expected.Permutation.Array

    [<Fact>]
    let ``Odd order creates maximum transpositions with one fixed point`` () =
        let perm_Rs = saturatedWithTwoCycles 5
        // Expected: [(0,1), (2,3)] -> [1; 0; 3; 2; 4]
        let expected = permSi.createUnsafe [|1; 0; 3; 2; 4|]
        perm_Rs.Permutation.Array |> should equal expected.Permutation.Array

    [<Fact>]
    let ``Order 2 creates one transposition`` () =
        let perm_Rs = saturatedWithTwoCycles 2
        // Expected: [(0,1)] -> [1; 0]
        let expected = permSi.createUnsafe [|1; 0|]
        perm_Rs.Permutation.Array |> should equal expected.Permutation.Array

    [<Fact>]
    let ``Order 1 creates empty Perm_Rs with one fixed point`` () =
        let perm_Rs = saturatedWithTwoCycles 1
        // Expected: [] -> [0]
        let expected = permSi.createUnsafe [|0|]
        perm_Rs.Permutation.Array |> should equal expected.Permutation.Array

    [<Fact>]
    let ``Negative order throws exception`` () =
        let ex = Assert.Throws<System.Exception>(fun () -> saturatedWithTwoCycles -1 |> ignore)
        ex.Message |> should equal "PermSi order must not be negative"

    [<Fact>]
    let ``Perm_Rs Reflection`` () =
        let orig = permSi.create [|2; 6; 0; 4; 3; 7; 1; 5|]
        let refl = PermSi.makeReflection orig
        let reflA = PermSi.makeReflection refl
        reflA.Permutation.Array |> should equal orig.Permutation.Array
        orig |> PermSi.isReflectionSymmetric |> should equal true
        let origB = permSi.create [|1; 0; 6; 4; 3; 7; 2; 5|]
        origB |> PermSi.isReflectionSymmetric |> should equal false

    [<Fact>]
    let ``Perm_Rs Reflection is it's own inverse`` () =
        let origA = permSi.createUnsafe [|1; 0; 3; 2; 4|]
        let reflA = PermSi.makeReflection origA
        let reflA2 = PermSi.makeReflection reflA
        reflA2.Permutation.Array |> should equal origA.Permutation.Array

        let origB = permSi.createUnsafe [|7; 1; 5; 0; 3; 6; 2; 4|]
        let reflB = PermSi.makeReflection origB
        let reflB2 = PermSi.makeReflection reflB
        reflB2.Permutation.Array |> should equal origB.Permutation.Array

    // Helper function to parse a string of comma-separated integers (e.g., "11,2,15")
    let parseIntArray (s: string) : int[] =
        s.Split(',')
        |> Array.map (fun n -> int (n.Trim()))

    //[<Fact>]
    //let ``mutatePerm_Sis with None mode returns original permutation`` () =
    //    let perm = permSi.create [|1; 0; 2; 3|] // (0 1)
    //    let mockIndexPicker = indexPicker [|0; 1|]
    //    let result = mutate mockIndexPicker MutationMode.NoAction perm
    //    Assert.True(perm.equals result)
    //    Assert.Equal<int array>(perm.Array, result.Array)

    //[<Fact>]
    //let ``mutatePerm_Sis with same orbit indices returns original permutation`` () =
    //    let perm = permSi.create [|1; 0; 2; 3|] // (0 1)
    //    let mockIndexPicker = indexPicker [|0; 1|] // Picks indices in the same orbit
    //    let resultOrtho = mutate mockIndexPicker MutationMode.Ortho perm
    //    let resultPara = mutate mockIndexPicker MutationMode.Para perm
    //    Assert.True(perm.equals resultOrtho)
    //    Assert.True(perm.equals resultPara)
    //    Assert.Equal<int array>(perm.Array, resultOrtho.Array)
    //    Assert.Equal<int array>(perm.Array, resultPara.Array)

    //[<Fact>]
    //let ``mutatePerm_Sis with Ortho mode produces valid self-inverse permutation`` () =
    //    let perm = permSi.create [|1; 0; 3; 2|] // (0 1)(2 3)
    //    let mockIndexPicker = indexPicker [|0; 2|] // Picks indices 0 and 2
    //    let result = mutate mockIndexPicker MutationMode.Ortho perm
    //    let expectedArray = [|2; 3; 0; 1|] // Expected: (0 2)(1 3)
    //    Assert.Equal<int array>(expectedArray, result.Array)
    //    Assert.True(Permutation.isSelfInverse result.Permutation)
    //    Assert.Equal(UMX.tag<Order> 4, result.Order)

    //[<Fact>]
    //let ``mutatePerm_Sis with Para mode produces valid self-inverse permutation`` () =
    //    let perm = permSi.create [|1; 0; 3; 2|] // (0 1)(2 3)
    //    let mockIndexPicker = indexPicker [|0; 2|] // Picks indices 0 and 2
    //    let result = mutate mockIndexPicker MutationMode.Para perm
    //    let expectedArray = [|3; 2; 1; 0|] // Expected: (0 3)(1 2)
    //    Assert.Equal<int array>(expectedArray, result.Array)
    //    Assert.True(Permutation.isSelfInverse result.Permutation)
    //    Assert.Equal(UMX.tag<Order> 4, result.Order)

    //[<Fact>]
    //let ``mutatePerm_Sis with Ortho mode swaps correctly for non-adjacent indices`` () =
    //    let perm = permSi.create [|1; 0; 3; 2; 5; 4|] // (0 1)(2 3)(4 5)
    //    let mockIndexPicker = indexPicker [|1; 4|] // Picks indices 1 and 4
    //    let result = mutate mockIndexPicker MutationMode.Ortho perm
    //    let expectedArray = [|4; 5; 3; 2; 0; 1|] // Expected: (0 1)(2 3)(4 5)
    //    Assert.Equal<int array>(expectedArray, result.Array)
    //    Assert.True(Permutation.isSelfInverse result.Permutation)

    //[<Fact>]
    //let ``permSi with Para mode swaps correctly for non-adjacent indices`` () =
    //    let perm = permSi.create [|1; 0; 3; 2; 5; 4|] // (0 1)(2 3)(4 5)
    //    let mockIndexPicker = indexPicker [|1; 4|] // Picks indices 1 and 4
    //    let result = mutate mockIndexPicker MutationMode.Para perm
    //    let expectedArray = [|5; 4; 3; 2; 1; 0|] // Expected: (0 5)(2 3)(1 4)
    //    Assert.Equal<int array>(expectedArray, result.Array)
    //    Assert.True(Permutation.isSelfInverse result.Permutation)

    [<Fact>]
    let ``mutatePerm_Sis preserves order of permutation`` () =
        let perm = permSi.create [|1; 0; 2; 3; 4; 5|] // (0 1)
        let mockIndexPicker = indexPicker [|2; 4|]
        let resultOrtho = mutate mockIndexPicker MutationMode.Ortho perm
        let resultPara = mutate mockIndexPicker MutationMode.Para perm
        Assert.Equal(UMX.tag<Order> 6, resultOrtho.Order)
        Assert.Equal(UMX.tag<Order> 6, resultPara.Order)

    [<Fact>]
    let ``mutatePerm_Sis throws on invalid input permutation`` () =
        let invalidPerm = permutation.createUnsafe [|1; 1; 2; 3|] // Not a valid permutation
        Assert.Throws<Exception>(fun () -> permSi.create invalidPerm.Array |> ignore)
