namespace Sortopia.Models.Inputs

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Inputs.SortableIntArray

// MsasF = a full bool test set for a given channel count
[<Struct; CustomEquality; NoComparison>]
type msasF =
    private
        { id: Guid<sorterTestModelID>
          channelCount: int<channelCount> }

    static member create
            (channelCount: int<channelCount>)
            : msasF =
        if %channelCount < 2 then
            failwith "ChannelCount must be at least 2"
        else
            let id =
                [
                    box "msasF"
                    box (channelCount |> UMX.untag)
                ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterTestModelID>
            { id = id; channelCount = channelCount; }

    member this.Id with get() = this.id

    member this.ChannelCount with get() = this.channelCount

    override this.Equals(obj) =
        match obj with
        | :? msasF as other ->
            this.channelCount = other.channelCount
        | _ -> false

    override this.GetHashCode() =
        hash (this.channelCount)

    interface IEquatable<msasF> with
        member this.Equals(other) =  this.channelCount = other.channelCount

    member this.MakeSortableBoolTest
                    (sorterTestId: Guid<sortableTestsId>)
                    (channelCount: int<channelCount>) : sortableBinaryTests =
        if channelCount <> this.ChannelCount then invalidArg "channelCount" "Width must match the model."
        let sortableArrays =  SortableBoolArray.getAllSortableBoolArrays channelCount
                              |> Seq.toArray
        sortableBinaryTests.create
                sorterTestId
                channelCount
                sortableArrays


    member this.MakeSortableIntTest
                    (sorterTestId: Guid<sortableTestsId>)
                    (channelCount: int<channelCount>) : sortableIntTests =
        if channelCount <> this.ChannelCount then invalidArg "channelCount" "Width must match the model."
        let sortableArrays =  BinaryIntArrays.getAllBinaryIntArraysForChannelCount channelCount
        sortableIntTests.create
                sorterTestId
                channelCount
                sortableArrays


    member this.MakeSortableBitv512Test
                    (sorterTestId: Guid<sortableTestsId>)
                    (channelCount: int<channelCount>) : sortableBitv512Tests =
        if channelCount <> this.ChannelCount then invalidArg "channelCount" "Width must match the model."
        let grayBlocks = GrayVectorGenerator.getAllSortBlockBitv512ForChannelCount channelCount |> Seq.toArray
        sortableBitv512Tests.create
                sorterTestId
                channelCount
                grayBlocks


    member this.MakeSortableUint8v256Test
                    (sorterTestId: Guid<sortableTestsId>)
                    (channelCount: int<channelCount>) : sortableUint8v256Tests =
        if channelCount <> this.ChannelCount then invalidArg "channelCount" "Width must match the model."
        let sortableArrays =  BinaryIntArrays.getAllBinaryIntArraysForChannelCount channelCount
        SortableUint8v256Tests.fromIntArrays
                sorterTestId
                channelCount
                sortableArrays


    member this.MakeSortableUint8v512Test
                    (sorterTestId: Guid<sortableTestsId>)
                    (channelCount: int<channelCount>) : sortableUint8v512Tests =
        if channelCount <> this.ChannelCount then invalidArg "channelCount" "Width must match the model."
        let sortableArrays =  BinaryIntArrays.getAllBinaryIntArraysForChannelCount channelCount
        SortableUint8v512Tests.fromIntArrays
                sorterTestId
                channelCount
                sortableArrays


module MsasF = ()

