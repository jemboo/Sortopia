namespace Sortopia.Sorting.Inputs

open System
open System.Runtime.Intrinsics
open FSharp.UMX
open Sortopia.Sorting

[<Struct>]
type sortBlockUint8v256 = 
    private { 
        vectors: Vector256<uint8>[] 
        sortableCount: int 
    }
    
    /// Creates a SIMD block from a slice of 1-32 sortableIntArrays.
    static member createFromIntArrays (sw: int<channelCount>) (arrays: sortableIntArray[]) =
        if isNull arrays then nullArg "arrays"
        if int sw < 0 then invalidArg "sw" "Channel count must be non-negative."
        if arrays |> Array.exists (fun a -> a.ChannelCount <> sw) then
            invalidArg "arrays" "Every input must match the block width."
        if arrays |> Array.exists (fun a -> a.Values |> Array.exists (fun v -> v < 0 || v > 255)) then
            invalidArg "arrays" "Byte SIMD values must be between 0 and 255."
        let width = %sw
        let inputCount = arrays.Length
        if inputCount > 32 then 
            invalidArg "arrays" "A single SIMD block can only hold up to 32 lanes."
        
        // Vertical transposition: arrays[lane][vIdx] -> vectors[vIdx].lane
        let vecs = Array.init width (fun vIdx ->
            let mutable v = Vector256<uint8>.Zero
            for lane = 0 to 31 do
                let value = 
                    if lane < inputCount then 
                        byte (arrays.[lane].Item(vIdx))
                    else 
                        0uy // Inactive lanes are zero padded.
                v <- v.WithElement(lane, value)
            v
        )
        { vectors = vecs; sortableCount = inputCount }

    static member createFromVectors (vecs: Vector256<uint8>[]) (count: int) =
        if isNull vecs then nullArg "vecs"
        if count < 0 || count > 32 then invalidArg "count" "Invalid SIMD lane count."
        { vectors = Array.copy vecs; sortableCount = count }

    member this.Vectors = Array.copy this.vectors
    member this.Length = this.vectors.Length
    member this.SortableCount = this.sortableCount

module SortBlockUint8v256 = 

    let toSortableIntArrays (s256: sortBlockUint8v256) : sortableIntArray[] =
        let result = Array.zeroCreate<sortableIntArray> s256.SortableCount
        let vectors = s256.Vectors
        let rawData : byte[][] =
            Array.init s256.Length (fun index ->
                let values = Array.zeroCreate<byte> 32
                vectors.[index].CopyTo(System.Span<byte>(values))
                values)

        for lane = 0 to s256.SortableCount - 1 do
            let values = Array.init s256.Length (fun index -> int rawData.[index].[lane])
            result.[lane] <-
                sortableIntArray.create(
                    values,
                    s256.Length |> UMX.tag<channelCount>,
                    256 |> UMX.tag<symbolSetSize>)

        result
