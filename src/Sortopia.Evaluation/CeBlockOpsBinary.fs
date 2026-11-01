namespace Sortopia.Evaluation

open System
open System.Buffers
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

module CeBlockOpsBinary =


    let eval (sbts: sortableBinaryTests) (prefix: ceBlock) (ceBlk: ceBlock) :ceBlockEval =
            CeBlock.validateWidth sbts.ChannelCount prefix ceBlk
            let ceUseCounts = ceUseCounts.Create ceBlk.CeLength
            let mutable unsortedCount = 0
            let ces = ceBlk.CeArray
            let sw = sbts.ChannelCount
            let pool = ArrayPool<bool>.Shared

            for sba in sbts.SortableBinaryArrays do
                let workArray = pool.Rent(%sw)
                try
                    Array.blit sba.Values 0 workArray 0 %sw

                    for i = 0 to ces.Length - 1 do
                        let ce = ces.[i]
                        // Boolean Comparison: true (1) > false (0)
                        if workArray.[(int ce.LowChannel)] && not workArray.[(int ce.HighChannel)] then
                            workArray.[(int ce.LowChannel)] <- false
                            workArray.[(int ce.HighChannel)] <- true
                            ceUseCounts.Increment (i |> UMX.tag<ceIndex>)

                    // A bool array is sorted if it's in the form [false, false, ..., true, true]
                    let isSorted =
                        let mutable ok = true
                        let mutable j = 0
                        while j < %sw - 1 && ok do
                            // Check if we have a 'true' followed by a 'false'
                            if workArray.[j] && not workArray.[j+1] then ok <- false
                            else j <- j + 1
                        ok

                    if not isSorted then
                        unsortedCount <- unsortedCount + 1

                finally
                    pool.Return(workArray)

            ceBlockEval.create prefix ceBlk ceUseCounts (unsortedCount |> UMX.tag<sortableCount>) None


    let evalAndCollectNewSortableTests (sbts: sortableBinaryTests) (prefix: ceBlock) (ceBlk: ceBlock) :ceBlockEval =
            CeBlock.validateWidth sbts.ChannelCount prefix ceBlk
            let ceUseCounts = ceUseCounts.Create ceBlk.CeLength
            let ces = ceBlk.CeArray
            let sw = sbts.ChannelCount
            let pool = ArrayPool<bool>.Shared
            // Ensure SortableBoolArrayValueComparer is defined similarly to the Int version
            let results = HashSet<sortableBoolArray>(SortableBoolArray.SortableBoolArrayValueComparer())

            for sba in sbts.SortableBinaryArrays do
                let workArray = pool.Rent(%sw)
                try
                    Array.blit sba.Values 0 workArray 0 %sw

                    for i = 0 to ces.Length - 1 do
                        let ce = ces.[i]
                        // Boolean Comparison: true (1) > false (0)
                        if workArray.[(int ce.LowChannel)] && not workArray.[(int ce.HighChannel)] then
                            workArray.[(int ce.LowChannel)] <- false
                            workArray.[(int ce.HighChannel)] <- true
                            ceUseCounts.Increment (i |> UMX.tag<ceIndex>)

                    // A bool array is sorted if it's in the form [false, false, ..., true, true]
                    let isSorted =
                        let mutable ok = true
                        let mutable j = 0
                        while j < %sw - 1 && ok do
                            // Check if we have a 'true' followed by a 'false'
                            if workArray.[j] && not workArray.[j+1] then ok <- false
                            else j <- j + 1
                        ok

                    if not isSorted then
                        let finalValues = Array.zeroCreate %sw
                        Array.blit workArray 0 finalValues 0 %sw
                        let resultSba = sortableBoolArray.create(finalValues, sw)
                        results.Add(resultSba) |> ignore

                finally
                    pool.Return(workArray)

            let newTests = Seq.toArray results |> sortableBinaryTests.create (Guid.NewGuid() |> UMX.tag) sw
            ceBlockEval.create
                            prefix
                            ceBlk
                            ceUseCounts
                            (results.Count |> UMX.tag<sortableCount>)
                            (Some (sortableTests.Bools newTests))
