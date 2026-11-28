namespace Sortopia.Evaluation

open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting


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

    let private evaluateSorters
                (options: ParallelOptions)
                (sorters: sorter array)
                (sortableTests: sortableTests)
                (collectNewSortableTests: bool<collectNewSortableTests>) =
        let ceBlocks =
                sorters
                |> Array.map (fun sorter ->
                        ceBlock.create
                            (%sorter.SorterId |> UMX.tag<ceBlockId>)
                            sorter.ChannelCount sorter.Ces )

        CeBlockOps.evalWithSorterTestsWithOptions options
                        sortableTests
                        ceBlocks
                        %collectNewSortableTests

    let makeSorterEvalsWithOptions options sorters sortableTests sorterEvalType collectNewSortableTests =
        let ceBlockEvals = evaluateSorters options sorters sortableTests collectNewSortableTests
        Array.zip sorters ceBlockEvals
        |> Array.map (
                fun (sorter, ceBlockEval ) ->
                        SorterEval.create
                            sorterEvalType
                            sorter.SorterId
                            ceBlockEval
                )


    /// Evaluate only the suffix against prepared inputs. Retain the already-applied
    /// model prefix with -1 usage markers; the CE limit applies to suffixes only.
    let makeSorterModelEvalsWithOptions options (models: sorterModel array) maxCeCount tests evalType collect =
        let sorters = models |> EvaluationParallel.map options (fun model ->
            let complete = SorterModel.makeSorter model maxCeCount
            sorter.create complete.SorterId complete.ChannelCount (complete.Ces |> Array.skip (SorterModel.getPrefix model).Length))
        if evalType = sorterEvalType.V1 then
            let blocks = evaluateSorters options sorters tests collect
            Array.mapi (fun index model ->
                SorterEval.createV1WithPrefix sorters[index].SorterId (SorterModel.getPrefix model) blocks[index]) models
        else
            let evaluations = makeSorterEvalsWithOptions options sorters tests evalType collect
            Array.map2 SorterEval.withModelPrefix models evaluations

    let makeSorterSetEvalWithOptions
            (options: ParallelOptions)
            (sorterSetEvalId: Guid<sorterSetEvalId>)
            (sorterSet: sorterSet)
            (sortableTests: sortableTests)
            (sorterEvalType:sorterEvalType)
            (collectNewSortableTests: bool<collectNewSortableTests>) : sorterSetEval =

        let sorterEvals = makeSorterEvalsWithOptions options
                            sorterSet.Sorters
                            sortableTests
                            sorterEvalType
                            collectNewSortableTests

        sorterSetEval.create
                    sorterSetEvalId
                    sorterSet.Id
                    (sortableTests |> SortableTests.getId )
                    sorterEvals


    let makeSorterEvals sorters tests evalType collect =
        makeSorterEvalsWithOptions (ParallelOptions()) sorters tests evalType collect

    let makeSorterSetEval id sorters tests evalType collect =
        makeSorterSetEvalWithOptions (ParallelOptions()) id sorters tests evalType collect

    let makeFullDataTableRecords (source: sorterSetEval) : Sortopia.Reporting.dataTableRecord seq =
        source.SorterEvals
        |> Seq.map (SorterEval.toDataTableRecord)
