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

    let private activeBits count word =
        let active = max 0 (min 64 (count - word * 64))
        if active = 64 then UInt64.MaxValue else (1UL <<< active) - 1UL

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
                total <- total + int (BitOperations.PopCount(swaps.GetElement(i) &&& activeBits count i))
            total
        else
            0

    let applyCas workBuffer low high = applyCasCount 512 workBuffer low high > 0

    let private countFailures
        (token: CancellationToken)
        reportFirstFailedLane
        (simdSortableBlocks: sortBlockBitv512[])
        (prefix: ceBlock)
        (ceBlocks: ceBlock array)
        : ceBlockEval [] =

        let numNetworks = ceBlocks.Length
        if numNetworks = 0 then [||] else

        let maxWidth = ceBlocks |> Array.maxBy (fun c -> %c.ChannelCount) |> (fun c -> %c.ChannelCount)
        let networkData = Array.map SimdBatch.comparatorChannels ceBlocks

        let globalUsage = Array.init numNetworks (fun i -> ceUseCounts.Create(ceBlocks.[i].CeLength))
        let globalUnsortedCount = Array.zeroCreate<int> numNetworks

        Parallel.For(
            0, simdSortableBlocks.Length, ParallelOptions(CancellationToken = token),
            (fun () ->
                let usage = Array.init numNetworks (fun i -> Array.zeroCreate<int> networkData.[i].Lows.Length)
                let unsortedCount = Array.zeroCreate<int> numNetworks
                let workBuffer = Array.zeroCreate<Vector512<uint64>> maxWidth
                (usage, unsortedCount, workBuffer)),

            (fun blockIndex _ ((localUsage: int [][]), (localUnsortedCount: int[]), workBuffer) ->
                let currentBlock = simdSortableBlocks[blockIndex]
                let currentLen = currentBlock.Length
                let source = currentBlock.Vectors

                for nIdx = 0 to numNetworks - 1 do
                    Array.blit source 0 workBuffer 0 currentLen
                    let data = networkData.[nIdx]

                    for cIdx = 0 to data.Lows.Length - 1 do
                        if (cIdx &&& 255) = 0 then token.ThrowIfCancellationRequested()
                        let swaps = applyCasCount currentBlock.SortableCount workBuffer data.Lows.[cIdx] data.Highs.[cIdx]
                        localUsage.[nIdx].[cIdx] <- localUsage.[nIdx].[cIdx] + swaps

                    // Check which of the 512 bits are unsorted
                    let unsortedMask = getUnsortedMask currentLen workBuffer
                    if unsortedMask <> Vector512<uint64>.Zero then
                        // Count how many bits (lanes) are failed in this block
                        // Note: Only count bits up to SortableCount
                        let mutable failures = 0
                        for word = 0 to 7 do
                            let bits = unsortedMask.GetElement(word) &&& activeBits currentBlock.SortableCount word
                            if bits <> 0UL then
                                if failures = 0 then
                                    reportFirstFailedLane nIdx blockIndex (word * 64 + BitOperations.TrailingZeroCount(bits))
                                failures <- failures + int (BitOperations.PopCount(bits))
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

    let evalSimdSortBlocks (simdSortableBlocks: sortBlockBitv512 seq) (prefix: ceBlock) (ceBlocks: ceBlock[]) =
        let inputs = SimdBatch.prepareBlocks (fun (block: sortBlockBitv512) -> block.Length) prefix ceBlocks simdSortableBlocks
        countFailures CancellationToken.None (fun _ _ _ -> ()) inputs prefix ceBlocks

    let eval
            (test: sortableBitv512Tests)
            (prefix: ceBlock)
            (ceBlocks: ceBlock []) =
            CeBlock.validateBatchWidth test.ChannelCount prefix ceBlocks
            countFailures CancellationToken.None (fun _ _ _ -> ()) test.SimdSortBlocks prefix ceBlocks

    /// Counts swaps and failures for one sorter and returns its first failing input in dataset order.
    let evalWithCounterexample (test: sortableBitv512Tests) (network: ceBlock) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        CeBlock.validateWidth test.ChannelCount ceBlock.Empty network
        let blocks = test.SimdSortBlocks
        let firstLanes = Array.create blocks.Length -1
        let recordLane _ blockIndex lane = firstLanes[blockIndex] <- lane
        let result = countFailures token recordLane blocks ceBlock.Empty [| network |] |> Array.exactlyOne
        let firstInput =
            firstLanes |> Array.tryFindIndex (fun lane -> lane >= 0)
            |> Option.map (fun index ->
                let lane = firstLanes[index]
                blocks[index].Vectors |> Array.map (fun wire -> int ((wire.GetElement(lane / 64) >>> (lane % 64)) &&& 1UL)))
        result, firstInput


    let private collectFailures
            (simdSortableBlocks: sortBlockBitv512[])
            (prefix: ceBlock)
            (ceBlocks: ceBlock array)
            : ceBlockEval [] =

        let numNetworks = ceBlocks.Length
        if numNetworks = 0 then [||] else

        let maxWidth = ceBlocks |> Array.maxBy (fun c -> %c.ChannelCount) |> (fun c -> %c.ChannelCount)
        let networkData = Array.map SimdBatch.comparatorChannels ceBlocks

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
                let usage = Array.init numNetworks (fun i -> Array.zeroCreate<int> networkData.[i].Lows.Length)
                let workBuffer = Array.zeroCreate<Vector512<uint64>> maxWidth
                (usage, workBuffer)),

            // 2. The Hot Loop
            (fun currentBlock loopState ((localUsage: int[][]), workBuffer) ->
                let currentLen = currentBlock.Length
                let source = currentBlock.Vectors

                for nIdx = 0 to numNetworks - 1 do
                    Array.blit source 0 workBuffer 0 currentLen
                    let data = networkData.[nIdx]

                    // 1. Run the sorting network
                    for cIdx = 0 to data.Lows.Length - 1 do
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

    let evalAndCollectUniqueFailures (simdSortableBlocks: sortBlockBitv512 seq) (prefix: ceBlock) (ceBlocks: ceBlock[]) =
        let inputs = SimdBatch.prepareBlocks (fun (block: sortBlockBitv512) -> block.Length) prefix ceBlocks simdSortableBlocks
        collectFailures inputs prefix ceBlocks

    let evalAndCollectNewSortableTests
            (test: sortableBitv512Tests)
            (prefix: ceBlock)
            (ceBlocks: ceBlock []) =
        CeBlock.validateBatchWidth test.ChannelCount prefix ceBlocks
        collectFailures test.SimdSortBlocks prefix ceBlocks
