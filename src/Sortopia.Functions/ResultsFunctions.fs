namespace Sortopia.Functions

open System
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Catalog
open Sortopia.Sorting.Networks
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
    let private evaluationOptions (host: runHost) =
        ParallelOptions(MaxDegreeOfParallelism = host.EvaluationParallelism, CancellationToken = host.CancellationToken)

    let private generator version dataType keys make =
        FunctionDefinition.results version keys [dataType, InputGenerators.queryKeys keys]
            (fun host -> host.Database.Save(host.Outputs.Output(dataType, []), fun () -> make host.Parameters))
        |> FunctionDefinition.withParameterSupport ParameterDefaults.support

    let mergeSortableTests =
        generator "1" testsType InputGenerators.mergeKeys InputGenerators.mergeTests
        |> FunctionDefinition.describe "Generate sortable tests for a merge library entry, saving the filtered tests in the results database."

    let prefixSortableTests =
        generator "1" testsType InputGenerators.prefixKeys InputGenerators.prefixTests
        |> FunctionDefinition.describe "Generate sortable tests for a prefix library entry, saving the filtered tests in the results database."

    let standardSortableTests =
        generator "1" testsType InputGenerators.standardKeys InputGenerators.standardTests
        |> FunctionDefinition.describe "Generate the full set of binary sortable tests for ChannelCount in the selected data format. Save the tests in the results database."

    let private models (host: runHost) =
        match host.Database.Load(host.Database.Find modelsType) with
        | sortopiaStorageData.SorterModelSet value -> value
        | _ -> invalidOp "Expected a saved SorterModelSet in the setup database."
    let private tests (host: runHost) =
        match host.Database.Load(host.Database.Find testsType) with
        | sortopiaStorageData.SortableTests value -> value
        | _ -> invalidOp "Expected saved SortableTests in the setup database."
    let private saveEvaluation (host: runHost) (make: unit -> sortopiaStorageData) =
        let inputs = [host.Database.Find modelsType; host.Database.Find testsType]
        host.Database.Save(host.Outputs.Output(evalType, inputs), make)

    let private evaluationSorters (models: sorterModelSet) =
        SorterModelSet.makeSorterSet (Guid.NewGuid() |> UMX.tag) None models

    let private evaluate : resultsFunction = fun host ->
        saveEvaluation host (fun () ->
            let parameters = host.Parameters
            let models = models host
            let tests = tests host
            let channels = SortableTests.getChannelCount tests
            let evalType = required ParamKeys.sorterEvalTypeKey parameters |> SorterEvalType.fromString
            let collect = required ParamKeys.collectSortableTestsKey parameters |> UMX.tag<collectNewSortableTests>
            EvaluationInputs.validateEvaluationWidths channels models.SorterModels
            let sorters = evaluationSorters models
            let evaluated = SorterSetEval.makeSorterSetEvalWithOptions (evaluationOptions host) (Guid.NewGuid() |> UMX.tag) sorters tests evalType collect
            sortopiaStorageData.SorterSetEval evaluated)

    let evaluateSorters =
        let required = [ParamKey.describe ParamKeys.sorterEvalTypeKey; ParamKey.describe ParamKeys.collectSortableTestsKey]
        FunctionDefinition.resultsWithInputs "4" required [evalType, [modelsType; testsType]]
            [evalType, InputGenerators.queryKeys required] evaluate
        |> FunctionDefinition.describe "Evaluate the sorter models including each model's prefix against the tests already saved in the setup database. Derive the channel count from SortableTests and require every sorter model to have that same channel count. Save the evaluations in the results database; optionally collect new sortable tests within the evaluations."
        |> FunctionDefinition.withParameterSupport ParameterDefaults.support

    // GeneSort SorterMutateExecution._evaluateMutants: evaluate prepared models
    // in chunks, without collecting additional tests or mutating the setup data.
    let evaluateMutants =
        FunctionDefinition.resultsWithInputs "3" [ParamKey.describe ParamKeys.sorterEvalTypeKey] [evalType, [modelsType; testsType]]
            [evalType, [ParamKeys.sorterEvalTypeKey.Name]]
            (fun host -> saveEvaluation host (fun () ->
                let parameters = host.Parameters
                let models = models host
                let tests = tests host
                let channels = SortableTests.getChannelCount tests
                let evalType = required ParamKeys.sorterEvalTypeKey parameters |> SorterEvalType.fromString
                let evaluations = models.SorterModels |> Array.chunkBySize 1000 |> Array.collect (fun chunk ->
                    host.CancellationToken.ThrowIfCancellationRequested()
                    EvaluationInputs.validateEvaluationWidths channels chunk
                    let group = sorterModelSet.create (Guid.NewGuid() |> UMX.tag) chunk models.RawCeLength
                    let sorters = evaluationSorters group
                    SorterSetEval.makeSorterEvalsWithOptions (evaluationOptions host) sorters.Sorters tests evalType (UMX.tag false))
                sorterSetEval.create (Guid.NewGuid() |> UMX.tag) (Guid.NewGuid() |> UMX.tag) (SortableTests.getId tests) evaluations |> sortopiaStorageData.SorterSetEval))
        |> FunctionDefinition.describe "Evaluate the prepared sorter models against setup tests in batches of 1,000, saving evaluations in the results database. Does not generate mutations or collect new sortable tests."
        |> FunctionDefinition.withParameterSupport ParameterDefaults.support
