namespace Sortopia.Evaluation

open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Sorting.Inputs


module CeBlockOps =

    let evalWithSorterTestsWithOptions
            (options: ParallelOptions)
            (sortableTs: sortableTests)
            (prefix: ceBlock)
            (ceBlocks: ceBlock[])
            (collectNewSortableTests: bool<collectNewSortableTests>) : ceBlockEval[] =
        options.CancellationToken.ThrowIfCancellationRequested()
        if isNull ceBlocks then nullArg "ceBlocks"
        if Array.isEmpty ceBlocks then
            CeBlock.validatePrefixWidth (SortableTests.getChannelCount sortableTs) prefix
            [||]
        else
            let scalar count collect inputs =
                let evaluate = if %collectNewSortableTests then collect else count
                Array.map (evaluate inputs prefix) ceBlocks
            let batch count collect inputs =
                let evaluate = if %collectNewSortableTests then collect else count
                evaluate inputs prefix ceBlocks
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

    let evalWithSorterTests tests prefix blocks collect =
        evalWithSorterTestsWithOptions (ParallelOptions()) tests prefix blocks collect

    let evalWithSorterTest
            (sortableTs: sortableTests)
            (prefix: ceBlock)
            (ceBlock: ceBlock)
            (collectNewSortableTests: bool<collectNewSortableTests>) : ceBlockEval =
        evalWithSorterTests sortableTs prefix [|ceBlock|] collectNewSortableTests |> Array.head
