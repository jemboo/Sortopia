namespace Sortopia.Permutations.Tests
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Core.Combinatorics


type Perm_RsTests() =

    // Helper function to create a Perm_Rs from an array
    let createPermRs (arr: int array) : permRs =
        permRs.create arr


    // Helper to check if a permutation is self-inverse
    let isSelfInverse (perm: permRs) : bool =
        let arr = perm.Array
        arr |> Array.indexed |> Array.forall (fun (i, x) -> arr.[x] = i)


    //[<Fact>]
    //let ``NoAction mode returns original permutation`` () =
    //    let originalArray = [| 3; 2; 1; 0 |] // Self-inverse permutation
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker [|0; 1|] // Should not be used in NoAction
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.NoAction permRs
    //    Assert.True(result.equals permRs)
    //    Assert.Equal<int array>(originalArray, result.Array)


    //[<Fact>]
    //let ``SS_Pair with SelfSymmetric mode`` () =
    //    let originalArray = [| 3; 2; 1; 0 |] // All self-symmetric pairs: (0,3), (1,2)
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker  [|0; 1|] // Pick pairs (0,3) and (1,2)
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.SelfRefl permRs
    //    Assert.True(isSelfInverse result)
    //    Assert.Equal<int array>(originalArray, result.Array) // SelfSymmetric should not change SS_Pair

    //[<Fact>]
    //let ``SS_Pair with Ortho mode`` () =
    //    let originalArray = [| 3; 2; 1; 0 |]
    //    let expectedArray = [| 1; 0; 3; 2 |]
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker [|0; 1|]// Pick pairs (0,3) and (1,2)
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.Ortho permRs
    //    Assert.True(isSelfInverse result)
    //    Assert.Equal<int array>(expectedArray, result.Array) // Ortho should not change SS_Pair

    //[<Fact>]
    //let ``SS_Pair with Para mode`` () =
    //    let originalArray = [| 3; 2; 1; 0 |]
    //    let expectedArray = [| 2; 3; 0; 1 |]
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker [|0; 1|] // Pick pairs (0,3) and (1,2)
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.Para permRs
    //    Assert.True(isSelfInverse result)
    //    Assert.Equal<int array>(expectedArray, result.Array) // Para should not change SS_Pair


    //[<Fact>]
    //let ``Mixed_Pair with SelfSymmetric mode`` () =
    //    let originalArray = [| 5; 2; 1; 4; 3; 0 |]
    //    let expectedArray = [| 5; 4; 3; 2; 1; 0 |]
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker [|0; 1|] // Pick pairs (0,5) and (1,2)
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.SelfRefl permRs
    //    Assert.True(isSelfInverse result)
    //    Assert.Equal<int array>(expectedArray, result.Array)

    //[<Fact>]
    //let ``Mixed_Pair with Ortho mode`` () =
    //    let originalArray = [| 5; 2; 1; 4; 3; 0 |] // (0,5), (1,2), (3,4)
    //    let expectedArray = [| 5; 2; 1; 4; 3; 0 |]
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker [|0; 1|] // Pick (0,3) and (4,5)
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.Ortho permRs
    //    Assert.True(isSelfInverse result)
    //    Assert.Equal<int array>(expectedArray, result.Array)

    //[<Fact>]
    //let ``Mixed_Pair with Para mode`` () =
    //    let originalArray = [| 5; 2; 1; 4; 3; 0 |] // (0,5), (1,2), (3,4)
    //    let expectedArray = [| 5; 3; 4; 1; 2; 0 |]
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker [|0; 2|] // Pick (0,4) and (1,2)
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.Para permRs
    //    Assert.True(isSelfInverse result)
    //    Assert.Equal<int array>(expectedArray, result.Array)


    //[<Fact>]
    //let ``Mixed_Pair with Para mode another rsTwoOrbit`` () =
    //    let originalArray = [| 11; 2; 1; 8; 5; 4; 7; 6; 3; 10; 9; 0 |]
    //    let expectedArray = [| 8; 2; 1; 11; 5; 4; 7; 6; 0; 10; 9; 3 |]
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker [|0; 2; 0|] // Pick (0,4) and (1,2)
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.Para permRs
    //    Assert.True(isSelfInverse result)
    //    Assert.Equal<int array>(expectedArray, result.Array)


    //[<Fact>]
    //let ``RS_Pair with Ortho mode`` () =
    //    let originalArray = [| 1; 0; 3; 2 |] // (0,1), (2,3)
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker [|0; 1|] // Pick (0,1) and (2,3)
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.Ortho permRs
    //    let expectedArray = [| 1; 0; 3; 2 |] // Ortho swap should preserve for valid pairs
    //    Assert.True(isSelfInverse result)
    //    Assert.Equal<int array>(expectedArray, result.Array)

    //[<Fact>]
    //let ``RS_Pair with Para mode`` () =
    //    let originalArray = [| 1; 0; 3; 2 |] // (0,1), (2,3)
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker [|0; 1|] // Pick (0,1) and (2,3)
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.Para permRs
    //    let expectedArray = [| 2; 3; 0; 1 |] // Para swap should preserve for valid pairs
    //    Assert.True(isSelfInverse result)
    //    Assert.Equal<int array>(expectedArray, result.Array)
         
    //[<Fact>]
    //let ``RS_Pair with SelfSymmetric mode`` () =
    //    let originalArray = [| 1; 0; 3; 2 |] // (0,1), (2,3)
    //    let permRs = permRs.create originalArray
    //    let indexPicker = indexPicker [|0; 1|] // Pick (0,1) and (2,3)
    //    let result = PermRsOps.mutatePerm_Rs indexPicker opsActionMode.SelfRefl permRs
    //    let expectedArray = [| 3; 2; 1; 0 |] // Map 0->3, 1->2, 2->1, 3->0
    //    Assert.True(isSelfInverse result)
    //    Assert.Equal<int array>(expectedArray, result.Array)



    [<Fact>]
    let ``Throws on invalid order less than 4`` () =
        let invalidArray = [| 1; 0 |] // Order 2, too small
        let permRs = permRs.createUnsafe invalidArray // Use unsafe to bypass create validation
        let indexPicker = indexPicker [|0; 1|]
        Assert.ThrowsAny<System.ArgumentException>(fun () ->
            PermRsOps.mutatePerm_Rs indexPicker opsActionMode.SelfRefl permRs |> ignore
        )

