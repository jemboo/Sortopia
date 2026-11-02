namespace Sortopia.Evaluation

open FSharp.UMX
open Sortopia.Sorting.Inputs


module CeBlockOps =

    let evalWithSorterTests
            (sortableTs: sortableTests)
            (prefix: ceBlock)
            (ceBlocks: ceBlock[])
            (collectNewSortableTests: bool<collectNewSortableTests>) : ceBlockEval[] =
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
                scalar CeBlockOpsInt.eval CeBlockOpsInt.evalAndCollectNewSortableTests inputs
            | sortableTests.Bools inputs ->
                scalar CeBlockOpsBinary.eval CeBlockOpsBinary.evalAndCollectNewSortableTests inputs
            | sortableTests.PackedInts inputs ->
                scalar CeBlockOpsPacked.eval CeBlockOpsPacked.evalAndCollectNewSortableTests inputs
            | sortableTests.Bitv512 inputs ->
                batch CeBlockOpsBitv512.eval CeBlockOpsBitv512.evalAndCollectNewSortableTests inputs
            | sortableTests.Uint8v256 inputs ->
                batch CeBlockOpsUint8v256.eval CeBlockOpsUint8v256.evalAndCollectNewSortableTests inputs
            | sortableTests.Uint8v512 inputs ->
                batch CeBlockOpsUint8v512.eval CeBlockOpsUint8v512.evalAndCollectNewSortableTests inputs

    let evalWithSorterTest
            (sortableTs: sortableTests)
            (prefix: ceBlock)
            (ceBlock: ceBlock)
            (collectNewSortableTests: bool<collectNewSortableTests>) : ceBlockEval =
        evalWithSorterTests sortableTs prefix [|ceBlock|] collectNewSortableTests |> Array.head
