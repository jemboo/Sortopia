namespace Sortopia.Sorting.Inputs

open System.Runtime.Intrinsics
open FSharp.UMX
open Sortopia.Sorting

[<Struct>]
type sortBlockBitv512 = 
    private { 
        vectors: Vector512<uint64>[] 
        sortableCount: int 
    }
    
    static member createFromVectors (vecs: Vector512<uint64>[]) (count: int) =
        if isNull vecs then nullArg "vecs"
        if count < 0 || count > 512 then invalidArg "count" "Invalid SIMD lane count."
        { vectors = Array.copy vecs; sortableCount = count }

    member this.Vectors = Array.copy this.vectors
    member this.Length = this.vectors.Length
    member this.SortableCount = this.sortableCount

module SortBlockBitv512 =
// Lowers the sortable count to newCount by truncating excess lanes.
    let truncate (sb: sortBlockBitv512) (newCount: int) : sortBlockBitv512 =
        if newCount >= sb.SortableCount then
            sb // No change needed if count is higher or equal
        elif newCount <= 0 then
            // Return empty block
            let emptyVecs = sb.Vectors |> Array.map (fun _ -> Vector512<uint64>.Zero)
            sortBlockBitv512.createFromVectors emptyVecs 0
        else
            let laneIndex = newCount / 64
            let bitOffset = newCount % 64
            
            // Create a mask for the partially filled lane
            // Bits below 'bitOffset' are kept (1), bits at or above are cleared (0)
            let laneMask = (1uL <<< bitOffset) - 1uL

            let truncatedVectors = sb.Vectors |> Array.map (fun v ->
                // Copy the 8 uint64 lanes to a buffer to mutate
                let mutable buffer = Array.zeroCreate<uint64> 8
                v.CopyTo(System.Span<uint64>(buffer))
                
                // 1. Mask the specific lane where the truncation occurs
                if laneIndex < 8 then
                    buffer.[laneIndex] <- buffer.[laneIndex] &&& laneMask
                
                // 2. Zero out any lanes that follow
                for i = laneIndex + 1 to 7 do
                    buffer.[i] <- 0uL

                Vector512.Create(
                    buffer.[0], buffer.[1], buffer.[2], buffer.[3],
                    buffer.[4], buffer.[5], buffer.[6], buffer.[7]
                )
            )

            sortBlockBitv512.createFromVectors truncatedVectors newCount

    let toSortableIntArrays (s512: sortBlockBitv512) : sortableIntArray[] =
        let sw = s512.Length |> UMX.tag<channelCount>
        let sss = 2 |> UMX.tag<symbolSetSize>
        let result = Array.zeroCreate<sortableIntArray> s512.sortableCount

        // Extract raw bit-data. Copying 'v' to 'localV' prevents FS0406 
        // if sortBlockBitv512 is a struct.
        let rawData = s512.Vectors |> Array.map (fun v ->
            let buf = Array.zeroCreate<uint64> 8
            let localV = v 
            localV.CopyTo(System.Span<uint64>(buf))
            buf)

        for testIdx = 0 to s512.sortableCount - 1 do
            let lane = testIdx / 64
            let bitShift = testIdx % 64
            let mask = 1uL <<< bitShift
    
            let values = Array.zeroCreate<int> %sw
            for wireIdx = 0 to %sw - 1 do
                if (rawData.[wireIdx].[lane] &&& mask) <> 0uL then
                    values.[wireIdx] <- 1
                else
                    values.[wireIdx] <- 0
    
            result.[testIdx] <- sortableIntArray.create(values, sw, sss)
        result

    let toSortableBoolArrays (s512: sortBlockBitv512) : sortableBoolArray[] =
        let sw = s512.Length |> UMX.tag<channelCount>
        let result = Array.zeroCreate<sortableBoolArray> s512.SortableCount
        
        // Pre-extract the 8 uint64 lanes for each vector to avoid Repeated GetElement calls
        // We use a local variable copy to avoid potential FS0406 struct-field access issues
        let rawData = s512.Vectors |> Array.map (fun v ->
            let buf = Array.zeroCreate<uint64> 8
            let localV = v
            localV.CopyTo(System.Span<uint64>(buf))
            buf)

        for testIdx = 0 to s512.SortableCount - 1 do
            let lane = testIdx / 64
            let bitPos = testIdx % 64
            let mask = 1uL <<< bitPos
            
            let values = Array.zeroCreate<bool> %sw
            for wireIdx = 0 to %sw - 1 do
                // If the bit is set at this wire for this test case, set to true
                if (rawData.[wireIdx].[lane] &&& mask) <> 0uL then
                    values.[wireIdx] <- true
                else
                    values.[wireIdx] <- false
            
            result.[testIdx] <- sortableBoolArray.create(values, sw)
            
        result
