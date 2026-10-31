namespace Sortopia.Models.Inputs

open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs


type sortableTestsModel =
     | MsasF of msasF      // MsasF = a full bool test set for a given channel count
     | MsasO of msasO      // MsasO = generated from a seed permutation; for bool models, it's expanded from the integer permutations
     | MsasMi of msasM     // All (channel count)/2 merge test cases
     | MsasPfx of msasPfx  // For testing sorters with a fixed prefix - an MsasF that's reduced by the prefix ce's.


module SortableTestsModel =

    let getChannelCount (simpleSortableTM: sortableTestsModel): int<channelCount> =
        match simpleSortableTM with
        | MsasF msasF -> msasF.channelCount
        | MsasO msasO -> %msasO.SeedPermutation.Order |> UMX.tag<channelCount>
        | MsasMi msasMi -> msasMi.channelCount
        | MsasPfx msasPfx -> msasPfx.ChannelCount

    let makeSortableTests
            (sorterTestId: Guid<sortableTestsId>)
            (sortableTM: sortableTestsModel)
            (sortableDF: sortableDataFormat) : sortableTests =

        match sortableTM with

        | MsasF msasF ->
                match sortableDF with
                | sortableDataFormat.BoolArray ->
                    (msasF.MakeSortableBoolTest sorterTestId (getChannelCount sortableTM)) |> sortableTests.Bools
                | sortableDataFormat.IntArray ->
                    (msasF.MakeSortableIntTest sorterTestId (getChannelCount sortableTM)) |> sortableTests.Ints
                | sortableDataFormat.BitVector256 ->
                    failwith "BitVector256 SortableArrayType not supported"
                | sortableDataFormat.BitVector512 ->
                    (msasF.MakeSortableBitv512Test sorterTestId (getChannelCount sortableTM)) |> sortableTests.Bitv512
                | sortableDataFormat.Int8Vector256 ->
                    (msasF.MakeSortableUint8v256Test sorterTestId (getChannelCount sortableTM)) |> sortableTests.Uint8v256
                | sortableDataFormat.Int8Vector512  ->
                    (msasF.MakeSortableUint8v512Test sorterTestId (getChannelCount sortableTM)) |> sortableTests.Uint8v512
                | sortableDataFormat.PackedIntArray ->
                    failwith "PackedIntArray SortableArrayType not supported"

        | MsasO msasO ->
                match sortableDF with
                | sortableDataFormat.BoolArray ->
                     (msasO.MakeSortableBoolTest sorterTestId) |> sortableTests.Bools
                | sortableDataFormat.IntArray ->
                     (msasO.MakeSortableIntTest sorterTestId) |> sortableTests.Ints
                | sortableDataFormat.Int8Vector256 ->
                     failwith "Int8Vector256 SortableArrayType not supported"
                | _ ->
                    failwith "Unsupported SortableArrayType for MsasO"

        | MsasMi msasMi ->
                match sortableDF with
                | sortableDataFormat.BoolArray ->
                    (msasMi.MakeSortableBoolTest sorterTestId) |> sortableTests.Bools
                | sortableDataFormat.IntArray ->
                    (msasMi.MakeSortableIntTest sorterTestId) |> sortableTests.Ints
                | sortableDataFormat.Int8Vector256 ->
                    (msasMi.MakeSortableUint8v256Test sorterTestId) |> sortableTests.Uint8v256
                | sortableDataFormat.Int8Vector512 ->
                    (msasMi.MakeSortableUint8v512Test sorterTestId) |> sortableTests.Uint8v512
                | sortableDataFormat.BitVector512 ->
                    (msasMi.MakeSortableBitv512Test sorterTestId) |> sortableTests.Bitv512
                | _ ->
                    failwith "Unsupported SortableArrayType for MsasMi"

        | MsasPfx msasPfx ->
                match sortableDF with
                | sortableDataFormat.BoolArray ->
                     (msasPfx.MakeSortableBoolTest sorterTestId) |> sortableTests.Bools
                | sortableDataFormat.IntArray ->
                     failwith "IntArray SortableArrayType not supported for MsasPfx"
                | sortableDataFormat.Int8Vector256 ->
                     failwith "Int8Vector256 SortableArrayType not supported"
                | sortableDataFormat.BitVector512 ->
                     msasPfx.MakeSortableBitv512Test sorterTestId |> sortableTests.Bitv512
                | _ ->
                    failwith "Unsupported SortableArrayType for MsasPfx"


    /// Creates a balanced set of sortable tests, with composition controlled by indexPicker.
    let makeSetOfSortableTests
            (sortableTestsId: Guid<sortableTestsId>)
            (indexPicker: int -> int)
            (count: int<sortableTestsCount>)
            (sortableTestsModel: sortableTestsModel)
            (sortableDataFormat: sortableDataFormat) : partitionOfSortableTests =
        makeSortableTests sortableTestsId sortableTestsModel sortableDataFormat
        |> PartitionOfSortableTests.partition indexPicker count
