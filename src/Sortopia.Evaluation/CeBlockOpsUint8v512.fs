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

    let private evaluateNetworks (options: ParallelOptions) collect
                                 (blocks: sortBlockUint8v512[]) networks =
        let sources = blocks |> Array.map _.Vectors
        SimdBatch.mapNetworks options (fun _ network ->
            let data = SimdBatch.comparatorChannels network
            let usage = Array.zeroCreate<int> data.Lows.Length
            let work = Array.zeroCreate<Vector512<uint8>> (int network.ChannelCount)
            let failures = if collect then Some (HashSet<int[]>(ArrayContentComparer<int>())) else None
            let mutable unsorted = 0
            for blockIndex = 0 to blocks.Length - 1 do
                options.CancellationToken.ThrowIfCancellationRequested()
                let block = blocks[blockIndex]
                Array.blit sources[blockIndex] 0 work 0 block.Length
                let active = activeMask block.SortableCount
                for index = 0 to data.Lows.Length - 1 do
                    if (index &&& 255) = 0 then options.CancellationToken.ThrowIfCancellationRequested()
                    let low, high = data.Lows[index], data.Highs[index]
                    let vLow, vHigh = work[low], work[high]
                    let minimum = Vector512.Min(vLow, vHigh)
                    if vLow <> minimum then
                        usage[index] <- usage[index] + int (BitOperations.PopCount(Vector512.ExtractMostSignificantBits(Vector512.GreaterThan(vLow, vHigh)) &&& active))
                        work[low] <- minimum
                        work[high] <- Vector512.Max(vLow, vHigh)
                let mutable bits = unsortedLaneMask block.Length work &&& active
                unsorted <- unsorted + int (BitOperations.PopCount bits)
                match failures with
                | None -> ()
                | Some values ->
                    while bits <> 0UL do
                        let lane = BitOperations.TrailingZeroCount bits
                        values.Add(Array.init block.Length (fun channel -> int (work[channel].GetElement(lane)))) |> ignore
                        bits <- bits &&& (bits - 1UL)
            SimdBatch.finishNetwork network usage unsorted 256<symbolSetSize> failures) networks

    let private countFailuresByBlocks
        (options: ParallelOptions)
        (simdSortableBlocks: sortBlockUint8v512[])
        (ceBlocks: ceBlock array)
        : ceBlockEval [] =

        let numNetworks = ceBlocks.Length
        if numNetworks = 0 then [||] else

        let maxWidth = ceBlocks |> Array.maxBy (fun c -> %c.ChannelCount) |> (fun c -> %c.ChannelCount)
        let networkData = Array.map SimdBatch.comparatorChannels ceBlocks

        let globalUsage = Array.init numNetworks (fun i -> ceUseCounts.Create(ceBlocks.[i].CeLength))
        let globalUnsorted = Array.zeroCreate<int> numNetworks

        Parallel.ForEach(
            simdSortableBlocks,
            options,
            (fun () ->
                let usage = Array.init numNetworks (fun i -> Array.zeroCreate<int> networkData.[i].Lows.Length)
                let unsorted = Array.zeroCreate<int> numNetworks
                let buffer = Array.zeroCreate<Vector512<uint8>> maxWidth
                (usage, unsorted, buffer)),

            (fun currentBlock loopState ((localUsage : int [][]), (localUnsorted: int[]), workBuffer) ->
                let currentLen = currentBlock.Length
                let source = currentBlock.Vectors

                for nIdx = 0 to numNetworks - 1 do
                    options.CancellationToken.ThrowIfCancellationRequested()
                    Array.blit source 0 workBuffer 0 currentLen
                    let data = networkData.[nIdx]
                    let ceCount = data.Lows.Length

                    for cIdx = 0 to ceCount - 1 do
                        if (cIdx &&& 255) = 0 then options.CancellationToken.ThrowIfCancellationRequested()
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
                    options.CancellationToken.ThrowIfCancellationRequested()
                    Interlocked.Add(&globalUnsorted.[nIdx], localUnsorted.[nIdx]) |> ignore
                    let globalArr = globalUsage.[nIdx]
                    for i = 0 to localUsage.[nIdx].Length - 1 do
                        globalArr.IncrementAtomicBy (i |> UMX.tag) localUsage.[nIdx].[i]
            )
        ) |> ignore

        Array.init numNetworks (fun i ->
            ceBlockEval.create ceBlocks.[i] globalUsage.[i] (globalUnsorted.[i] |> UMX.tag) None
        )

    let private countFailures options (blocks: sortBlockUint8v512[]) (networks: ceBlock[]) =
        if SimdBatch.preferNetworks options networks.Length blocks.Length then
            evaluateNetworks options false blocks networks
        else countFailuresByBlocks options blocks networks

    let evalSimdSortBlocks (simdSortableBlocks: sortBlockUint8v512 seq) (ceBlocks: ceBlock[]) =
        let inputs = SimdBatch.prepareBlocks (fun (block: sortBlockUint8v512) -> block.Length) ceBlocks simdSortableBlocks
        countFailures (ParallelOptions()) inputs ceBlocks

    let private collectFailuresByBlocks
            (options: ParallelOptions)
            (simdSortableBlocks: sortBlockUint8v512[])
            (ceBlocks: ceBlock array)
            : ceBlockEval [] =

        let numNetworks = ceBlocks.Length
        if numNetworks = 0 then [||] else

        let maxWidth = ceBlocks |> Array.maxBy (fun c -> %c.ChannelCount) |> (fun c -> %c.ChannelCount)
        let networkData = Array.map SimdBatch.comparatorChannels ceBlocks

        let globalUsage = Array.init numNetworks (fun i -> ceUseCounts.Create(ceBlocks.[i].CeLength))
        let failureSets = Array.init numNetworks (fun _ -> ConcurrentDictionary<int[], byte>(ArrayContentComparer<int>()))

        Parallel.ForEach(
            simdSortableBlocks,
            options,
            (fun () ->
                let usage = Array.init numNetworks (fun i -> Array.zeroCreate<int> networkData.[i].Lows.Length)
                let buffer = Array.zeroCreate<Vector512<uint8>> maxWidth
                // Removed unsorted array from thread-local state
                (usage, buffer)),

            (fun currentBlock loopState ((localUsage: int[][]), workBuffer) ->
                let currentLen = currentBlock.Length
                let source = currentBlock.Vectors

                for nIdx = 0 to numNetworks - 1 do
                    options.CancellationToken.ThrowIfCancellationRequested()
                    Array.blit source 0 workBuffer 0 currentLen
                    let data = networkData.[nIdx]
                    let ceCount = data.Lows.Length

                    for cIdx = 0 to ceCount - 1 do
                        if (cIdx &&& 255) = 0 then options.CancellationToken.ThrowIfCancellationRequested()
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
                    options.CancellationToken.ThrowIfCancellationRequested()
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
                ceBlocks.[i]
                globalUsage.[i]
                (uniqueUnsortedCount |> UMX.tag<sortableCount>)
                failTest
        )

    let private collectFailures options (blocks: sortBlockUint8v512[]) (networks: ceBlock[]) =
        if SimdBatch.preferNetworks options networks.Length blocks.Length then
            evaluateNetworks options true blocks networks
        else collectFailuresByBlocks options blocks networks

    let evalAndCollectUniqueFailures (simdSortableBlocks: sortBlockUint8v512 seq) (ceBlocks: ceBlock[]) =
        let inputs = SimdBatch.prepareBlocks (fun (block: sortBlockUint8v512) -> block.Length) ceBlocks simdSortableBlocks
        collectFailures (ParallelOptions()) inputs ceBlocks

    let eval (test: sortableUint8v512Tests) (ceBlocks: ceBlock []) =
        CeBlock.validateBatchWidth test.ChannelCount ceBlocks
        countFailures (ParallelOptions()) test.SimdSortBlocks ceBlocks

    let evalAndCollectNewSortableTests (test: sortableUint8v512Tests) (ceBlocks: ceBlock []) =
        CeBlock.validateBatchWidth test.ChannelCount ceBlocks
        collectFailures (ParallelOptions()) test.SimdSortBlocks ceBlocks

    /// Runtime limits apply to this invocation only; inputs remain unchanged.
    let evalWithOptions (options: ParallelOptions) (test: sortableUint8v512Tests) ceBlocks =
        options.CancellationToken.ThrowIfCancellationRequested()
        CeBlock.validateBatchWidth test.ChannelCount ceBlocks
        countFailures options test.SimdSortBlocks ceBlocks

    let evalAndCollectWithOptions (options: ParallelOptions) (test: sortableUint8v512Tests) ceBlocks =
        options.CancellationToken.ThrowIfCancellationRequested()
        CeBlock.validateBatchWidth test.ChannelCount ceBlocks
        collectFailures options test.SimdSortBlocks ceBlocks
