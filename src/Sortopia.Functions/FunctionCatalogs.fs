namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

/// Register an independent function by adding its typed name and function here.
module FunctionCatalogs =
    let resultsFunctionCatalog : Map<string<resultsFunctionName>, functionDefinition<resultsFunction>> =
        Map.ofList [
            ResultsFunctionNames.mergeSortableTests, ResultsFunctions.mergeSortableTests
            ResultsFunctionNames.prefixSortableTests, ResultsFunctions.prefixSortableTests
            ResultsFunctionNames.sorterEval, ResultsFunctions.evaluateSorters
            ResultsFunctionNames.standardSortableTests, ResultsFunctions.standardSortableTests
            ResultsFunctionNames.uniformSorterModels, ResultsFunctions.uniformSorterModels
            ResultsFunctionNames.standardEvaluationInputs, ResultsFunctions.standardEvaluationInputs
            ResultsFunctionNames.mutantEval, ResultsFunctions.evaluateMutants
        ]

    let setupFunctionCatalog : Map<string<setupFunctionName>, functionDefinition<setupFunction>> =
        Map.ofList [
            SetupFunctionNames.standardTests, SetupFunctions.standardTests
            SetupFunctionNames.uniformSorterModels, SetupFunctions.uniformSorterModels
            SetupFunctionNames.evaluationStandard, SetupFunctions.evaluationStandard
            SetupFunctionNames.evaluationMerge, SetupFunctions.evaluationMerge
            SetupFunctionNames.evaluationPrefix, SetupFunctions.evaluationPrefix
        ]

    // Bump when a function's output semantics change. Names remain simple map keys.
    let resultsFunctionVersions = resultsFunctionCatalog |> Map.map (fun _ _ -> "1")
    let setupFunctionVersions = setupFunctionCatalog |> Map.map (fun _ _ -> "1")

    let resultsOutputs name =
        resultsFunctionCatalog |> Map.tryFind name |> Option.map (fun definition ->
            definition.DataTypes |> Map.toList |> List.map (fun (dataType, keys) ->
                { DataType = dataType; QueryParameterKeys = keys |> List.map (fun key -> key.Name) } : resultsDataTypeConfig))

    let setupOutputs name =
        setupFunctionCatalog |> Map.tryFind name |> Option.map (fun definition ->
            definition.DataTypes |> Map.toList |> List.map (fun (dataType, keys) ->
                dataType, keys |> List.map (fun key -> key.Name)))
