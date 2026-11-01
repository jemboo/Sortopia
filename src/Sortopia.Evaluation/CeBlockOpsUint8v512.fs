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

module CeBlockOpsUint8v512 =

    let private activeMask count =
        if count = 64 then UInt64.MaxValue else (1uL <<< count) - 1uL


    /// Check if all 64 lanes in the block are sorted.
    let IsBlockSorted (len: int) (vectors: Vector512<uint8>[]) =
        let mutable sorted = true
        let mutable i = 0
        while i < len - 1 && sorted do
            let vLow = vectors.[i]
            let vHi = vectors.[i+1]
            if Vector512.Min(vLow, vHi) <> vLow then
                sorted <- false
            else
                i <- i + 1
        sorted

    /// Mask-optimized: Check if all 64 lanes in the block are sorted.
    let IsBlockSortedMask (len: int) (vectors: Vector512<uint8>[]) =
        let mutable sorted = true
        let mutable i = 0
        while i < len - 1 do
            let vLow = vectors.[i]
            let vHi = vectors.[i+1]

            // Mask: 0xFF where ordered (Low <= Hi), 0x00 otherwise
            let mask = Vector512.Equals(Vector512.Min(vLow, vHi), vLow)

            // AVX-512 ExtractMostSignificantBits for uint8 returns uint64 (64 bits)
            let bitmask = Vector512.ExtractMostSignificantBits(mask)

            // If all 64 lanes are sorted, bitmask will be 0xFFFFFFFFFFFFFFFF
            if bitmask <> UInt64.MaxValue then
                sorted <- false
                i <- len // Break
            else
                i <- i + 1
        sorted

    /// One bit per lane, set when any adjacent channel pair is out of order.
    let unsortedLaneMask (len: int) (vectors: Vector512<uint8>[]) =
        let mutable failures = 0uL
        for i = 0 to len - 2 do
            let inversions = Vector512.GreaterThan(vectors.[i], vectors.[i + 1])
            failures <- failures ||| Vector512.ExtractMostSignificantBits(inversions)
        failures

    let inline hashLane (block: Vector512<uint8>[]) (lane: int) =
        let mutable h = 17
        for i = 0 to block.Length - 1 do
            h <- h * 31 + int (block.[i].GetElement(lane))
        h

    let evalSimdSortBlocks
        (simdSortableBlocks: sortBlockUint8v512 seq)
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
            (fun () ->
                let usage = Array.init numNetworks (fun i -> Array.zeroCreate<int> networkData.[i].CeLen)
                let unsorted = Array.zeroCreate<int> numNetworks
                let buffer = Array.zeroCreate<Vector512<uint8>> maxWidth
                (usage, unsorted, buffer)),

            (fun currentBlock loopState ((localUsage : int [][]), (localUnsorted: int[]), workBuffer) ->
                let currentLen = currentBlock.Length

                for nIdx = 0 to numNetworks - 1 do
                    Array.blit currentBlock.Vectors 0 workBuffer 0 currentLen
                    let data = networkData.[nIdx]
                    let ceCount = data.CeLen

                    for cIdx = 0 to ceCount - 1 do
                        let lIdx = data.Lows.[cIdx]
                        let hIdx = data.Highs.[cIdx]
                        let vLow = workBuffer.[lIdx]
                        let vHi = workBuffer.[hIdx]
                        let vMin = Vector512.Min(vLow, vHi)

                        if vLow <> vMin then
                            localUsage.[nIdx].[cIdx] <- localUsage.[nIdx].[cIdx] + int (BitOperations.PopCount((Vector512.ExtractMostSignificantBits(Vector512.GreaterThan(vLow, vHi)) &&& activeMask currentBlock.SortableCount)))
                            workBuffer.[lIdx] <- vMin
                            workBuffer.[hIdx] <- Vector512.Max(vLow, vHi)

                    let failures = unsortedLaneMask currentLen workBuffer
                    localUnsorted.[nIdx] <- localUnsorted.[nIdx] + int (BitOperations.PopCount(failures &&& activeMask currentBlock.SortableCount))

                (localUsage, localUnsorted, workBuffer)),

            (fun ((localUsage: int [][]), (localUnsorted: int[]), _) ->
                for nIdx = 0 to numNetworks - 1 do
                    Interlocked.Add(&globalUnsorted.[nIdx], localUnsorted.[nIdx]) |> ignore
                    let globalArr = globalUsage.[nIdx]
                    for i = 0 to localUsage.[nIdx].Length - 1 do
                        globalArr.IncrementAtomicBy (i |> UMX.tag) localUsage.[nIdx].[i]
            )
        ) |> ignore

        Array.init numNetworks (fun i ->
            ceBlockEval.create prefix ceBlocks.[i] globalUsage.[i] (globalUnsorted.[i] |> UMX.tag) None
        )


    let evalAndCollectUniqueFailures
            (simdSortableBlocks: sortBlockUint8v512 seq)
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
        let failureSets = Array.init numNetworks (fun _ -> ConcurrentDictionary<int[], byte>(ArrayContentComparer<int>()))

        Parallel.ForEach(
            simdSortableBlocks,
            (fun () ->
                let usage = Array.init numNetworks (fun i -> Array.zeroCreate<int> networkData.[i].CeLen)
                let buffer = Array.zeroCreate<Vector512<uint8>> maxWidth
                // Removed unsorted array from thread-local state
                (usage, buffer)),

            (fun currentBlock loopState ((localUsage: int[][]), workBuffer) ->
                let currentLen = currentBlock.Length

                for nIdx = 0 to numNetworks - 1 do
                    Array.blit currentBlock.Vectors 0 workBuffer 0 currentLen
                    let data = networkData.[nIdx]
                    let ceCount = data.CeLen

                    for cIdx = 0 to ceCount - 1 do
                        let lIdx = data.Lows.[cIdx]
                        let hIdx = data.Highs.[cIdx]
                        let vLow = workBuffer.[lIdx]
                        let vHi = workBuffer.[hIdx]
                        let vMin = Vector512.Min(vLow, vHi)

                        if vLow <> vMin then
                            localUsage.[nIdx].[cIdx] <- localUsage.[nIdx].[cIdx] + int (BitOperations.PopCount((Vector512.ExtractMostSignificantBits(Vector512.GreaterThan(vLow, vHi)) &&& activeMask currentBlock.SortableCount)))
                            workBuffer.[lIdx] <- vMin
                            workBuffer.[hIdx] <- Vector512.Max(vLow, vHi)

                    let failures = unsortedLaneMask currentLen workBuffer
                    if failures <> 0uL then
                        // Padding lanes never become collected test cases.
                        for lane = 0 to currentBlock.SortableCount - 1 do
                            if (failures &&& (1uL <<< lane)) <> 0uL then
                                let laneArray = Array.init currentLen (fun i -> int (workBuffer.[i].GetElement(lane)))
                                failureSets.[nIdx].TryAdd(laneArray, 0uy) |> ignore

                (localUsage, workBuffer)),

            (fun ((localUsage: int[][]), _) ->
                for nIdx = 0 to numNetworks - 1 do
                    // Removed Interlocked.Add block tracking entirely
                    let globalArr = globalUsage.[nIdx]
                    for i = 0 to localUsage.[nIdx].Length - 1 do
                        globalArr.IncrementAtomicBy (i |> UMX.tag) localUsage.[nIdx].[i]
            )
        ) |> ignore

        Array.init numNetworks (fun i ->
            let uniqueFailures = failureSets.[i].Keys |> Seq.toArray

            // Derive your sortableCount directly from the distinct elements count
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

    let eval (test: sortableUint8v512Tests) (prefix: ceBlock) (ceBlocks: ceBlock []) =
        evalSimdSortBlocks test.SimdSortBlocks prefix ceBlocks

    let evalAndCollectNewSortableTests (test: sortableUint8v512Tests) (prefix: ceBlock) (ceBlocks: ceBlock []) =
        evalAndCollectUniqueFailures test.SimdSortBlocks prefix ceBlocks
