namespace Sortopia.Evaluation.Tests

open System
open FSharp.UMX
open FsUnit.Xunit   
open Xunit
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Evaluation

type CeOpsTests() =

    let createCe (low: int) (hi: int) =
       ce.create low hi

    [<Fact>]
    let ``evalWithSorterTests handles bool arrays and removes duplicates`` () =
        // Arrange
        let channelCount = 3<channelCount>
        let boolArrays = [|
            sortableBoolArray.create([| true; false; true |], channelCount)
            sortableBoolArray.create([| false; true; false |], channelCount)
            sortableBoolArray.create([| true; false; true |], channelCount) // Duplicate
            sortableBoolArray.create([| false; false; true |], channelCount) // Already sorted
        |]
        let sortableTests = 
                sortableBinaryTests.create 
                        (Guid.NewGuid() |> UMX.tag<sortableTestsId>)
                        channelCount
                        boolArrays |> sortableTests.Bools
        let ceBlk = ceBlock.create (Guid.NewGuid() |> UMX.tag<ceBlockId>) channelCount [| createCe 0 1 |]
        
        // Act
        let collectNewSortableTests = true |> UMX.tag<collectNewSortableTests>
        let ceBlockEval = CeBlockOps.evalWithSorterTest sortableTests ceBlock.Empty ceBlk collectNewSortableTests
        
        // Assert
        let boolTests = match (ceBlockEval).SortableTests.Value with | sortableTests.Bools bt -> bt | _ -> failwith "Expected Bools"
        boolTests.SortableBinaryArrays.Length |> should equal 1 // Duplicate removed
        boolTests.SortableBinaryArrays |> Array.forall (fun sba -> sba.Values = [| false; true; false |]) |> should be True
        %ceBlockEval.CeLength |> should be (greaterThanOrEqualTo 1) // At least one swap occurred

    [<Fact>]
    let ``evalWithSorterTests handles int arrays and removes duplicates`` () =
        // Arrange
        let channelCount = 3<channelCount>
        let symbolSetSize = 2<symbolSetSize>
        let intArrays = [|
            sortableIntArray.create([| 1; 0; 1 |], channelCount, symbolSetSize)
            sortableIntArray.create([| 0; 1; 0 |], channelCount, symbolSetSize)
            sortableIntArray.create([| 1; 0; 1 |], channelCount, symbolSetSize) // Duplicate
            sortableIntArray.create([| 0; 0; 1 |], channelCount, symbolSetSize) // Already sorted
        |]
        let sortableTests = 
            sortableIntTests.create 
                (Guid.NewGuid() |> UMX.tag<sortableTestsId>) 
                channelCount
                intArrays |> sortableTests.Ints

        let ceBlk = ceBlock.create (Guid.NewGuid() |> UMX.tag<ceBlockId>) channelCount [| createCe 0 1 |]
        
        // Act
        let collectNewSortableTests = true |> UMX.tag<collectNewSortableTests>
        let ceBlockEval = CeBlockOps.evalWithSorterTest sortableTests ceBlock.Empty ceBlk collectNewSortableTests
        
        // Assert
        let intTests = match (ceBlockEval).SortableTests.Value with | sortableTests.Ints it -> it | _ -> failwith "Expected Ints"
        intTests.SortableIntArrays.Length |> should equal 1
        %ceBlockEval.CeLength |> should be (greaterThanOrEqualTo 1) // At least one swap occurred



    [<Fact>]
    let ``evalWithSorterTests preserves channel count and type`` () =
        // Arrange
        let channelCount = 2<channelCount>
        let symbolSetSize = 2<symbolSetSize>
        let intArrays = [| sortableIntArray.create([| 1; 0 |], channelCount, symbolSetSize) |]
        let sortableTests = (sortableIntTests.create 
                                (Guid.NewGuid() |> UMX.tag<sortableTestsId>)
                                channelCount
                                intArrays ) |> sortableTests.Ints

        let ceBlk = ceBlock.create (Guid.NewGuid() |> UMX.tag<ceBlockId>) channelCount [| createCe 0 1 |]

        // Act
        let collectNewSortableTests = true |> UMX.tag<collectNewSortableTests>
        let result = CeBlockOps.evalWithSorterTest sortableTests ceBlock.Empty ceBlk collectNewSortableTests
        
        // Assert
        let intTests = match result.SortableTests.Value with | sortableTests.Ints it -> it | _ -> failwith "Expected Ints"
        intTests.ChannelCount |> should equal channelCount
        intTests.SortableDataFormat |> should equal sortableDataFormat.IntArray
    [<Fact>]
    let ``single network honors collection flag for every format`` () =
        let width = 3<channelCount>
        let id () = Guid.NewGuid() |> UMX.tag<sortableTestsId>
        let ints = [| sortableIntArray.create([| 0; 1; 0 |], width, 2<symbolSetSize>) |]
        let bools = [| sortableBoolArray.create([| false; true; false |], width) |]
        let tests = [|
            sortableIntTests.create (id ()) width ints |> sortableTests.Ints
            sortableBinaryTests.create (id ()) width bools |> sortableTests.Bools
            packedSortableIntTests.create width ints |> sortableTests.PackedInts
            SortableUint8v256Tests.fromIntArrays (id ()) width ints |> sortableTests.Uint8v256
            SortableUint8v512Tests.fromIntArrays (id ()) width ints |> sortableTests.Uint8v512
            SortableBitv512Tests.fromBoolArrays (id ()) width bools |> sortableTests.Bitv512
        |]
        let network = ceBlock.create (Guid.NewGuid() |> UMX.tag) width [| createCe 0 1 |]
        for test in tests do
            let countsOnly = CeBlockOps.evalWithSorterTest test ceBlock.Empty network (UMX.tag false)
            countsOnly.SortableTests.IsNone |> should be True
            %countsOnly.UnsortedCount |> should equal 1
            let collected = CeBlockOps.evalWithSorterTest test ceBlock.Empty network (UMX.tag true)
            collected.SortableTests.IsSome |> should be True
            %collected.UnsortedCount |> should equal 1

    [<Theory>]
    [<InlineData(32)>]
    [<InlineData(64)>]
    let ``uint8 collection returns exactly unsorted lanes and deduplicates`` (lanes: int) =
        let width = 4<channelCount>
        let sorted = [| 0; 0; 1; 1 |]
        let earlyFailure = [| 1; 0; 1; 1 |]
        let lateFailure = [| 0; 0; 255; 128 |]
        // Cover both ends of a full SIMD block and a partially filled next block.
        let values = Array.init (lanes + 3) (fun i ->
            if i = 0 || i = lanes then earlyFailure
            elif i = lanes - 1 then lateFailure
            else sorted)
        let arrays = values |> Array.map (fun a -> sortableIntArray.create(a, width, 256<symbolSetSize>))
        let id = Guid.NewGuid() |> UMX.tag<sortableTestsId>
        let tests =
            if lanes = 32 then SortableUint8v256Tests.fromIntArrays id width arrays |> sortableTests.Uint8v256
            else SortableUint8v512Tests.fromIntArrays id width arrays |> sortableTests.Uint8v512
        let network = ceBlock.create (Guid.NewGuid() |> UMX.tag) width [||]
        let result = CeBlockOps.evalWithSorterTest tests ceBlock.Empty network (UMX.tag true)
        %result.UnsortedCount |> should equal 2
        match result.SortableTests with
        | Some (sortableTests.Ints failures) ->
            let actual = failures.SortableIntArrays |> Array.map (fun a -> a.Values |> Array.toList) |> Set.ofArray
            actual |> should equal (Set.ofList [Array.toList earlyFailure; Array.toList lateFailure])
        | _ -> failwith "Expected collected integer failures"
