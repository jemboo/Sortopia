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
            ResultsFunctionNames.uniformMsceSorterModels, ResultsFunctions.uniformMsceSorterModels
            ResultsFunctionNames.uniformMssiSorterModels, ResultsFunctions.uniformMssiSorterModels
            ResultsFunctionNames.uniformMsrsSorterModels, ResultsFunctions.uniformMsrsSorterModels
            ResultsFunctionNames.uniformMsuf4SorterModels, ResultsFunctions.uniformMsuf4SorterModels
            ResultsFunctionNames.uniformMsuf6SorterModels, ResultsFunctions.uniformMsuf6SorterModels
            ResultsFunctionNames.prefixMsceSorterModels, ResultsFunctions.prefixMsceSorterModels
            ResultsFunctionNames.prefixMssiSorterModels, ResultsFunctions.prefixMssiSorterModels
            ResultsFunctionNames.prefixMsrsSorterModels, ResultsFunctions.prefixMsrsSorterModels
            ResultsFunctionNames.prefixMsuf4SorterModels, ResultsFunctions.prefixMsuf4SorterModels
            ResultsFunctionNames.prefixMsuf6SorterModels, ResultsFunctions.prefixMsuf6SorterModels
            ResultsFunctionNames.standardMsceEvaluationInputs, ResultsFunctions.standardMsceEvaluationInputs
            ResultsFunctionNames.standardMssiEvaluationInputs, ResultsFunctions.standardMssiEvaluationInputs
            ResultsFunctionNames.standardMsrsEvaluationInputs, ResultsFunctions.standardMsrsEvaluationInputs
            ResultsFunctionNames.standardMsuf4EvaluationInputs, ResultsFunctions.standardMsuf4EvaluationInputs
            ResultsFunctionNames.standardMsuf6EvaluationInputs, ResultsFunctions.standardMsuf6EvaluationInputs
            ResultsFunctionNames.mutantEval, ResultsFunctions.evaluateMutants
        ]

    let setupFunctionCatalog : Map<string<setupFunctionName>, functionDefinition<setupFunction>> =
        Map.ofList [
            SetupFunctionNames.standardTests, SetupFunctions.standardTests
            SetupFunctionNames.uniformMsceSorterModels, SetupFunctions.uniformMsceSorterModels
            SetupFunctionNames.uniformMssiSorterModels, SetupFunctions.uniformMssiSorterModels
            SetupFunctionNames.uniformMsrsSorterModels, SetupFunctions.uniformMsrsSorterModels
            SetupFunctionNames.uniformMsuf4SorterModels, SetupFunctions.uniformMsuf4SorterModels
            SetupFunctionNames.uniformMsuf6SorterModels, SetupFunctions.uniformMsuf6SorterModels
            SetupFunctionNames.evaluationStandardMsce, SetupFunctions.evaluationStandardMsce
            SetupFunctionNames.evaluationStandardMssi, SetupFunctions.evaluationStandardMssi
            SetupFunctionNames.evaluationStandardMsrs, SetupFunctions.evaluationStandardMsrs
            SetupFunctionNames.evaluationStandardMsuf4, SetupFunctions.evaluationStandardMsuf4
            SetupFunctionNames.evaluationStandardMsuf6, SetupFunctions.evaluationStandardMsuf6
            SetupFunctionNames.evaluationMergeMsce, SetupFunctions.evaluationMergeMsce
            SetupFunctionNames.evaluationMergeMssi, SetupFunctions.evaluationMergeMssi
            SetupFunctionNames.evaluationMergeMsrs, SetupFunctions.evaluationMergeMsrs
            SetupFunctionNames.evaluationMergeMsuf4, SetupFunctions.evaluationMergeMsuf4
            SetupFunctionNames.evaluationMergeMsuf6, SetupFunctions.evaluationMergeMsuf6
            SetupFunctionNames.evaluationPrefixMsce, SetupFunctions.evaluationPrefixMsce
            SetupFunctionNames.evaluationPrefixMssi, SetupFunctions.evaluationPrefixMssi
            SetupFunctionNames.evaluationPrefixMsrs, SetupFunctions.evaluationPrefixMsrs
            SetupFunctionNames.evaluationPrefixMsuf4, SetupFunctions.evaluationPrefixMsuf4
            SetupFunctionNames.evaluationPrefixMsuf6, SetupFunctions.evaluationPrefixMsuf6
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
