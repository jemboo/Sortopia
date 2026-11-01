namespace Sortopia.Evaluation

open System
open System.Numerics
open Sortopia.Core
open System.Collections.Concurrent
open System.Collections.Generic
open System.Runtime.Intrinsics
open System.Threading
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

module CeBlockOpsBitv512 =

    /// Returns a Vector512 where each bit is '1' if that specific lane is unsorted.
    /// A lane is unsorted if it contains a 1 on a lower wire and a 0 on a higher wire.
    let getUnsortedMask (len: int) (vectors: Vector512<uint64>[]) =
        let mutable anyUnsorted = Vector512<uint64>.Zero
        for i = 0 to len - 2 do
            // Inversion: vector[i] has 1, vector[i+1] has 0
            let inversions = Vector512.AndNot(vectors.[i], vectors.[i+1])
            anyUnsorted <- anyUnsorted ||| inversions
        anyUnsorted

    /// Fast check if all 512 lanes are sorted.
    let isBlockSorted (len: int) (vectors: Vector512<uint64>[]) =
        getUnsortedMask len vectors = Vector512<uint64>.Zero

    /// Bitwise Compare-and-Swap (CAS) for 0-1 data.
    /// Directly mutates the workBuffer.
    let private applyCasCount count (workBuffer: Vector512<uint64>[]) (lIdx: int) (hIdx: int) =
        let vLow = workBuffer.[lIdx]
        let vHi = workBuffer.[hIdx]
        // Bits that need to move from Low to High
        let swaps = Vector512.AndNot(vLow, vHi)

        if swaps <> Vector512<uint64>.Zero then
            workBuffer.[lIdx] <- vLow ^^^ swaps
            workBuffer.[hIdx] <- vHi ^^^ swaps
            let mutable total = 0
            for i = 0 to 7 do
                let active = max 0 (min 64 (count - i * 64))
                let mask = if active = 64 then UInt64.MaxValue else (1UL <<< active) - 1UL
                total <- total + int (BitOperations.PopCount(swaps.GetElement(i) &&& mask))
            total
        else
            0

    let applyCas workBuffer low high = applyCasCount 512 workBuffer low high > 0

    let evalSimdSortBlocks
        (simdSortableBlocks: sortBlockBitv512 seq)
        (prefix: ceBlock)
        (ceBlocks: ceBlock array)
        : ceBlockEval [] =

        if isNull ceBlocks then nullArg "ceBlocks"

        let simdSortableBlocks = simdSortableBlocks |> Seq.toArray

        for block in simdSortableBlocks do

            for network in ceBlocks do CeBlock.validateWidth (UMX.tag block.Length) prefix network

        let numNetworks = ceBlocks.Length
        if numNetworks = 0 then [||] else

        let maxWidth = ceBlocks |> Array.maxBy (fun c -> %c.ChannelCount) |> (fun c -> %c.ChannelCount)
        let networkData = ceBlocks |> Array.map (fun ceb ->
            {| Lows = ceb.CeArray |> Array.map (fun c -> (int c.LowChannel))
               Highs = ceb.CeArray |> Array.map (fun c -> (int c.HighChannel))
               CeLen = ceb.CeArray.Length |})

        let globalUsage = Array.init numNetworks (fun i -> ceUseCounts.Create(ceBlocks.[i].CeLength))
        let globalUnsortedCount = Array.zeroCreate<int> numNetworks

        Parallel.ForEach(
            simdSortableBlocks,
            (fun () ->
                let usage = Array.init numNetworks (fun i -> Array.zeroCreate<int> networkData.[i].CeLen)
                let unsortedCount = Array.zeroCreate<int> numNetworks
                let workBuffer = Array.zeroCreate<Vector512<uint64>> maxWidth
                (usage, unsortedCount, workBuffer)),

            (fun currentBlock loopState ((localUsage: int [][]), (localUnsortedCount: int[]), workBuffer) ->
                let currentLen = currentBlock.Length

                for nIdx = 0 to numNetworks - 1 do
                    Array.blit currentBlock.Vectors 0 workBuffer 0 currentLen
                    let data = networkData.[nIdx]

                    for cIdx = 0 to data.CeLen - 1 do
                        let swaps = applyCasCount currentBlock.SortableCount workBuffer data.Lows.[cIdx] data.Highs.[cIdx]
                        localUsage.[nIdx].[cIdx] <- localUsage.[nIdx].[cIdx] + swaps

                    // Check which of the 512 bits are unsorted
                    let unsortedMask = getUnsortedMask currentLen workBuffer
                    if unsortedMask <> Vector512<uint64>.Zero then
                        // Count how many bits (lanes) are failed in this block
                        // Note: Only count bits up to SortableCount
                        let mutable failures = 0
                        let buf = Array.zeroCreate<uint64> 8
                        unsortedMask.CopyTo(Span<uint64>(buf))
                        for i = 0 to 511 do
                            if i < currentBlock.SortableCount then
                                let lane = i / 64
                                let bit = i % 64
                                if (buf.[lane] &&& (1uL <<< bit)) <> 0uL then
                                    failures <- failures + 1
                        localUnsortedCount.[nIdx] <- localUnsortedCount.[nIdx] + failures

                (localUsage, localUnsortedCount, workBuffer)),

            (fun ((localUsage: int[][] ), (localUnsortedCount: int[]), _) ->
                for nIdx = 0 to numNetworks - 1 do
                    Interlocked.Add(&globalUnsortedCount.[nIdx], localUnsortedCount.[nIdx]) |> ignore
                    let globalArr = globalUsage.[nIdx]
                    for i = 0 to localUsage.[nIdx].Length - 1 do
                        globalArr.IncrementAtomicBy (i |> UMX.tag) localUsage.[nIdx].[i]
            )
        ) |> ignore

        Array.init numNetworks (fun i ->
            ceBlockEval.create prefix ceBlocks.[i] globalUsage.[i] (globalUnsortedCount.[i] |> UMX.tag) None
        )


    let eval
            (test: sortableBitv512Tests)
            (prefix: ceBlock)
            (ceBlocks: ceBlock []) =
            evalSimdSortBlocks test.SimdSortBlocks prefix ceBlocks


    let evalAndCollectUniqueFailures
            (simdSortableBlocks: sortBlockBitv512 seq)
            (prefix: ceBlock)
            (ceBlocks: ceBlock array)
            : ceBlockEval [] =

        if isNull ceBlocks then nullArg "ceBlocks"

        let simdSortableBlocks = simdSortableBlocks |> Seq.toArray

        for block in simdSortableBlocks do

            for network in ceBlocks do CeBlock.validateWidth (UMX.tag block.Length) prefix network

        let numNetworks = ceBlocks.Length
        if numNetworks = 0 then [||] else

        let maxWidth = ceBlocks |> Array.maxBy (fun c -> %c.ChannelCount) |> (fun c -> %c.ChannelCount)
        let networkData = ceBlocks |> Array.map (fun ceb ->
            {| Lows = ceb.CeArray |> Array.map (fun c -> (int c.LowChannel))
               Highs = ceb.CeArray |> Array.map (fun c -> (int c.HighChannel))
               ChannelCount = ceb.ChannelCount
               CeLen = ceb.CeArray.Length |})

        let globalUsage = Array.init numNetworks (fun i -> ceUseCounts.Create(ceBlocks.[i].CeLength))

        // ConcurrentDictionary to store unique failed sequences
        let failureSets =
            Array.init numNetworks (fun _ ->
                let comparer = new ArrayContentComparer<int>() :> IEqualityComparer<int[]>
                ConcurrentDictionary<int[], byte>(comparer)
            )

        Parallel.ForEach(
            simdSortableBlocks,
            // 1. Initialize Thread-Local State - removed unsortedCount tracker
            (fun () ->
                let usage = Array.init numNetworks (fun i -> Array.zeroCreate<int> networkData.[i].CeLen)
                let workBuffer = Array.zeroCreate<Vector512<uint64>> maxWidth
                (usage, workBuffer)),

            // 2. The Hot Loop
            (fun currentBlock loopState ((localUsage: int[][]), workBuffer) ->
                let currentLen = currentBlock.Length

                for nIdx = 0 to numNetworks - 1 do
                    Array.blit currentBlock.Vectors 0 workBuffer 0 currentLen
                    let data = networkData.[nIdx]

                    // 1. Run the sorting network
                    for cIdx = 0 to data.CeLen - 1 do
                        let swaps = applyCasCount currentBlock.SortableCount workBuffer data.Lows.[cIdx] data.Highs.[cIdx]
                        localUsage.[nIdx].[cIdx] <- localUsage.[nIdx].[cIdx] + swaps

                    // 2. Identify failures at bit-level
                    let unsortedMask = getUnsortedMask currentLen workBuffer
                    if unsortedMask <> Vector512<uint64>.Zero then
                        let maskBuf = Array.zeroCreate<uint64> 8
                        unsortedMask.CopyTo(Span<uint64>(maskBuf))

                        // Copy current workBuffer to local arrays to facilitate extraction
                        let extractedWires = workBuffer |> Array.take currentLen |> Array.map (fun v ->
                            let b = Array.zeroCreate<uint64> 8
                            v.CopyTo(Span<uint64>(b))
                            b)

                        for i = 0 to currentBlock.SortableCount - 1 do
                            let lane = i / 64
                            let bitPos = i % 64
                            let bitMask = 1uL <<< bitPos

                            // If this specific bit index represents a sorting failure
                            if (maskBuf.[lane] &&& bitMask) <> 0uL then
                                // Reconstruct the 0-1 sequence for this specific lane
                                let failedSequence = Array.init currentLen (fun wIdx ->
                                    if (extractedWires.[wIdx].[lane] &&& bitMask) <> 0uL then 1 else 0
                                )
                                failureSets.[nIdx].TryAdd(failedSequence, 0uy) |> ignore

                (localUsage, workBuffer)),

            // 3. Thread State Merge - removed Interlocked.Add allocations
            (fun ((localUsage: int[][]), _) ->
                for nIdx = 0 to numNetworks - 1 do
                    let globalArr = globalUsage.[nIdx]
                    for i = 0 to localUsage.[nIdx].Length - 1 do
                        globalArr.IncrementAtomicBy (i |> UMX.tag) localUsage.[nIdx].[i]
            )
        ) |> ignore

        // Assemble results
        Array.init numNetworks (fun i ->
            let uniqueFailures = failureSets.[i].Keys |> Seq.toArray
            let uniqueUnsortedCount = uniqueFailures.Length

            let failTest =
                if uniqueFailures.Length > 0 then
                    let sw = ceBlocks.[i].ChannelCount
                    let sss = 2 |> UMX.tag<symbolSetSize> // Bit-packed is always 0-1
                    uniqueFailures
                    |> Array.map (fun arr -> sortableIntArray.create(arr, sw, sss))
                    |> sortableIntTests.create (Guid.NewGuid() |> UMX.tag) sw
                    |> sortableTests.Ints
                    |> Some
                else None

            ceBlockEval.create
                prefix
                ceBlocks.[i]
                globalUsage.[i]
                (uniqueUnsortedCount |> UMX.tag<sortableCount>)
                failTest
        )

    let evalAndCollectNewSortableTests
            (test: sortableBitv512Tests)
            (prefix: ceBlock)
            (ceBlocks: ceBlock []) =
        evalAndCollectUniqueFailures test.SimdSortBlocks prefix ceBlocks
