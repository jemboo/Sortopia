namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

/// Register independent results functions by their typed catalog names.
module ResultsFunctionCatalog =
    let functions : Map<string<resultsFunctionName>, functionDefinition<resultsFunction>> =
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

    let require name =
        functions |> Map.tryFind name
        |> Option.defaultWith (fun () -> invalidArg "resultsFunctionName" $"Unknown results function '{name}'. Choose a registered catalog function on the module.")

    let outputs name =
        (require name).DataTypes |> Map.toList |> List.map (fun (dataType, keys) ->
            { DataType = dataType; QueryParameterKeys = keys } : resultsDataTypeConfig)
