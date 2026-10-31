namespace Sortopia.Sorting.Inputs

open System
open System.Runtime.Intrinsics
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Sorting

[<Struct>]
type sortableUint8v512Tests =
    private { 
        id: Guid<sortableTestsId>
        channelCount: int<channelCount>
        simdSortBlocks: sortBlockUint8v512[]
    }

    static member create 
            (id: Guid<sortableTestsId>) 
            (sw: int<channelCount>)
            (blocks: sortBlockUint8v512[]) =
        if isNull blocks then nullArg "blocks"
        if int sw < 0 then invalidArg "sw" "Channel count must be non-negative."
        if blocks |> Array.exists (fun b -> b.Length <> int sw) then
            invalidArg "blocks" "Every block must match the dataset channel count."
        { id = id
          channelCount = sw
          simdSortBlocks = Array.copy blocks }

    static member Empty =
        let id = Guid.NewGuid() |> UMX.tag<sortableTestsId>
        sortableUint8v512Tests.create id 0<channelCount> [||]

    member this.Id with get() = this.id
    member this.SimdSortBlocks with get() = Array.copy this.simdSortBlocks
    
    // Updated to reflect the 512-bit format
    member this.SortableDataFormat with get() = sortableDataFormat.Int8Vector512
    
    member this.SortableCount with get() = 
        this.simdSortBlocks 
        |> Array.sumBy (fun block -> block.SortableCount) 
        |> UMX.tag<sortableCount>
        
    member this.ChannelCount with get() = this.channelCount

module SortableUint8v512Tests =

    let fromIntArrays
        (id: Guid<sortableTestsId>)
        (sw: int<channelCount>)
        (allTests: sortableIntArray[]) : sortableUint8v512Tests =

        let totalTests = allTests.Length
        
        // --- 64 Lanes per block for Vector512 ---
        let totalBlocksCount = (totalTests + 63) / 64
        let blocks = Array.zeroCreate<sortBlockUint8v512> totalBlocksCount

        // Parallel Pack: use all cores to build the 64-lane blocks
        Parallel.For(0, totalBlocksCount, (fun i ->
            let startIdx = i * 64
            let length = min 64 (totalTests - startIdx)
            
            // Extract the slice for this block
            let slice = Array.sub allTests startIdx length
            blocks.[i] <- sortBlockUint8v512.createFromIntArrays sw slice
        )) |> ignore

        sortableUint8v512Tests.create id sw blocks
