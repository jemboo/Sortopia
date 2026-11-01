namespace Sortopia.Evaluation

open System
open System.Buffers
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs


type PackedOffsetComparer(data: int[], sw: int) =
    interface IEqualityComparer<int> with
        member _.Equals(offsetA: int, offsetB: int) =
            let spanA = data.AsSpan(offsetA, sw)
            let spanB = data.AsSpan(offsetB, sw)
            spanA.SequenceEqual(spanB)

        member _.GetHashCode(offset: int) =
            let span = data.AsSpan(offset, sw)
            // Reinterpret the int span as bytes (zero-copy)
            let byteSpan = MemoryMarshal.Cast<int, byte>(span)

            let mutable hash = HashCode()
            // Force the conversion by defining a ReadOnlySpan binding
            let roByteSpan : ReadOnlySpan<byte> = Span<byte>.op_Implicit(byteSpan)
            hash.AddBytes(roByteSpan)
            hash.ToHashCode()


module CeBlockOpsPacked =

    // non-inline version, not used in hot path
    let deduplicatePacked (packedTs: packedSortableIntTests) (resultsBuffer: int[]) =
        let sw = %packedTs.ChannelCount
        let totalTests = packedTs.SortableCount

        // 1. HashSet stores ONLY the starting index (offset) of unique unsorted tests
        let comparer = PackedOffsetComparer(resultsBuffer, sw)
        let uniqueOffsets = HashSet<int>(comparer)

        for t = 0 to %totalTests - 1 do
            let offset = t * sw

            // 2. Check sortedness in-place first (cheapest check)
            let mutable isSorted = true
            let mutable j = 0
            while j < sw - 1 && isSorted do
                if resultsBuffer.[offset + j] > resultsBuffer.[offset + j + 1] then
                    isSorted <- false
                else j <- j + 1

            // 3. If unsorted, try to add the offset to our "unique" set
            if not isSorted then
                uniqueOffsets.Add(offset) |> ignore

        // 4. Re-pack only the unique failures into a new contiguous array
        let newCount = uniqueOffsets.Count
        let newPackedData = Array.zeroCreate (newCount * sw)
        let mutable targetOffset = 0

        for sourceOffset in uniqueOffsets do
            Array.blit resultsBuffer sourceOffset newPackedData targetOffset sw
            targetOffset <- targetOffset + sw

        packedSortableIntTests.createFromPackedValues
                        packedTs.ChannelCount
                        newPackedData


    let eval (tests: packedSortableIntTests)
             (prefix: ceBlock) (ceBlk: ceBlock) : ceBlockEval =
        CeBlock.validateWidth tests.ChannelCount prefix ceBlk
        let sw = %tests.ChannelCount
        let totalTests = tests.SortableCount
        let ces = ceBlk.CeArray

        // Pre-deconstruct CEs for the hot loop
        let lows = Array.init %ceBlk.CeLength (fun i -> (int ces.[i].LowChannel))
        let highs = Array.init %ceBlk.CeLength (fun i -> (int ces.[i].HighChannel))
        let ceUseCounts = ceUseCounts.Create ceBlk.CeLength
        let mutable unsortedCount = 0

        // Work on a mutable copy
        let resultsBuffer = tests.PackedValues
        let mutable dataRef = &MemoryMarshal.GetReference(resultsBuffer.AsSpan())

        // PHASE 1: THE SORTING (Hot Loop)
        for t = 0 to %totalTests - 1 do
            let offset = t * sw
            for i = 0 to %ceBlk.CeLength - 1 do
                let lPtr = &Unsafe.Add(&dataRef, offset + lows.[i])
                let hPtr = &Unsafe.Add(&dataRef, offset + highs.[i])
                let a = lPtr
                let b = hPtr
                if a > b then
                    lPtr <- b
                    hPtr <- a
                    ceUseCounts.Increment (i |> UMX.tag<ceIndex>)

        for t = 0 to %totalTests - 1 do
            let offset = t * sw
            let mutable isSorted = true
            let mutable j = 0
            while j < sw - 1 && isSorted do
                if resultsBuffer.[offset + j] > resultsBuffer.[offset + j + 1] then
                    isSorted <- false
                else j <- j + 1

            if not isSorted then
                unsortedCount <- unsortedCount + 1

        ceBlockEval.create prefix ceBlk ceUseCounts (unsortedCount |> UMX.tag<sortableCount>) None


    let evalAndCollectNewSortableTests (tests: packedSortableIntTests)
                                       (prefix: ceBlock) (ceBlk: ceBlock) : ceBlockEval =
        CeBlock.validateWidth tests.ChannelCount prefix ceBlk
        let sw = %tests.ChannelCount
        let totalTests = tests.SortableCount
        let ces = ceBlk.CeArray

        // Pre-deconstruct CEs for the hot loop
        let lows = Array.init %ceBlk.CeLength (fun i -> (int ces.[i].LowChannel))
        let highs = Array.init %ceBlk.CeLength (fun i -> (int ces.[i].HighChannel))
        let ceUseCounts = ceUseCounts.Create ceBlk.CeLength

        // Work on a mutable copy
        let resultsBuffer = tests.PackedValues
        let mutable dataRef = &MemoryMarshal.GetReference(resultsBuffer.AsSpan())

        // PHASE 1: THE SORTING (Hot Loop)
        for t = 0 to %totalTests - 1 do
            let offset = t * sw
            for i = 0 to %ceBlk.CeLength - 1 do
                let lPtr = &Unsafe.Add(&dataRef, offset + lows.[i])
                let hPtr = &Unsafe.Add(&dataRef, offset + highs.[i])
                let a = lPtr
                let b = hPtr
                if a > b then
                    lPtr <- b
                    hPtr <- a
                    ceUseCounts.Increment (i |> UMX.tag<ceIndex>)

        // PHASE 2: IN-PLACE DEDUPLICATION
        let comparer = PackedOffsetComparer(resultsBuffer, sw)
        let uniqueOffsets = HashSet<int>(comparer)

        for t = 0 to %totalTests - 1 do
            let offset = t * sw
            let mutable isSorted = true
            let mutable j = 0
            while j < sw - 1 && isSorted do
                if resultsBuffer.[offset + j] > resultsBuffer.[offset + j + 1] then
                    isSorted <- false
                else j <- j + 1

            if not isSorted then
                uniqueOffsets.Add(offset) |> ignore

        // PHASE 3: MATERIALIZATION
        let newCount = uniqueOffsets.Count
        let newPackedData = Array.zeroCreate (newCount * sw)
        let mutable targetOffset = 0
        for sourceOffset in uniqueOffsets do
            Array.blit resultsBuffer sourceOffset newPackedData targetOffset sw
            targetOffset <- targetOffset + sw

        let newPacked = packedSortableIntTests.createFromPackedValues
                                                    tests.ChannelCount
                                                    newPackedData

        ceBlockEval.create
                    prefix
                    ceBlk
                    ceUseCounts
                    (newCount |> UMX.tag<sortableCount>)
                    (Some (sortableTests.PackedInts newPacked))
