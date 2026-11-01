namespace Sortopia.Evaluation

open FSharp.UMX
open Sortopia.Sorting.Inputs


module CeBlockOps =

    let private validate (tests: sortableTests) (prefix: ceBlock) (blocks: ceBlock[]) =
        let width = SortableTests.getChannelCount tests
        if isNull blocks then nullArg "ceBlocks"
        if %prefix.ChannelCount <> 0 && prefix.ChannelCount <> width then invalidArg "prefix" "Prefix width must match the inputs."
        if blocks |> Array.exists (fun block -> block.ChannelCount <> width) then invalidArg "ceBlocks" "Network widths must match the inputs."

    let evalWithSorterTest
                (sortableTs: sortableTests)
                (prefix: ceBlock)
                (ceBlock: ceBlock)
                (collectNewSortableTests: bool<collectNewSortableTests>) : ceBlockEval =
        validate sortableTs prefix [| ceBlock |]
        match sortableTs with
        | sortableTests.Ints sits ->
            if %collectNewSortableTests then
                CeBlockOpsInt.evalAndCollectNewSortableTests sits prefix ceBlock
            else
                CeBlockOpsInt.eval sits prefix ceBlock

        | sortableTests.Bools sbts ->
            if %collectNewSortableTests then
                CeBlockOpsBinary.evalAndCollectNewSortableTests sbts prefix ceBlock
            else
                CeBlockOpsBinary.eval sbts prefix ceBlock

        | sortableTests.PackedInts packedTs ->
            if %collectNewSortableTests then
                CeBlockOpsPacked.evalAndCollectNewSortableTests packedTs prefix ceBlock
            else
                CeBlockOpsPacked.eval packedTs prefix ceBlock

        | sortableTests.Uint8v256 su8v256ts ->
            if %collectNewSortableTests then
                CeBlockOpsUint8v256.evalAndCollectNewSortableTests su8v256ts prefix [| ceBlock |] |> Array.head
            else
                CeBlockOpsUint8v256.eval su8v256ts prefix [| ceBlock |] |> Array.head

        | sortableTests.Uint8v512 su8by512ts ->
            if %collectNewSortableTests then
                CeBlockOpsUint8v512.evalAndCollectNewSortableTests su8by512ts prefix [| ceBlock |] |> Array.head
            else
                CeBlockOpsUint8v512.eval su8by512ts prefix [| ceBlock |] |> Array.head

        | sortableTests.Bitv512 su8v256ts ->
            if %collectNewSortableTests then
                CeBlockOpsBitv512.evalAndCollectNewSortableTests su8v256ts prefix [| ceBlock |] |> Array.head
            else
                CeBlockOpsBitv512.eval su8v256ts prefix [| ceBlock |] |> Array.head


    let evalWithSorterTests
                (sortableTs: sortableTests)
                (prefix: ceBlock)
                (ceBlocks: ceBlock [])
                (collectNewSortableTests: bool<collectNewSortableTests>) : ceBlockEval [] =

        match sortableTs with

        | sortableTests.Bitv512 su8v256ts ->
                if %collectNewSortableTests then
                    CeBlockOpsBitv512.evalAndCollectNewSortableTests su8v256ts prefix ceBlocks
                else
                    CeBlockOpsBitv512.eval su8v256ts prefix ceBlocks

        | sortableTests.Bools sbts ->
            ceBlocks |> Array.map (fun ceBlock ->
                if %collectNewSortableTests then
                    CeBlockOpsBinary.evalAndCollectNewSortableTests sbts prefix ceBlock
                else
                    CeBlockOpsBinary.eval sbts prefix ceBlock)

        | sortableTests.Ints sits ->
            ceBlocks |> Array.map (fun ceBlock ->
                if %collectNewSortableTests then
                    CeBlockOpsInt.evalAndCollectNewSortableTests sits prefix ceBlock
                else
                    CeBlockOpsInt.eval sits prefix ceBlock)

        | sortableTests.PackedInts packedTs ->
            ceBlocks |> Array.map (fun ceBlock ->
                if %collectNewSortableTests then
                    CeBlockOpsPacked.evalAndCollectNewSortableTests packedTs prefix ceBlock
                else
                    CeBlockOpsPacked.eval packedTs prefix ceBlock)

        | sortableTests.Uint8v256 su8v256ts ->
                if %collectNewSortableTests then
                    CeBlockOpsUint8v256.evalAndCollectNewSortableTests su8v256ts prefix ceBlocks
                else
                    CeBlockOpsUint8v256.eval su8v256ts prefix ceBlocks

        | sortableTests.Uint8v512 su8by512ts ->
                if %collectNewSortableTests then
                    CeBlockOpsUint8v512.evalAndCollectNewSortableTests su8by512ts prefix ceBlocks
                else
                    CeBlockOpsUint8v512.eval su8by512ts prefix ceBlocks
