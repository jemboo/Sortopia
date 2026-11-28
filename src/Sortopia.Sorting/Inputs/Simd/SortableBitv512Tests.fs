namespace Sortopia.Sorting.Inputs

open System
open System.Runtime.Intrinsics
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Sorting

[<Struct>]
type sortableBitv512Tests =
    private { 
        id: Guid<sortableTestsId>
        channelCount: int<channelCount>
        simdSortBlocks: sortBlockBitv512[] 
    }

    static member create 
            (id: Guid<sortableTestsId>) 
            (sw: int<channelCount>)
            (blocks: sortBlockBitv512[]) =
        if isNull blocks then nullArg "blocks"
        if int sw < 0 then invalidArg "sw" "Channel count must be non-negative."
        if blocks |> Array.exists (fun b -> b.Length <> int sw) then
            invalidArg "blocks" "Every block must match the dataset channel count."
        { id = id
          channelCount = sw
          simdSortBlocks = Array.copy blocks }

    static member Empty =
        let id = Guid.NewGuid() |> UMX.tag<sortableTestsId>
        sortableBitv512Tests.create id 0<channelCount> [||]

    member this.Id with get() = this.id

    member this.SimdSortBlocks with get() = Array.copy this.simdSortBlocks
    
    member this.SortableDataFormat with get() = sortableDataFormat.BitVector512
    
    member this.SortableCount with get() = 
        this.simdSortBlocks 
        |> Array.sumBy (fun block -> block.SortableCount) 
        |> UMX.tag<sortableCount>
        
    member this.ChannelCount with get() = this.channelCount

module SortableBitv512Tests =

    /// Pack a single pass of inputs while retaining at most 512 Boolean rows.
    /// Preserve input order, duplicates, and the exact active-lane count.
    let fromBoolSequence
        (id: Guid<sortableTestsId>)
        (sw: int<channelCount>)
        (sortableBoolArrays: seq<sortableBoolArray>) : sortableBitv512Tests =
        
        if isNull sortableBoolArrays then nullArg "sortableBoolArrays"
        if int sw < 0 then invalidArg "sw" "Channel count must be non-negative."
        let width = %sw
        let blocks = ResizeArray<sortBlockBitv512>()
        let chunk = Array.zeroCreate<sortableBoolArray> 512
        let buffer = Array.zeroCreate<uint64> 8
        use inputs = sortableBoolArrays.GetEnumerator()
        let mutable finished = false
        while not finished do
            let mutable count = 0
            while count < chunk.Length && not finished do
                if inputs.MoveNext() then
                    let value = inputs.Current
                    if value.ChannelCount <> sw then
                        invalidArg "sortableBoolArrays" "Every input must match the dataset channel count."
                    chunk[count] <- value
                    count <- count + 1
                else finished <- true
            if count > 0 then
                let vectors = Array.init width (fun wire ->
                    Array.Clear buffer
                    for index = 0 to count - 1 do
                        if chunk[index].Item(wire) then
                            let lane = index / 64
                            buffer[lane] <- buffer[lane] ||| (1UL <<< (index % 64))
                    Vector512.Create(buffer[0], buffer[1], buffer[2], buffer[3],
                                     buffer[4], buffer[5], buffer[6], buffer[7]))
                blocks.Add(sortBlockBitv512.createFromVectors vectors count)
                Array.Clear chunk
        sortableBitv512Tests.create id sw (blocks.ToArray())

    let fromBoolArrays id sw (sortableBoolArrays: sortableBoolArray[]) =
        fromBoolSequence id sw sortableBoolArrays
