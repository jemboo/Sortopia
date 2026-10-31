namespace Sortopia.Models.Inputs

open System
open System.Runtime.Intrinsics
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Catalog

[<Struct; CustomEquality; NoComparison>]
type msasM =
    private
        { id: Guid<sorterTestModelID>
          mergeDimension: int<mergeDimension>
          mergeFillType: mergeSuffixType
          channelCount: int<channelCount>
          libVariant: sorterLibVariant }

    static member create
            (channelCount: int<channelCount>)
            (mergeDimension: int<mergeDimension>)
            (mergeSuffixType: mergeSuffixType)
            (libVariant: sorterLibVariant)
            : msasM =
        if %channelCount < 2 then
            failwith "ChannelCount must be at least 2"
        if int mergeDimension <= 0 then invalidArg "mergeDimension" "Merge dimension must be positive."
        if (%channelCount) % (%mergeDimension) <> 0 then
            failwith "mergeDimension must evenly divide channelCount"
        else
            let id =
                [
                    box "msasM"
                    box (mergeDimension |> UMX.untag)
                    box (sprintf "%A" mergeSuffixType)
                    box (channelCount |> UMX.untag)
                    box (libVariant |> SorterLibVariant.toString)
                ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterTestModelID>
            {
                id = id;
                mergeDimension = mergeDimension
                mergeFillType = mergeSuffixType
                channelCount = channelCount
                libVariant = libVariant;
            }

    member this.Id with get() = this.id
    member this.MergeDimension with get() = this.mergeDimension
    member this.MergeFillType with get() = this.mergeFillType
    member this.SorterLibVariant with get() = this.libVariant
    member this.ChannelCount with get() = this.channelCount
    member this.SorterLibId with get() = sorterLibId.create (this.ChannelCount / int this.MergeDimension) this.libVariant
    member this.CeArray with get() : ce array option = SorterDataParse.getCeArrayFromMergeLib (mergeLibId.create this.ChannelCount this.MergeDimension this.SorterLibVariant) |> Option.map Array.concat


    override this.Equals(obj) =
        match obj with
        | :? msasM as other ->
            (this.channelCount, this.mergeDimension, this.mergeFillType, this.libVariant) = (other.channelCount, other.mergeDimension, other.mergeFillType, other.libVariant)
        | _ -> false

    override this.GetHashCode() =
        hash (this.channelCount, this.mergeDimension, this.mergeFillType, this.libVariant)

    interface IEquatable<msasM> with
        member this.Equals(other) =  (this.channelCount, this.mergeDimension, this.mergeFillType, this.libVariant) = (other.channelCount, other.mergeDimension, other.mergeFillType, other.libVariant)

    member this.MakeSortableIntTest
                    (sorterTestId: Guid<sortableTestsId>) : sortableIntTests =
        let intArrays = SortableIntArray.getMergeTestCases
                            this.ChannelCount
                            this.MergeDimension
                            this.mergeFillType

        sortableIntTests.create
                sorterTestId
                this.ChannelCount
                intArrays


    member this.MakeSortableBoolTest
                    (sorterTestId: Guid<sortableTestsId>) : sortableBinaryTests =

        let sortableArrays = SortableBoolArray.getMergeTestCases
                                    this.channelCount
                                    this.mergeDimension
                                    this.mergeFillType
                             |> Seq.toArray

        sortableBinaryTests.create
                sorterTestId
                this.channelCount
                sortableArrays


    member this.MakeSortableUint8v256Test
                    (sorterTestId: Guid<sortableTestsId>) : sortableUint8v256Tests =
        let intArrays = SortableIntArray.getMergeTestCases
                            this.ChannelCount
                            this.MergeDimension
                            this.mergeFillType

        SortableUint8v256Tests.fromIntArrays
                sorterTestId
                this.ChannelCount
                intArrays


    member this.MakeSortableUint8v512Test
                    (sorterTestId: Guid<sortableTestsId>) : sortableUint8v512Tests =
        let intArrays = SortableIntArray.getMergeTestCases
                            this.ChannelCount
                            this.MergeDimension
                            this.mergeFillType

        SortableUint8v512Tests.fromIntArrays
                sorterTestId
                this.ChannelCount
                intArrays


    member this.MakeSortableBitv512Test
                    (sorterTestId: Guid<sortableTestsId>) : sortableBitv512Tests =
        let intArrays = SortableIntArray.getMergeTestCases
                            this.ChannelCount
                            this.MergeDimension
                            this.mergeFillType

        let sw = this.ChannelCount

        // Instead of converting all to bool arrays first, we process intArrays in chunks
        let blocks =
            intArrays
            |> Array.chunkBySize 512
            |> Array.collect (fun (chunk: sortableIntArray[]) ->
                let inputCount = chunk.Length
                let numThresholds = %sw + 1

                // We are building a sequence of blocks.
                // Since one chunk of 512 intArrays actually represents 512 * (width+1) bool cases,
                // we yield multiple simdSortBlocks.
                [|
                    for threshold = 0 to %sw do
                        let vecs = Array.init %sw (fun wireIdx ->
                            let buffer = Array.zeroCreate<uint64> 8
                            for testIdx = 0 to inputCount - 1 do
                                // Logic: thresholding the int value directly into the bit buffer
                                if chunk.[testIdx].[wireIdx] >= threshold then
                                    let lane = testIdx / 64
                                    let bit = testIdx % 64
                                    buffer.[lane] <- buffer.[lane] ||| (1uL <<< bit)

                            Vector512.Create(
                                buffer.[0], buffer.[1], buffer.[2], buffer.[3],
                                buffer.[4], buffer.[5], buffer.[6], buffer.[7]
                            )
                        )
                        sortBlockBitv512.createFromVectors vecs inputCount
                |]
            )

        sortableBitv512Tests.create sorterTestId sw blocks


module MsasMi = ()

