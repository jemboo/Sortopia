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
            ResultsFunctionNames.prefixSorterModels, ResultsFunctions.prefixSorterModels
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

    let requireResults name =
        resultsFunctionCatalog |> Map.tryFind name
        |> Option.defaultWith (fun () -> invalidArg "resultsFunctionName" $"Unknown results function '{name}'. Choose a registered catalog function on the module.")
    let requireSetup name =
        setupFunctionCatalog |> Map.tryFind name
        |> Option.defaultWith (fun () -> invalidArg "setupFunctionName" $"Unknown setup function '{name}'. Choose a registered catalog function on the module.")
    let resultsOutputs name =
        (requireResults name).DataTypes |> Map.toList |> List.map (fun (dataType, keys) ->
            { DataType = dataType; QueryParameterKeys = keys } : resultsDataTypeConfig)
    let setupOutputs name = (requireSetup name).DataTypes |> Map.toList
