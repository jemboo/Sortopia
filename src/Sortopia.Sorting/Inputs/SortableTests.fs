namespace Sortopia.Sorting.Inputs

open FSharp.UMX
open Sortopia.Sorting

type sortableTests = 
    | Bitv512 of sortableBitv512Tests
    | Bools of sortableBinaryTests
    | Ints of sortableIntTests
    | PackedInts of packedSortableIntTests
    | Uint8v256 of sortableUint8v256Tests
    | Uint8v512 of sortableUint8v512Tests

module SortableTests = 

    let getId (test: sortableTests) : Guid<sortableTestsId> =
        match test with
        | Bitv512 bitv512Test -> bitv512Test.Id
        | Bools boolTest -> boolTest.Id
        | Ints intTest -> intTest.Id
        | PackedInts packedIntTest -> packedIntTest.Id
        | Uint8v256 uint8v256Test -> uint8v256Test.Id
        | Uint8v512 uint8v512Test -> uint8v512Test.Id

    let getChannelCount (test: sortableTests) =
        match test with
        | Bitv512 bitv512Test -> bitv512Test.ChannelCount
        | Bools boolTest -> boolTest.ChannelCount
        | Ints intTest -> intTest.ChannelCount
        | PackedInts packedIntTest -> packedIntTest.ChannelCount
        | Uint8v256 uint8v256Test -> uint8v256Test.ChannelCount
        | Uint8v512 uint8v512Test -> uint8v512Test.ChannelCount

    let getSortableCount (test: sortableTests) : int<sortableCount> =
        match test with
        | Bitv512 bitv512Test -> bitv512Test.SortableCount
        | Bools boolTest -> boolTest.SortableCount
        | Ints intTest -> intTest.SortableCount
        | PackedInts packedIntTest -> packedIntTest.SortableCount
        | Uint8v256 uint8v256Test -> uint8v256Test.SortableCount
        | Uint8v512 uint8v512Test -> uint8v512Test.SortableCount

    let mergeSortableTests (test1: sortableTests) (test2: sortableTests) : sortableTests =
        let requireMatchingChannelCounts channelCount1 channelCount2 =
            if channelCount1 <> channelCount2 then
                invalidArg "test2" "Sortable tests must have the same channel count."

        match test1, test2 with
        | Bitv512 bitv512Test1, Bitv512 bitv512Test2 ->
            requireMatchingChannelCounts bitv512Test1.ChannelCount bitv512Test2.ChannelCount
            sortableBitv512Tests.create
                (System.Guid.NewGuid() |> UMX.tag<sortableTestsId>)
                bitv512Test1.ChannelCount
                (Array.append bitv512Test1.SimdSortBlocks bitv512Test2.SimdSortBlocks)
            |> Bitv512
        | Bools boolTest1, Bools boolTest2 ->
            requireMatchingChannelCounts boolTest1.ChannelCount boolTest2.ChannelCount
            sortableBinaryTests.create
                (System.Guid.NewGuid() |> UMX.tag<sortableTestsId>)
                boolTest1.ChannelCount
                (Array.append boolTest1.SortableBinaryArrays boolTest2.SortableBinaryArrays)
            |> Bools
        | Ints intTest1, Ints intTest2 ->
            requireMatchingChannelCounts intTest1.ChannelCount intTest2.ChannelCount
            sortableIntTests.create
                (System.Guid.NewGuid() |> UMX.tag<sortableTestsId>)
                intTest1.ChannelCount
                (Array.append intTest1.SortableIntArrays intTest2.SortableIntArrays)
            |> Ints
        | PackedInts packedIntTest1, PackedInts packedIntTest2 ->
            requireMatchingChannelCounts packedIntTest1.ChannelCount packedIntTest2.ChannelCount
            packedSortableIntTests.createFromPackedValues
                packedIntTest1.ChannelCount
                (Array.append packedIntTest1.PackedValues packedIntTest2.PackedValues)
            |> PackedInts
        | Uint8v256 uint8v256Test1, Uint8v256 uint8v256Test2 ->
            requireMatchingChannelCounts uint8v256Test1.ChannelCount uint8v256Test2.ChannelCount
            sortableUint8v256Tests.create
                (System.Guid.NewGuid() |> UMX.tag<sortableTestsId>)
                uint8v256Test1.ChannelCount
                (Array.append uint8v256Test1.SimdSortBlocks uint8v256Test2.SimdSortBlocks)
            |> Uint8v256
        | Uint8v512 uint8v512Test1, Uint8v512 uint8v512Test2 ->
            requireMatchingChannelCounts uint8v512Test1.ChannelCount uint8v512Test2.ChannelCount
            sortableUint8v512Tests.create
                (System.Guid.NewGuid() |> UMX.tag<sortableTestsId>)
                uint8v512Test1.ChannelCount
                (Array.append uint8v512Test1.SimdSortBlocks uint8v512Test2.SimdSortBlocks)
            |> Uint8v512
        | _ -> failwith "Cannot merge tests of different types."
