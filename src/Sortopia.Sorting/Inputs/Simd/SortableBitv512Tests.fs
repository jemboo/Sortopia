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

    let fromBoolArrays
        (id: Guid<sortableTestsId>)
        (sw: int<channelCount>)
        (sortableBoolArrays: sortableBoolArray[]) : sortableBitv512Tests =
        
        if isNull sortableBoolArrays then nullArg "sortableBoolArrays"
        if int sw < 0 then invalidArg "sw" "Channel count must be non-negative."
        if sortableBoolArrays |> Array.exists (fun a -> a.ChannelCount <> sw) then
            invalidArg "sortableBoolArrays" "Every input must match the dataset channel count."
        let width = %sw
        // Chunk the input arrays into groups of 512
        let blocks = 
            sortableBoolArrays 
            |> Array.chunkBySize 512 
            |> Array.map (fun chunk ->
                let inputCount = chunk.Length
                
                // Map wires across the bit-lanes
                let vecs = Array.init width (fun wireIdx ->
                    let buffer = Array.zeroCreate<uint64> 8
                    
                    for testIdx = 0 to inputCount - 1 do
                        // Direct bool check is faster than int conversion
                        if chunk.[testIdx].Item(wireIdx) then
                            let lane = testIdx / 64
                            let bit = testIdx % 64
                            buffer.[lane] <- buffer.[lane] ||| (1uL <<< bit)
                    
                    Vector512.Create(
                        buffer.[0], buffer.[1], buffer.[2], buffer.[3],
                        buffer.[4], buffer.[5], buffer.[6], buffer.[7]
                    )
                )
                sortBlockBitv512.createFromVectors vecs inputCount
            )

        sortableBitv512Tests.create id sw blocks
