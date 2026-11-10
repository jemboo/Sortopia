namespace Sortopia.Functions

open System
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Catalog
open Sortopia.Models.Inputs
open Sortopia.Models.Sorting
open Sortopia.Evaluation
open FunctionParameters

// Adapted from GeneSort.Project.V1/Catalogs/Execution:
// SortableTests/SortableTestsExecution._runSortableTests and SorterEval/SorterEvalExecution._makeSorterEvals.
// Copyright (c) 2025 Tom Patterson. See GENESORT-LICENSE.txt.
module ResultsFunctions =
    let makeSortableTests : resultsFunction = fun parameters _ ->
        try
            let format = required ParamKeys.sortableDataFormatKey parameters |> SortableDataFormat.fromString
            let model, prefix =
                match parameters.TryFind ParamKeys.mergeLibIdKey, parameters.TryFind ParamKeys.prefixLibIdKey with
                | Some library, None ->
                    let merge = msasM.create library.ChannelCount library.MergeDimension mergeSuffixType.NoSuffix library.SorterLibVariant
                    let prefix = merge.CeArray |> Option.defaultWith (fun () -> invalidArg "parameters" "The merge prefix is unavailable.")
                    sortableTestsModel.MsasMi merge, prefix
                | None, Some library ->
                    let prefix = SorterDataParse.getCeArrayFromPrefixLib library |> Option.defaultWith (fun () -> invalidArg "parameters" "The prefix is unavailable.")
                    sortableTestsModel.MsasPfx (msasPfx.create library), prefix
                | _ -> invalidArg "parameters" "Specify exactly one MergeLibId or PrefixLibId."
            let tests = SortableTestsModel.makeSortableTests (Guid.NewGuid() |> UMX.tag) model format
            Ok [functionData.SortableTests (tests, prefix)]
        with error -> Error error.Message

    let evaluateSorters : resultsFunction = fun parameters inputs ->
        try
            let exactlyOne name values =
                match values with
                | [value] -> value
                | _ -> invalidArg "inputs" $"Expected exactly one {name} input; received {List.length values}."
            let models = inputs |> List.choose (function functionData.SorterModelSet value -> Some value | _ -> None) |> exactlyOne "SorterModelSet"
            let tests, ces = inputs |> List.choose (function functionData.SortableTests (tests, ces) -> Some (tests, ces) | _ -> None) |> exactlyOne "SortableTests"
            let channels = required ParamKeys.channelCountKey parameters |> UMX.tag<channelCount>
            let evalType = required ParamKeys.sorterEvalTypeKey parameters |> SorterEvalType.fromString
            let collect = required ParamKeys.collectSortableTestsKey parameters |> UMX.tag<collectNewSortableTests>
            if SortableTests.getChannelCount tests <> channels then invalidArg "inputs" "The tests and parameters must have the same ChannelCount."
            let prefix = ceBlock.create (Guid.Empty |> UMX.tag) channels ces
            let sorters = SorterModelSet.makeSorterSetWithPrefix (Guid.NewGuid() |> UMX.tag) None models
            let evaluated = SorterSetEval.makeSorterSetEval (Guid.NewGuid() |> UMX.tag) sorters prefix tests evalType collect
            Ok [functionData.SorterSetEval evaluated]
        with error -> Error error.Message
