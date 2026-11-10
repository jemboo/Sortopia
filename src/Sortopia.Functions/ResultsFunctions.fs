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
    let mergeSortableTests =
        FunctionDefinition.results [sortopiaDataType.SortableTests "", InputGenerators.mergeKeys]
            (fun parameters _ -> InputGenerators.attempt (fun () -> [InputGenerators.mergeTests parameters]))

    let prefixSortableTests =
        FunctionDefinition.results [sortopiaDataType.SortableTests "", InputGenerators.prefixKeys]
            (fun parameters _ -> InputGenerators.attempt (fun () -> [InputGenerators.prefixTests parameters]))

    let standardSortableTests =
        FunctionDefinition.results [sortopiaDataType.SortableTests "", InputGenerators.standardKeys]
            (fun parameters _ -> InputGenerators.attempt (fun () -> [InputGenerators.standardTests parameters]))

    let uniformSorterModels =
        FunctionDefinition.results [sortopiaDataType.SorterModelSet "", InputGenerators.modelKeys]
            (fun parameters _ -> InputGenerators.attempt (fun () -> [InputGenerators.uniformModels parameters]))

    let standardEvaluationInputs =
        FunctionDefinition.results
            [sortopiaDataType.SortableTests "", InputGenerators.standardKeys; sortopiaDataType.SorterModelSet "", InputGenerators.modelKeys]
            (fun parameters _ -> InputGenerators.attempt (fun () -> [InputGenerators.uniformModels parameters; InputGenerators.standardTests parameters]))

    let private evaluate : resultsFunction = fun parameters inputs ->
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
            if models.SorterModels |> Array.exists (fun model -> SorterModel.getChannelCount model <> channels) then
                invalidArg "inputs" "The models and tests must have the same channel count."
            let prefix = ceBlock.create (Guid.Empty |> UMX.tag) channels ces
            let sorters = SorterModelSet.makeSorterSetWithPrefix (Guid.NewGuid() |> UMX.tag) None models
            let evaluated = SorterSetEval.makeSorterSetEval (Guid.NewGuid() |> UMX.tag) sorters prefix tests evalType collect
            Ok [functionData.SorterSetEval evaluated]
        with error -> Error error.Message

    let evaluateSorters =
        FunctionDefinition.resultsWithInputs [sortopiaDataType.SorterModelSet ""; sortopiaDataType.SortableTests ""]
            [ sortopiaDataType.SorterSetEval "", [ParamKey.describe ParamKeys.channelCountKey; ParamKey.describe ParamKeys.sorterEvalTypeKey;
              ParamKey.describe ParamKeys.collectSortableTestsKey] ] evaluate

    // GeneSort SorterMutateExecution._evaluateMutants: evaluate prepared models
    // in chunks, without collecting additional tests or mutating the setup data.
    let evaluateMutants =
        FunctionDefinition.resultsWithInputs [sortopiaDataType.SorterModelSet ""; sortopiaDataType.SortableTests ""]
            [sortopiaDataType.SorterSetEval "", [ParamKey.describe ParamKeys.sorterEvalTypeKey]]
            (fun parameters inputs -> InputGenerators.attempt (fun () ->
                let models = inputs |> List.choose (function functionData.SorterModelSet value -> Some value | _ -> None) |> List.exactlyOne
                let tests, ces = inputs |> List.choose (function functionData.SortableTests (tests, ces) -> Some (tests, ces) | _ -> None) |> List.exactlyOne
                let channels = SortableTests.getChannelCount tests
                let prefix = ceBlock.create (Guid.Empty |> UMX.tag) channels ces
                let evalType = required ParamKeys.sorterEvalTypeKey parameters |> SorterEvalType.fromString
                let evaluations = models.SorterModels |> Array.chunkBySize 1000 |> Array.collect (fun chunk ->
                    if chunk |> Array.exists (fun model -> SorterModel.getChannelCount model <> channels) then
                        invalidArg "inputs" "The models and tests must have the same channel count."
                    let group = sorterModelSet.create (Guid.NewGuid() |> UMX.tag) chunk models.RawCeLength
                    let sorters = SorterModelSet.makeSorterSetWithPrefix (Guid.NewGuid() |> UMX.tag) None group
                    SorterSetEval.makeSorterEvals sorters.Sorters prefix tests evalType (UMX.tag false))
                [sorterSetEval.create (Guid.NewGuid() |> UMX.tag) (Guid.NewGuid() |> UMX.tag) (SortableTests.getId tests) evaluations |> functionData.SorterSetEval]))
