namespace Sortopia.Evaluation

open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Sorting.Inputs


module CeBlockOps =

    let evalWithSorterTestsWithOptions
            (options: ParallelOptions)
            (sortableTs: sortableTests)
            (ceBlocks: ceBlock[])
            (collectNewSortableTests: bool<collectNewSortableTests>) : ceBlockEval[] =
        options.CancellationToken.ThrowIfCancellationRequested()
        if isNull ceBlocks then nullArg "ceBlocks"
        if Array.isEmpty ceBlocks then
            [||]
        else
            let scalar count collect inputs =
                let evaluate = if %collectNewSortableTests then collect else count
                CeBlock.validateBatchWidth (SortableTests.getChannelCount sortableTs) ceBlocks
                SimdBatch.mapNetworks options (fun _ network -> evaluate inputs network) ceBlocks
            let batch count collect inputs =
                let evaluate = if %collectNewSortableTests then collect else count
                evaluate inputs ceBlocks
            match sortableTs with
            | sortableTests.Ints inputs ->
                scalar (CeBlockOpsInt.evalWithCancellation options.CancellationToken) (CeBlockOpsInt.evalAndCollectWithCancellation options.CancellationToken) inputs
            | sortableTests.Bools inputs ->
                scalar (CeBlockOpsBinary.evalWithCancellation options.CancellationToken) (CeBlockOpsBinary.evalAndCollectWithCancellation options.CancellationToken) inputs
            | sortableTests.PackedInts inputs ->
                scalar (CeBlockOpsPacked.evalWithCancellation options.CancellationToken) (CeBlockOpsPacked.evalAndCollectWithCancellation options.CancellationToken) inputs
            | sortableTests.Bitv512 inputs ->
                batch (CeBlockOpsBitv512.evalWithOptions options) (CeBlockOpsBitv512.evalAndCollectWithOptions options) inputs
            | sortableTests.Uint8v256 inputs ->
                batch (CeBlockOpsUint8v256.evalWithOptions options) (CeBlockOpsUint8v256.evalAndCollectWithOptions options) inputs
            | sortableTests.Uint8v512 inputs ->
                batch (CeBlockOpsUint8v512.evalWithOptions options) (CeBlockOpsUint8v512.evalAndCollectWithOptions options) inputs

    let evalWithSorterTests tests blocks collect =
        evalWithSorterTestsWithOptions (ParallelOptions()) tests blocks collect

    let evalWithSorterTest
            (sortableTs: sortableTests)
            (ceBlock: ceBlock)
            (collectNewSortableTests: bool<collectNewSortableTests>) : ceBlockEval =
        evalWithSorterTests sortableTs [|ceBlock|] collectNewSortableTests |> Array.head
