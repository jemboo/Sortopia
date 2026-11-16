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
            (fun host -> host.ResultsDb.SaveIfMissing(dataType, [], fun () -> make host.Parameters))

    let mergeSortableTests =
        generator "1" testsType InputGenerators.mergeKeys InputGenerators.mergeTests
        |> FunctionDefinition.describe "Generate sortable tests for a merge library entry, saving the tests and their generating prefix in the results database."

    let prefixSortableTests =
        generator "1" testsType InputGenerators.prefixKeys InputGenerators.prefixTests
        |> FunctionDefinition.describe "Generate sortable tests for a prefix library entry, saving the tests and their generating prefix in the results database."

    let standardSortableTests =
        generator "1" testsType InputGenerators.standardKeys InputGenerators.standardTests
        |> FunctionDefinition.describe "Generate the full set of binary sortable tests for ChannelCount in the selected data format, with an empty prefix. Save the tests in the results database."

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

    let private evaluationSorters (prefix: ce array) (models: sorterModelSet) =
        if Array.isEmpty prefix then
            SorterModelSet.makeSorterSetWithPrefix (Guid.NewGuid() |> UMX.tag) None models
        else
            // Tests have already passed through this prefix. Empty model prefixes
            // describe suffix-only models; a different nonempty prefix is incompatible.
            EvaluationInputs.validateEvaluationPrefixes prefix models
            SorterModelSet.makeSorterSetWithoutPrefix (Guid.NewGuid() |> UMX.tag) None models

    let private evaluate : resultsFunction = fun host ->
        saveEvaluation host (fun () ->
            let parameters = host.Parameters
            let models = models host
            let tests, ces = tests host
            let channels = SortableTests.getChannelCount tests
            let evalType = required ParamKeys.sorterEvalTypeKey parameters |> SorterEvalType.fromString
            let collect = required ParamKeys.collectSortableTestsKey parameters |> UMX.tag<collectNewSortableTests>
            EvaluationInputs.validateEvaluationWidths channels models.SorterModels
            let prefix = ceBlock.create (Guid.Empty |> UMX.tag) channels ces
            let sorters = evaluationSorters ces models
            let evaluated = SorterSetEval.makeSorterSetEvalWithOptions (evaluationOptions host) (Guid.NewGuid() |> UMX.tag) sorters prefix tests evalType collect
            functionData.SorterSetEval evaluated)

    let evaluateSorters =
        let required = [ParamKey.describe ParamKeys.sorterEvalTypeKey; ParamKey.describe ParamKeys.collectSortableTestsKey]
        FunctionDefinition.resultsWithInputs "3" required [evalType, [modelsType; testsType]]
            [evalType, InputGenerators.queryKeys required] evaluate
        |> FunctionDefinition.describe "Evaluate the sorter models against the tests and prefix already saved in the setup database. Derive the channel count from SortableTests and require every sorter model to have that same channel count. Save the evaluations in the results database; optionally collect new sortable tests within the evaluations."

    // GeneSort SorterMutateExecution._evaluateMutants: evaluate prepared models
    // in chunks, without collecting additional tests or mutating the setup data.
    let evaluateMutants =
        FunctionDefinition.resultsWithInputs "2" [ParamKey.describe ParamKeys.sorterEvalTypeKey] [evalType, [modelsType; testsType]]
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
                    EvaluationInputs.validateEvaluationWidths channels chunk
                    let group = sorterModelSet.create (Guid.NewGuid() |> UMX.tag) chunk models.RawCeLength
                    let sorters = evaluationSorters ces group
                    SorterSetEval.makeSorterEvalsWithOptions (evaluationOptions host) sorters.Sorters prefix tests evalType (UMX.tag false))
                sorterSetEval.create (Guid.NewGuid() |> UMX.tag) (Guid.NewGuid() |> UMX.tag) (SortableTests.getId tests) evaluations |> functionData.SorterSetEval))
        |> FunctionDefinition.describe "Evaluate the prepared sorter models against setup tests in batches of 1,000, saving evaluations in the results database. Does not generate mutations or collect new sortable tests."
