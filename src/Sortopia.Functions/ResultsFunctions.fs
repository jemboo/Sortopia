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
    let private testsType = sortopiaDataType.SortableTests ""
    let private modelsType = sortopiaDataType.SorterModelSet ""
    let private evalType = sortopiaDataType.SorterSetEval ""

    let private generator version dataType keys make =
        FunctionDefinition.results version keys [dataType, InputGenerators.queryKeys keys]
            (fun host -> host.ResultsDb.SaveIfMissing(dataType, [], fun () -> make host.Parameters))

    let mergeSortableTests =
        generator "1" testsType InputGenerators.mergeKeys InputGenerators.mergeTests

    let prefixSortableTests =
        generator "1" testsType InputGenerators.prefixKeys InputGenerators.prefixTests

    let standardSortableTests =
        generator "1" testsType InputGenerators.standardKeys InputGenerators.standardTests

    let uniformSorterModels =
        generator "1" modelsType InputGenerators.modelKeys InputGenerators.uniformModels

    let standardEvaluationInputs =
        FunctionDefinition.results "1"
            (InputGenerators.standardKeys @ InputGenerators.modelKeys |> List.distinctBy (fun key -> key.Name))
            [testsType, InputGenerators.queryKeys InputGenerators.standardKeys; modelsType, InputGenerators.queryKeys InputGenerators.modelKeys]
            (fun host ->
                host.ResultsDb.SaveIfMissing(modelsType, [], fun () -> InputGenerators.uniformModels host.Parameters)
                host.ResultsDb.SaveIfMissing(testsType, [], fun () -> InputGenerators.standardTests host.Parameters))

    let private models (host: runHost) =
        match host.SetupDb.Load modelsType with
        | functionData.SorterModelSet value -> value
        | _ -> invalidOp "Expected a saved SorterModelSet in the setup database."
    let private tests (host: runHost) =
        match host.SetupDb.Load testsType with
        | functionData.SortableTests (value, prefix) -> value, prefix
        | _ -> invalidOp "Expected saved SortableTests in the setup database."
    let private saveEvaluation (host: runHost) make =
        let inputs = [host.SetupDb.Reference modelsType; host.SetupDb.Reference testsType]
        host.ResultsDb.SaveIfMissing(evalType, inputs, make)

    let private evaluate : resultsFunction = fun host ->
        saveEvaluation host (fun () ->
            let parameters = host.Parameters
            let models = models host
            let tests, ces = tests host
            let channels = required ParamKeys.channelCountKey parameters |> UMX.tag<channelCount>
            let evalType = required ParamKeys.sorterEvalTypeKey parameters |> SorterEvalType.fromString
            let collect = required ParamKeys.collectSortableTestsKey parameters |> UMX.tag<collectNewSortableTests>
            if SortableTests.getChannelCount tests <> channels then invalidArg "inputs" "The tests and parameters must have the same ChannelCount."
            if models.SorterModels |> Array.exists (fun model -> SorterModel.getChannelCount model <> channels) then
                invalidArg "inputs" "The models and tests must have the same channel count."
            let prefix = ceBlock.create (Guid.Empty |> UMX.tag) channels ces
            let sorters = SorterModelSet.makeSorterSetWithPrefix (Guid.NewGuid() |> UMX.tag) None models
            let evaluated = SorterSetEval.makeSorterSetEval (Guid.NewGuid() |> UMX.tag) sorters prefix tests evalType collect
            functionData.SorterSetEval evaluated)

    let evaluateSorters =
        let required = [ParamKey.describe ParamKeys.channelCountKey; ParamKey.describe ParamKeys.sorterEvalTypeKey; ParamKey.describe ParamKeys.collectSortableTestsKey]
        FunctionDefinition.resultsWithInputs "1" required [evalType, [modelsType; testsType]]
            [evalType, InputGenerators.queryKeys required] evaluate

    // GeneSort SorterMutateExecution._evaluateMutants: evaluate prepared models
    // in chunks, without collecting additional tests or mutating the setup data.
    let evaluateMutants =
        FunctionDefinition.resultsWithInputs "1" [ParamKey.describe ParamKeys.sorterEvalTypeKey] [evalType, [modelsType; testsType]]
            [evalType, [ParamKeys.sorterEvalTypeKey.Name]]
            (fun host -> saveEvaluation host (fun () ->
                let parameters = host.Parameters
                let models = models host
                let tests, ces = tests host
                let channels = SortableTests.getChannelCount tests
                let prefix = ceBlock.create (Guid.Empty |> UMX.tag) channels ces
                let evalType = required ParamKeys.sorterEvalTypeKey parameters |> SorterEvalType.fromString
                let evaluations = models.SorterModels |> Array.chunkBySize 1000 |> Array.collect (fun chunk ->
                    host.CancellationToken.ThrowIfCancellationRequested()
                    if chunk |> Array.exists (fun model -> SorterModel.getChannelCount model <> channels) then
                        invalidArg "inputs" "The models and tests must have the same channel count."
                    let group = sorterModelSet.create (Guid.NewGuid() |> UMX.tag) chunk models.RawCeLength
                    let sorters = SorterModelSet.makeSorterSetWithPrefix (Guid.NewGuid() |> UMX.tag) None group
                    SorterSetEval.makeSorterEvals sorters.Sorters prefix tests evalType (UMX.tag false))
                sorterSetEval.create (Guid.NewGuid() |> UMX.tag) (Guid.NewGuid() |> UMX.tag) (SortableTests.getId tests) evaluations |> functionData.SorterSetEval))
