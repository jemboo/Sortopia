namespace Sortopia.Evaluation

open System
open System.Buffers
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

module CeBlockOpsInt =


    let eval (sits: sortableIntTests)
             (prefix: ceBlock)
             (ceBlk: ceBlock) =
            CeBlock.validateWidth sits.ChannelCount prefix ceBlk
            let ceUseCounts = ceUseCounts.Create ceBlk.CeLength
            let mutable unsortedCount = 0
            let ces = ceBlk.CeArray
            let sw = sits.ChannelCount
            let pool = ArrayPool<int>.Shared

            for sia in sits.SortableIntArrays do
                let workArray = pool.Rent(%sw)
                try
                    Array.blit sia.Values 0 workArray 0 %sw

                    for i = 0 to ces.Length - 1 do
                        let ce = ces.[i]
                        let a = workArray.[(int ce.LowChannel)]
                        let b = workArray.[(int ce.HighChannel)]
                        if a > b then
                            workArray.[(int ce.LowChannel)] <- b
                            workArray.[(int ce.HighChannel)] <- a
                            ceUseCounts.Increment (i |> UMX.tag<ceIndex>)

                    let isSorted =
                        let mutable ok = true
                        let mutable j = 0
                        while j < %sw - 1 && ok do
                            if workArray.[j] > workArray.[j+1] then ok <- false
                            else j <- j + 1
                        ok

                    if not isSorted then
                        unsortedCount <- unsortedCount + 1

                finally
                    pool.Return(workArray)

            ceBlockEval.create prefix ceBlk ceUseCounts (unsortedCount |> UMX.tag<sortableCount>) None


    let evalAndCollectNewSortableTests (sits: sortableIntTests)
                (prefix: ceBlock) (ceBlk: ceBlock) =
            CeBlock.validateWidth sits.ChannelCount prefix ceBlk
            let ceUseCounts = ceUseCounts.Create ceBlk.CeLength
            let ces = ceBlk.CeArray
            let sw = sits.ChannelCount
            let pool = ArrayPool<int>.Shared
            let results = HashSet<sortableIntArray>(SortableIntArray.SortableIntArrayValueComparer())

            for sia in sits.SortableIntArrays do
                let workArray = pool.Rent(%sw)
                try
                    Array.blit sia.Values 0 workArray 0 %sw

                    for i = 0 to ces.Length - 1 do
                        let ce = ces.[i]
                        let a = workArray.[(int ce.LowChannel)]
                        let b = workArray.[(int ce.HighChannel)]
                        if a > b then
                            workArray.[(int ce.LowChannel)] <- b
                            workArray.[(int ce.HighChannel)] <- a
                            ceUseCounts.Increment (i |> UMX.tag<ceIndex>)

                    let isSorted =
                        let mutable ok = true
                        let mutable j = 0
                        while j < %sw - 1 && ok do
                            if workArray.[j] > workArray.[j+1] then ok <- false
                            else j <- j + 1
                        ok

                    if not isSorted then
                        let finalValues = Array.zeroCreate %sw
                        Array.blit workArray 0 finalValues 0 %sw
                        let resultSia = sortableIntArray.create(finalValues, sw, sia.SymbolSetSize)
                        results.Add(resultSia) |> ignore

                finally
                    pool.Return(workArray)

            let newTests = Seq.toArray results |> sortableIntTests.create (Guid.NewGuid() |> UMX.tag) sw
            ceBlockEval.create
                        prefix
                        ceBlk
                        ceUseCounts
                        (results.Count |> UMX.tag<sortableCount>)
                        (Some (sortableTests.Ints newTests))
