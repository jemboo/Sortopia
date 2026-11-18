namespace Sortopia.Evaluation

open System
open System.Collections.Generic
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

module internal SimdBatch =
    // Choose one parallel dimension. Small test sets cannot keep many block workers busy.
    let preferNetworks (options: ParallelOptions) networkCount blockCount =
        let workers = if options.MaxDegreeOfParallelism < 1 then Environment.ProcessorCount else options.MaxDegreeOfParallelism
        networkCount >= blockCount || workers = 1

    /// Each result slot and its mutable counters have one owner; preserve input order.
    let mapNetworks (options: ParallelOptions) evaluate (networks: ceBlock[]) =
        EvaluationParallel.mapIndexed options evaluate networks

    let finishNetwork prefix (network: ceBlock) usage unsorted symbolCount (failures: HashSet<int[]> option) =
        let count, residual =
            match failures with
            | None -> unsorted, None
            | Some values when values.Count = 0 -> 0, None
            | Some values ->
                let tests = values |> Seq.map (fun value -> sortableIntArray.create(value, network.ChannelCount, symbolCount)) |> Seq.toArray
                values.Count, Some (sortableIntTests.create (UMX.tag (Guid.NewGuid())) network.ChannelCount tests |> sortableTests.Ints)
        ceBlockEval.create prefix network (ceUseCounts.CreateFromArray usage) (UMX.tag count) residual

    let comparatorChannels (network: ceBlock) =
        let ces = network.CeArray
        {| Lows = ces |> Array.map (fun ce -> int ce.LowChannel)
           Highs = ces |> Array.map (fun ce -> int ce.HighChannel) |}

    // Raw block streams carry width on each block. Typed datasets validate their blocks at construction.
    let prepareBlocks (widthOf: 'block -> int) prefix (networks: ceBlock[]) blocks =
        if isNull networks then nullArg "ceBlocks"
        let inputs = Seq.toArray blocks
        if inputs.Length > 0 && networks.Length > 0 then
            let width = widthOf inputs[0]
            CeBlock.validateBatchWidth (UMX.tag width) prefix networks
            if inputs |> Array.exists (fun block -> widthOf block <> width) then
                invalidArg "simdSortableBlocks" "All input blocks must have the same width."
        inputs
