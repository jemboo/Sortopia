namespace Sortopia.Sorting.Inputs

open System
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Sorting

[<Struct>]
type sortableUint8v256Tests =
    private { 
        id: Guid<sortableTestsId>
        channelCount: int<channelCount>
        simdSortBlocks: sortBlockUint8v256[]
    }

    static member create 
            (id: Guid<sortableTestsId>) 
            (sw: int<channelCount>)
            (blocks: sortBlockUint8v256[]) =
        if isNull blocks then nullArg "blocks"
        if int sw < 0 then invalidArg "sw" "Channel count must be non-negative."
        if blocks |> Array.exists (fun b -> b.Length <> int sw) then
            invalidArg "blocks" "Every block must match the dataset channel count."
        { id = id
          channelCount = sw
          simdSortBlocks = Array.copy blocks }

    static member Empty =
        let id = Guid.NewGuid() |> UMX.tag<sortableTestsId>
        sortableUint8v256Tests.create id 0<channelCount> [||]

    member this.Id with get() = this.id
    member this.SimdSortBlocks with get() = Array.copy this.simdSortBlocks
    member this.SortableDataFormat with get() = sortableDataFormat.Int8Vector256
    member this.SortableCount with get() = 
        this.simdSortBlocks 
        |> Array.sumBy (fun block -> block.SortableCount) 
        |> UMX.tag<sortableCount>
    member this.ChannelCount with get() = this.channelCount

module SortableUint8v256Tests =

    let fromIntArrays
        (id: Guid<sortableTestsId>)
        (sw: int<channelCount>)
        (allTests: sortableIntArray[]) : sortableUint8v256Tests =
        
        let totalTests = allTests.Length
        // Calculate how many SIMD blocks we need (32 lanes per block)
        let totalBlocksCount = (totalTests + 31) / 32
        let blocks = Array.zeroCreate<sortBlockUint8v256> totalBlocksCount

        // Parallel Pack: use all cores to build the blocks
        Parallel.For(0, totalBlocksCount, (fun i ->
            let startIdx = i * 32
            let length = min 32 (totalTests - startIdx)
            // Extract the slice for this block
            let slice = Array.sub allTests startIdx length
            blocks.[i] <- sortBlockUint8v256.createFromIntArrays sw slice
        )) |> ignore

        sortableUint8v256Tests.create id sw blocks
