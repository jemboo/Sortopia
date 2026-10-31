namespace Sortopia.Sorting.Inputs

open System.Runtime.Intrinsics
open FSharp.UMX
open Sortopia.Sorting

[<Struct>]
type sortBlockUint8v512 = 
    private { 
        vectors: Vector512<uint8>[] 
        sortableCount: int 
    }

    static member createFromIntArrays (sw: int<channelCount>) (arrays: sortableIntArray[]) =
        if isNull arrays then nullArg "arrays"
        if int sw < 0 then invalidArg "sw" "Channel count must be non-negative."
        if arrays |> Array.exists (fun a -> a.ChannelCount <> sw) then
            invalidArg "arrays" "Every input must match the block width."
        if arrays |> Array.exists (fun a -> a.Values |> Array.exists (fun v -> v < 0 || v > 255)) then
            invalidArg "arrays" "Byte SIMD values must be between 0 and 255."
        let width = %sw
        let inputCount = arrays.Length
        if inputCount > 64 then 
            invalidArg "arrays" "A single Vector512 block can only hold up to 64 lanes."
        
        let vecs = Array.init width (fun vIdx ->
            let mutable v = Vector512<uint8>.Zero
            for lane = 0 to 63 do
                let value = 
                    if lane < inputCount then byte (arrays.[lane].Item(vIdx))
                    else 0uy // Inactive lanes are zero padded.
                v <- v.WithElement(lane, value)
            v
        )
        { vectors = vecs; sortableCount = inputCount }

    static member createFromVectors (vecs: Vector512<uint8>[]) (count: int) =
        if isNull vecs then nullArg "vecs"
        if count < 0 || count > 64 then invalidArg "count" "Invalid SIMD lane count."
        { vectors = Array.copy vecs; sortableCount = count }

    member this.Vectors = Array.copy this.vectors
    member this.Length = this.vectors.Length
    member this.SortableCount = this.sortableCount

module SortBlockUint8v512 = 

    let toSortableIntArrays (s512: sortBlockUint8v512) : sortableIntArray[] =
        let result = Array.zeroCreate<sortableIntArray> s512.sortableCount
        
        // Explicitly create spans to avoid FS0406
        let vectors = s512.Vectors
        let rawData : byte [][] = Array.init s512.Length (fun i -> 
            let buf = Array.zeroCreate<byte> 64
            // Convert the array to a Span explicitly
            let span = System.Span<byte>(buf)
            vectors.[i].CopyTo(span)
            buf)

        for lane = 0 to s512.sortableCount - 1 do
            let values = Array.zeroCreate<int> s512.Length
            for vIdx = 0 to s512.Length - 1 do
                values.[vIdx] <- int rawData.[vIdx].[lane]
            
            result.[lane] <- sortableIntArray.create(
                                values, 
                                s512.Length |> UMX.tag<channelCount>,
                                256 |> UMX.tag<symbolSetSize>)
        result
