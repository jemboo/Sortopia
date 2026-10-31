namespace Sortopia.Sorting.Inputs

open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting

type partitionOfSortableTests =
    private { sortableTests: Map<string<sortableTestsPartitionId>, sortableTests> }

    static member create (tests: Map<string<sortableTestsPartitionId>, sortableTests>) : partitionOfSortableTests =
        if Map.isEmpty tests then
            invalidArg "tests" "A set of sortable tests must not be empty."

        let first = tests |> Map.toSeq |> Seq.map snd |> Seq.head
        let hasSameType test =
            match first, test with
            | Bitv512 _, Bitv512 _
            | Bools _, Bools _
            | Ints _, Ints _
            | PackedInts _, PackedInts _
            | Uint8v256 _, Uint8v256 _
            | Uint8v512 _, Uint8v512 _ -> true
            | _ -> false

        if tests |> Map.exists (fun _ t -> SortableTests.getChannelCount t <> SortableTests.getChannelCount first) then
            invalidArg "tests" "All partitions must have the same channel count."

        if tests |> Map.toSeq |> Seq.map snd |> Seq.exists (hasSameType >> not) then
            invalidArg "tests" "All sortable tests in a set must have the same type."

        { sortableTests = tests }

    member this.SortableTests = this.sortableTests

    member this.Count = this.sortableTests.Count

module PartitionOfSortableTests =

    let private newId () = System.Guid.NewGuid() |> UMX.tag<sortableTestsId>

    let private partitionItems (indexPicker: int -> int) (count: int) (items: 'a[]) : Map<string<sortableTestsPartitionId>, 'a[]> =
        if count <= 0 then
            invalidArg "count" "Partition count must be greater than zero."
        if count > items.Length then
            invalidArg "count" "Partition count cannot exceed the number of sortable tests."

        let shuffled = Array.copy items
        for index = shuffled.Length - 1 downto 1 do
            let selectedIndex = indexPicker (index + 1)
            if selectedIndex < 0 || selectedIndex > index then
                invalidArg "indexPicker" $"Index {selectedIndex} is outside [0, {index + 1})."
            let selected = shuffled.[selectedIndex]
            shuffled.[selectedIndex] <- shuffled.[index]
            shuffled.[index] <- selected

        [ for partitionIndex in 0 .. count - 1 do
              let values =
                  [| for index in partitionIndex .. count .. shuffled.Length - 1 do
                         yield shuffled.[index] |]
              yield (string partitionIndex) |> UMX.tag<sortableTestsPartitionId>, values ]
        |> Map.ofList

    let partition
            (indexPicker: int -> int)
            (count: int<sortableTestsCount>)
            (tests: sortableTests) : partitionOfSortableTests =
        let partitionCount = %count
        let makeSet makeTests =
            makeTests () |> partitionOfSortableTests.create

        match tests with
        | _ when partitionCount = 1 ->
            Map.ofList [ SortableTestsPartitionId.Default, tests ]
            |> partitionOfSortableTests.create
        | Bitv512 bitv512Tests ->
            let arrays =
                bitv512Tests.SimdSortBlocks
                |> Array.collect SortBlockBitv512.toSortableBoolArrays
            makeSet (fun () ->
                partitionItems indexPicker partitionCount arrays
                |> Map.map (fun _ values ->
                    SortableBitv512Tests.fromBoolArrays (newId ()) bitv512Tests.ChannelCount values
                    |> Bitv512))
        | Bools binaryTests ->
            makeSet (fun () ->
                partitionItems indexPicker partitionCount binaryTests.SortableBinaryArrays
                |> Map.map (fun _ values ->
                    sortableBinaryTests.create (newId ()) binaryTests.ChannelCount values
                    |> Bools))
        | Ints intTests ->
            makeSet (fun () ->
                partitionItems indexPicker partitionCount intTests.SortableIntArrays
                |> Map.map (fun _ values ->
                    sortableIntTests.create (newId ()) intTests.ChannelCount values
                    |> Ints))
        | PackedInts packedTests ->
            let values =
                packedTests.PackedValues
                |> Array.chunkBySize (%packedTests.ChannelCount)
            makeSet (fun () ->
                partitionItems indexPicker partitionCount values
                |> Map.map (fun _ arrays ->
                    arrays
                    |> Array.collect id
                    |> packedSortableIntTests.createFromPackedValues packedTests.ChannelCount
                    |> PackedInts))
        | Uint8v256 uint8v256Tests ->
            let arrays =
                uint8v256Tests.SimdSortBlocks
                |> Array.collect SortBlockUint8v256.toSortableIntArrays
            makeSet (fun () ->
                partitionItems indexPicker partitionCount arrays
                |> Map.map (fun _ values ->
                    SortableUint8v256Tests.fromIntArrays (newId ()) uint8v256Tests.ChannelCount values
                    |> Uint8v256))
        | Uint8v512 uint8v512Tests ->
            let arrays =
                uint8v512Tests.SimdSortBlocks
                |> Array.collect SortBlockUint8v512.toSortableIntArrays
            makeSet (fun () ->
                partitionItems indexPicker partitionCount arrays
                |> Map.map (fun _ values ->
                    SortableUint8v512Tests.fromIntArrays (newId ()) uint8v512Tests.ChannelCount values
                    |> Uint8v512))
