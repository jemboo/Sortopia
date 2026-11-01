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


module CeBlockOpsUint8v256 =

    let private activeMask count =
        if count = 32 then UInt32.MaxValue else (1u <<< count) - 1u


    /// Check if all 32 lanes in the block are sorted.
    let IsBlockSorted (len: int) (vectors: Vector256<uint8>[]) =
        let mutable sorted = true
        let mutable i = 0
        while i < len - 1 && sorted do
            // In a sorted lane, v[i] <= v[i+1].
            // Therefore, Min(v[i], v[i+1]) must be equal to v[i].
            let vLow = vectors.[i]
            let vHi = vectors.[i+1]
            if Vector256.Min(vLow, vHi) <> vLow then
                sorted <- false
            else
                i <- i + 1
        sorted

/// Mask-optimized: Check if all 32 lanes in the block are sorted.
    let IsBlockSortedMask (len: int) (vectors: Vector256<uint8>[]) =
        let mutable sorted = true
        let mutable i = 0
        while i < len - 1 do
            let vLow = vectors.[i]
            let vHi = vectors.[i+1]

            // Mask: 0xFF where ordered (Low <= Hi), 0x00 otherwise
            let mask = Vector256.Equals(Vector256.Min(vLow, vHi), vLow)

            // Extract the MSB of each of the 32 bytes into a 32-bit integer
            let bitmask = Vector256.ExtractMostSignificantBits(mask)

            // If all 32 lanes are sorted, bitmask will be 0xFFFFFFFFu
            if bitmask <> 0xffffffffu then
                sorted <- false
                i <- len // Break
            else
                i <- i + 1
        sorted


    /// One bit per lane, set when any adjacent channel pair is out of order.
    let unsortedLaneMask (len: int) (vectors: Vector256<uint8>[]) =
        let mutable failures = 0u
        for i = 0 to len - 2 do
            let inversions = Vector256.GreaterThan(vectors.[i], vectors.[i + 1])
            failures <- failures ||| Vector256.ExtractMostSignificantBits(inversions)
        failures


    let evalSimdSortBlocks
            (simdSortableBlocks: sortBlockUint8v256 seq)
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
            let globalUnsorted = Array.zeroCreate<int> numNetworks

            Parallel.ForEach(
                simdSortableBlocks,
                // 1. Initialize Thread-Local State (TLS)
                (fun () ->
                    let usage = Array.init numNetworks (fun i -> Array.zeroCreate<int> networkData.[i].CeLen)
                    let unsorted = Array.zeroCreate<int> numNetworks
                    let buffer = Array.zeroCreate<Vector256<uint8>> maxWidth
                    (usage, unsorted, buffer)),

                // 2. The Hot Loop
                // Explicit type annotations for the state tuple to satisfy the F# compiler
                (fun currentBlock loopState (localUsage: int [][], localUnsorted: int [], workBuffer: Vector256<uint8> []) ->
                    let currentLen = currentBlock.Length

                    for nIdx = 0 to numNetworks - 1 do
                        // Copy source vectors to local work buffer
                        Array.blit currentBlock.Vectors 0 workBuffer 0 currentLen
                        let data = networkData.[nIdx]
                        let ceCount = data.CeLen
                        let currentNetUsage = localUsage.[nIdx]

                        // --- Hot Loop: Compare and Swap ---
                        for cIdx = 0 to ceCount - 1 do
                            let lIdx = data.Lows.[cIdx]
                            let hIdx = data.Highs.[cIdx]
                            let vLow = workBuffer.[lIdx]
                            let vHi = workBuffer.[hIdx]

                            let vMin = Vector256.Min(vLow, vHi)

                            // If vLow is not the minimum, at least one lane swapped
                            if vLow <> vMin then
                                currentNetUsage.[cIdx] <- currentNetUsage.[cIdx] + int (BitOperations.PopCount((Vector256.ExtractMostSignificantBits(Vector256.GreaterThan(vLow, vHi)) &&& activeMask currentBlock.SortableCount)))
                                workBuffer.[lIdx] <- vMin
                                workBuffer.[hIdx] <- Vector256.Max(vLow, vHi)

                        // Verification
                        let failures = unsortedLaneMask currentLen workBuffer
                        localUnsorted.[nIdx] <- localUnsorted.[nIdx] + int (BitOperations.PopCount(failures &&& activeMask currentBlock.SortableCount))

                    // RETURN the state so it is available for the next iteration/merge
                    (localUsage, localUnsorted, workBuffer)),

                // 3. Final Merge (TLS to Global)
                (fun ((localUsage: int [][]), (localUnsorted: int []), _) ->
                    for nIdx = 0 to numNetworks - 1 do
                        // Merge unsorted counts
                        if localUnsorted.[nIdx] > 0 then
                            Interlocked.Add(&globalUnsorted.[nIdx], localUnsorted.[nIdx]) |> ignore

                        // Merge usage counts
                        let globalArr = globalUsage.[nIdx]
                        let threadLocalArr = localUsage.[nIdx]
                        for i = 0 to threadLocalArr.Length - 1 do
                            if threadLocalArr.[i] > 0 then
                                globalArr.IncrementAtomicBy (i |> UMX.tag) threadLocalArr.[i]
                )
            ) |> ignore

            Array.init numNetworks (fun i ->
                ceBlockEval.create prefix ceBlocks.[i] globalUsage.[i] (globalUnsorted.[i] |> UMX.tag) None
            )


    let eval
            (test: sortableUint8v256Tests)
            (prefix: ceBlock)
            (ceBlocks: ceBlock []) =
            //let chunkedStream = test.SimdSortBlocks |> Seq.chunkBySize chunkSize
            //evalSimdSortBlockChunks chunkedStream ceBlocks
            evalSimdSortBlocks test.SimdSortBlocks prefix ceBlocks


    /// Computes a hash for a single lane across all vectors in the block.
    /// This allows us to check for duplicates without extracting the full array.
    let inline hashLane (block: Vector256<uint8>[]) (lane: int) =
        let mutable h = 17
        for i = 0 to block.Length - 1 do
            h <- h * 31 + int (block.[i].GetElement(lane))
        h


    let evalAndCollectUniqueFailures
            (simdSortableBlocks: sortBlockUint8v256 seq)
            (prefix: ceBlock)
            (ceBlocks: ceBlock array)
            : ceBlockEval [] =

        if isNull ceBlocks then nullArg "ceBlocks"

        let simdSortableBlocks = simdSortableBlocks |> Seq.toArray

        for block in simdSortableBlocks do

            for network in ceBlocks do CeBlock.validateWidth (UMX.tag block.Length) prefix network

        let numNetworks = ceBlocks.Length
        if numNetworks = 0 then [||] else

        // Pre-calculate network metadata for fast access in the hot loop
        let maxWidth = ceBlocks |> Array.maxBy (fun c -> %c.ChannelCount) |> (fun c -> %c.ChannelCount)
        let networkData = ceBlocks |> Array.map (fun ceb ->
            {| Lows = ceb.CeArray |> Array.map (fun c -> (int c.LowChannel))
               Highs = ceb.CeArray |> Array.map (fun c -> (int c.HighChannel))
               ChannelCount = ceb.ChannelCount
               CeLen = ceb.CeArray.Length |})

        // Final global storage
        let globalUsage = Array.init numNetworks (fun i -> ceUseCounts.Create(ceBlocks.[i].CeLength))

        // ConcurrentDictionary with explicit type annotation and custom content comparer
        let failureSets: ConcurrentDictionary<int[], byte> [] =
            Array.init numNetworks (fun _ ->
                ConcurrentDictionary<int[], byte>(ArrayContentComparer<int>()))

        Parallel.ForEach(
            simdSortableBlocks,
            // 1. Initialize Thread-Local State (TLS) - Removed unsorted tracking array
            (fun () ->
                let usage = Array.init numNetworks (fun i -> Array.zeroCreate<int> networkData.[i].CeLen)
                let buffer = Array.zeroCreate<Vector256<uint8>> maxWidth
                (usage, buffer)),

            // 2. The Hot Loop (Processing one block at a time)
            (fun currentBlock loopState ((localUsage: int [][]), workBuffer) ->
                let currentLen = currentBlock.Length

                for nIdx = 0 to numNetworks - 1 do
                    // Blit current vectors into the reusable thread-local buffer
                    Array.blit currentBlock.Vectors 0 workBuffer 0 currentLen
                    let data = networkData.[nIdx]
                    let ceCount = data.CeLen

                    // --- Hot Loop: CAS Sorting ---
                    for cIdx = 0 to ceCount - 1 do
                        let lIdx = data.Lows.[cIdx]
                        let hIdx = data.Highs.[cIdx]
                        let vLow = workBuffer.[lIdx]
                        let vHi = workBuffer.[hIdx]
                        let vMin = Vector256.Min(vLow, vHi)

                        if vLow <> vMin then
                            localUsage.[nIdx].[cIdx] <- localUsage.[nIdx].[cIdx] + int (BitOperations.PopCount((Vector256.ExtractMostSignificantBits(Vector256.GreaterThan(vLow, vHi)) &&& activeMask currentBlock.SortableCount)))
                            workBuffer.[lIdx] <- vMin
                            workBuffer.[hIdx] <- Vector256.Max(vLow, vHi)

                    let failures = unsortedLaneMask currentLen workBuffer
                    if failures <> 0u then
                        // Padding lanes never become collected test cases.
                        for lane = 0 to currentBlock.SortableCount - 1 do
                            if (failures &&& (1u <<< lane)) <> 0u then
                                let laneArray = Array.init currentLen (fun i -> int (workBuffer.[i].GetElement(lane)))
                                failureSets.[nIdx].TryAdd(laneArray, 0uy) |> ignore

                (localUsage, workBuffer)),

            // 3. Final Merge (Consolidating TLS into Global state)
            (fun ((localUsage: int [][]), _) ->
                for nIdx = 0 to numNetworks - 1 do
                    let globalArr = globalUsage.[nIdx]
                    for i = 0 to localUsage.[nIdx].Length - 1 do
                        globalArr.IncrementAtomicBy (i |> UMX.tag<ceIndex>) localUsage.[nIdx].[i]
            )
        ) |> ignore

        // 4. Assemble final ceBlockEval results
        Array.init numNetworks (fun i ->
            let uniqueFailures = failureSets.[i].Keys |> Seq.toArray
            let uniqueUnsortedCount = uniqueFailures.Length

            let failTest =
                if uniqueFailures.Length > 0 then
                    let sw = ceBlocks.[i].ChannelCount
                    let sss = 256<symbolSetSize>
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
                    (test: sortableUint8v256Tests)
                    (prefix: ceBlock)
                    (ceBlocks: ceBlock []) =
        evalAndCollectUniqueFailures test.SimdSortBlocks prefix ceBlocks
