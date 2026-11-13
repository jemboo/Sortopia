namespace Sortopia.Evaluation

open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks


type sorterSetEval =

    private {
        sorterSetEvalId: Guid<sorterSetEvalId>
        sorterSetId: Guid<sorterSetId>
        sorterTestId: Guid<sortableTestsId>
        sorterEvals: Map<Guid<sorterId>, sorterEval>
    }

    static member create
                (sorterSetEvalId: Guid<sorterSetEvalId>)
                (sorterSetId: Guid<sorterSetId>)
                (sorterTestsId: Guid<sortableTestsId>)
                (sorterEvals: sorterEval[]) =
        if isNull sorterEvals then nullArg "sorterEvals"
        let ids = sorterEvals |> Array.map SorterEval.getSorterId
        if (Array.distinct ids).Length <> ids.Length then invalidArg "sorterEvals" "Duplicate sorter IDs are not allowed."
        {
            sorterSetEvalId = sorterSetEvalId
            sorterSetId = sorterSetId
            sorterTestId = sorterTestsId
            sorterEvals = sorterEvals |> Array.map (fun se -> (se |> SorterEval.getSorterId, se)) |> Map.ofArray
        }

    member this.SorterSetEvalId with get() : Guid<sorterSetEvalId> = this.sorterSetEvalId
    member this.SorterSetId with get() : Guid<sorterSetId> = this.sorterSetId
    member this.SorterTestId with get() : Guid<sortableTestsId> = this.sorterTestId
    member this.SorterEvals with get() : sorterEval[] = this.sorterEvals.Values |> Seq.toArray


module SorterSetEval =

    let makeSorterEvalsWithOptions
                (options: ParallelOptions)
                (sorters: sorter array)
                (prefix: ceBlock)
                (sortableTests: sortableTests)
                (sorterEvalType:sorterEvalType)
                (collectNewSortableTests: bool<collectNewSortableTests>) :sorterEval array =
        let ceBlocks =
                sorters
                |> Array.map (fun sorter ->
                        ceBlock.create
                            (%sorter.SorterId |> UMX.tag<ceBlockId>)
                            sorter.ChannelCount sorter.Ces )

        let ceBlockEvals : ceBlockEval array =
                CeBlockOps.evalWithSorterTestsWithOptions options
                        sortableTests
                        prefix
                        ceBlocks
                        %collectNewSortableTests

        Array.zip sorters ceBlockEvals
        |> Array.map (
                fun (sorter, ceBlockEval ) ->
                        SorterEval.create
                            sorterEvalType
                            sorter.SorterId
                            ceBlockEval
                )


    let makeSorterSetEvalWithOptions
            (options: ParallelOptions)
            (sorterSetEvalId: Guid<sorterSetEvalId>)
            (sorterSet: sorterSet)
            (prefix: ceBlock)
            (sortableTests: sortableTests)
            (sorterEvalType:sorterEvalType)
            (collectNewSortableTests: bool<collectNewSortableTests>) : sorterSetEval =

        let sorterEvals = makeSorterEvalsWithOptions options
                            sorterSet.Sorters
                            prefix
                            sortableTests
                            sorterEvalType
                            collectNewSortableTests

        sorterSetEval.create
                    sorterSetEvalId
                    sorterSet.Id
                    (sortableTests |> SortableTests.getId )
                    sorterEvals


    let makeSorterEvals sorters prefix tests evalType collect =
        makeSorterEvalsWithOptions (ParallelOptions()) sorters prefix tests evalType collect

    let makeSorterSetEval id sorters prefix tests evalType collect =
        makeSorterSetEvalWithOptions (ParallelOptions()) id sorters prefix tests evalType collect

    let makeFullDataTableRecords (source: sorterSetEval) : Sortopia.Reporting.dataTableRecord seq =
        source.SorterEvals
        |> Seq.map (SorterEval.toDataTableRecord)
