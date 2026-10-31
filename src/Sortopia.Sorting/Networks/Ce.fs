namespace Sortopia.Sorting.Networks

open System
open System.Collections
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting

[<Measure>] type sequenceHash
[<Measure>] type reflectiveCount

[<Struct>]
type ce = private { lowChannel: int<channelIndex>; highChannel: int<channelIndex> } with

    static member create (lhs: int) (rhs: int) : ce =
        if lhs < 0 || rhs < 0 then
            failwith "Indices must be non-negative"
        else if lhs < rhs then
            { lowChannel = ChannelIndex.create lhs; highChannel = ChannelIndex.create rhs }
        else
            { lowChannel = ChannelIndex.create rhs; highChannel = ChannelIndex.create lhs }

    /// The lower channel endpoint.
    member this.LowChannel with get () = this.lowChannel

    /// The higher channel endpoint.
    member this.HighChannel with get () = this.highChannel

    member this.ChannelSpan : int<channelCount> = UMX.tag (int this.HighChannel - int this.LowChannel)

module Ce =

    let toString (ce: ce) : string =
        sprintf "(%d, %d)" (int ce.LowChannel) (int ce.HighChannel)

    let fromPermSi (psi: permSi) : ce [] =
        psi
        |> PermSi.getTwoOrbits
        |> Array.map (fun tbit -> ce.create tbit.First tbit.Second)

    // combine the upper and lower arrays, but increase the lowChannel and highChannel indexes of the ce's in cesLower
    // by channelOffset. Verify that ce.LowChannel, and ce.HighChannel in cesUpper are between 0 and (channelCountUpper - 1).
    // Verify that ce.LowChannel, and ce.HighChannel in cesLower are between 0 and (channelCountLower - 1).
    let stack (cesUpper: ce[]) (cesLower: ce[]) 
              (channelOffset: int<channelCount>)  : ce[] =

        // Shift cesLower indices by channelOffset
        let shiftedLower =
            cesLower |> Array.map (fun _ce -> ce.create ((int _ce.LowChannel) + %channelOffset) ((int _ce.HighChannel) + %channelOffset))

        // Combine the arrays
        Array.append cesUpper shiftedLower

    // use stack to combine arrays of ce arrays
    let stack2d (cesUpper: ce[][]) (cesLower: ce[][]) 
                (channelOffset: int<channelCount>) : ce[][] =

        if cesUpper.Length <> cesLower.Length then
            failwith "cesUpper and cesLower must have the same length"
        
        Array.map2 (fun upper lower -> 
            stack upper lower channelOffset
        ) cesUpper cesLower

    let inline private validateDivisibility (width: int<channelCount>) (divisor: int) (funcName: string) =
            let rawWidth = UMX.untag width
            if divisor < 1 then invalidArg "divisor" "Merge dimension must be positive."
            if rawWidth < 1 then invalidArg "width" "Channel count must be positive."
            if rawWidth % divisor <> 0 then
                invalidArg (nameof width) $"{funcName} requires mergedChannelCount ({rawWidth}) to be divisible by {divisor}."

    let mergeN (dim: int<mergeDimension>) (mergedChannelCount: int<channelCount>) (cesA: ce[][]) : ce[][] =
            validateDivisibility mergedChannelCount %dim $"merge{dim}"
            let subWidth = mergedChannelCount / %dim
            let mutable current = cesA
            for i in 1 .. (%dim - 1) do
                current <- stack2d current cesA (subWidth * i)
            current

    /// Number of distinct non-self channel pairs; also the triangular index boundary.
    let maxIndexForChannelCount (width: int) : int =
        if width < 0 then invalidArg "width" "Width must be non-negative."
        let count = int64 width * (int64 width - 1L) / 2L
        if count > int64 Int32.MaxValue then invalidArg "width" "Comparator index range exceeds Int32 capacity."
        int count

    /// Triangular encoding, including self-comparators. Uses Int64 arithmetic to prevent overflow.
    let toIndex (comparator: ce) : int =
        let hi = int64 comparator.HighChannel
        let code = hi * (hi + 1L) / 2L + int64 comparator.LowChannel
        if code > int64 Int32.MaxValue then invalidArg "comparator" "Comparator index exceeds Int32 capacity."
        int code

    let fromIndex (index: int) : ce =
        if index < 0 then failwith "Index must be non-negative"
        let code = int64 index
        let mutable hi = int64 ((sqrt (1.0 + 8.0 * float index) - 1.0) / 2.0)
        let triangle n = n * (n + 1L) / 2L
        while triangle hi > code do hi <- hi - 1L
        while triangle (hi + 1L) <= code do hi <- hi + 1L
        ce.create (int (code - triangle hi)) (int hi)

    let permute (perm: permutation) (cer:ce) =
        ce.create (perm.permute (int cer.LowChannel)) (perm.permute (int cer.HighChannel))

    let reflect (channelCount: int<channelCount>) (cer: ce) : ce =
            ce.create 
                ((int cer.HighChannel) |> Sortopia.Core.Combinatorics.reflect %channelCount)
                ((int cer.LowChannel) |>  Sortopia.Core.Combinatorics.reflect %channelCount)

    let countReflectiveOrReflected (channelCount: int<channelCount>) (ces: ce[]) : int<reflectiveCount> =
        if ces.Length = 0 then
            0 |> UMX.tag
        else
            let w = %channelCount
            let maxIdx = maxIndexForChannelCount (w + 1)
        
            // Create bit-presence mask (extremely fast bitwise operations)
            let presence = BitArray(maxIdx + 1, false)
            for i in 0 .. ces.Length - 1 do
                presence.Set(toIndex ces.[i], true)

            let mutable count = 0
            for i in 0 .. ces.Length - 1 do
                let c = ces.[i]
                let reflected = reflect channelCount c
                let reflectedIdx = toIndex reflected
            
                if presence.Get(reflectedIdx) then
                    count <- count + 1

            count |> UMX.tag

    let generateCeCode (excludeSelfCe:bool)
                       (width:int) 
                       (indexPicker: int -> int) : int =
        if width < 1 then
            failwith "Width must be at least 1"
        if excludeSelfCe && width < 2 then invalidArg "width" "At least two channels are needed for a non-self comparator."
        if width > 65535 then invalidArg "width" "Comparator index range exceeds Int32 capacity."
        let ww = if excludeSelfCe then width else width + 1
        let indexMax = maxIndexForChannelCount ww
        let dex = indexPicker indexMax
        if excludeSelfCe then
            let ceTemp = fromIndex dex
            ce.create ((int ceTemp.LowChannel)) ((int ceTemp.HighChannel) + 1) |> toIndex
        else
            dex

    /// returns random Ce's to make Sorters, including Ce's where lowChannel=highChannel
    /// <param name="width">The Sorter width</param>
    let generateCeCodes (indexPicker: int -> int) 
                        (width:int) : int seq =
        if width < 1 then
            failwith "Width must be at least 1"
        let indexMax = maxIndexForChannelCount (width + 1)
        seq {
            while true do
                indexPicker indexMax
        }

    /// returns random Ce's to make Sorters, excluding Ce's where lowChannel=highChannel
    /// <param name="width">The Sorter width</param>
    let generateCeCodesExcludeSelf 
                    (indexPicker: int -> int) 
                    (width:int) : int seq =
        if width < 2 then
            failwith "Width must be at least 2"
        let indexMax = maxIndexForChannelCount width
        seq {
            while true do
                let ceTemp = indexPicker indexMax |> fromIndex
                ce.create ((int ceTemp.LowChannel)) ((int ceTemp.HighChannel) + 1) |> toIndex
        }

    let fromTwoOrbit (toOb:twoOrbit) :ce = 
            ce.create (toOb.First) (toOb.Second)
