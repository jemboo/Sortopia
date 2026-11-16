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
            ResultsFunctionNames.uniformMsceSimpleSorterModels, ResultsFunctions.uniformMsceSorterModels modelFamily.Simple
            ResultsFunctionNames.uniformMsceSimpleRSorterModels, ResultsFunctions.uniformMsceSorterModels modelFamily.SimpleR
            ResultsFunctionNames.uniformMsceSimpleSSorterModels, ResultsFunctions.uniformMsceSorterModels modelFamily.SimpleS
            ResultsFunctionNames.uniformMssiSimpleSorterModels, ResultsFunctions.uniformMssiSorterModels modelFamily.Simple
            ResultsFunctionNames.uniformMssiSimpleRSorterModels, ResultsFunctions.uniformMssiSorterModels modelFamily.SimpleR
            ResultsFunctionNames.uniformMssiSimpleSSorterModels, ResultsFunctions.uniformMssiSorterModels modelFamily.SimpleS
            ResultsFunctionNames.uniformMsrsSimpleSorterModels, ResultsFunctions.uniformMsrsSorterModels modelFamily.Simple
            ResultsFunctionNames.uniformMsrsSimpleRSorterModels, ResultsFunctions.uniformMsrsSorterModels modelFamily.SimpleR
            ResultsFunctionNames.uniformMsrsSimpleSSorterModels, ResultsFunctions.uniformMsrsSorterModels modelFamily.SimpleS
            ResultsFunctionNames.uniformMsuf4SimpleSorterModels, ResultsFunctions.uniformMsuf4SorterModels modelFamily.Simple
            ResultsFunctionNames.uniformMsuf4SimpleRSorterModels, ResultsFunctions.uniformMsuf4SorterModels modelFamily.SimpleR
            ResultsFunctionNames.uniformMsuf4SimpleSSorterModels, ResultsFunctions.uniformMsuf4SorterModels modelFamily.SimpleS
            ResultsFunctionNames.uniformMsuf6SimpleSorterModels, ResultsFunctions.uniformMsuf6SorterModels modelFamily.Simple
            ResultsFunctionNames.uniformMsuf6SimpleRSorterModels, ResultsFunctions.uniformMsuf6SorterModels modelFamily.SimpleR
            ResultsFunctionNames.uniformMsuf6SimpleSSorterModels, ResultsFunctions.uniformMsuf6SorterModels modelFamily.SimpleS
            ResultsFunctionNames.prefixMsceSimpleSorterModels, ResultsFunctions.prefixMsceSorterModels modelFamily.Simple
            ResultsFunctionNames.prefixMsceSimpleRSorterModels, ResultsFunctions.prefixMsceSorterModels modelFamily.SimpleR
            ResultsFunctionNames.prefixMsceSimpleSSorterModels, ResultsFunctions.prefixMsceSorterModels modelFamily.SimpleS
            ResultsFunctionNames.prefixMssiSimpleSorterModels, ResultsFunctions.prefixMssiSorterModels modelFamily.Simple
            ResultsFunctionNames.prefixMssiSimpleRSorterModels, ResultsFunctions.prefixMssiSorterModels modelFamily.SimpleR
            ResultsFunctionNames.prefixMssiSimpleSSorterModels, ResultsFunctions.prefixMssiSorterModels modelFamily.SimpleS
            ResultsFunctionNames.prefixMsrsSimpleSorterModels, ResultsFunctions.prefixMsrsSorterModels modelFamily.Simple
            ResultsFunctionNames.prefixMsrsSimpleRSorterModels, ResultsFunctions.prefixMsrsSorterModels modelFamily.SimpleR
            ResultsFunctionNames.prefixMsrsSimpleSSorterModels, ResultsFunctions.prefixMsrsSorterModels modelFamily.SimpleS
            ResultsFunctionNames.prefixMsuf4SimpleSorterModels, ResultsFunctions.prefixMsuf4SorterModels modelFamily.Simple
            ResultsFunctionNames.prefixMsuf4SimpleRSorterModels, ResultsFunctions.prefixMsuf4SorterModels modelFamily.SimpleR
            ResultsFunctionNames.prefixMsuf4SimpleSSorterModels, ResultsFunctions.prefixMsuf4SorterModels modelFamily.SimpleS
            ResultsFunctionNames.prefixMsuf6SimpleSorterModels, ResultsFunctions.prefixMsuf6SorterModels modelFamily.Simple
            ResultsFunctionNames.prefixMsuf6SimpleRSorterModels, ResultsFunctions.prefixMsuf6SorterModels modelFamily.SimpleR
            ResultsFunctionNames.prefixMsuf6SimpleSSorterModels, ResultsFunctions.prefixMsuf6SorterModels modelFamily.SimpleS
            ResultsFunctionNames.standardMsceSimpleEvaluationInputs, ResultsFunctions.standardMsceEvaluationInputs modelFamily.Simple
            ResultsFunctionNames.standardMsceSimpleREvaluationInputs, ResultsFunctions.standardMsceEvaluationInputs modelFamily.SimpleR
            ResultsFunctionNames.standardMsceSimpleSEvaluationInputs, ResultsFunctions.standardMsceEvaluationInputs modelFamily.SimpleS
            ResultsFunctionNames.standardMssiSimpleEvaluationInputs, ResultsFunctions.standardMssiEvaluationInputs modelFamily.Simple
            ResultsFunctionNames.standardMssiSimpleREvaluationInputs, ResultsFunctions.standardMssiEvaluationInputs modelFamily.SimpleR
            ResultsFunctionNames.standardMssiSimpleSEvaluationInputs, ResultsFunctions.standardMssiEvaluationInputs modelFamily.SimpleS
            ResultsFunctionNames.standardMsrsSimpleEvaluationInputs, ResultsFunctions.standardMsrsEvaluationInputs modelFamily.Simple
            ResultsFunctionNames.standardMsrsSimpleREvaluationInputs, ResultsFunctions.standardMsrsEvaluationInputs modelFamily.SimpleR
            ResultsFunctionNames.standardMsrsSimpleSEvaluationInputs, ResultsFunctions.standardMsrsEvaluationInputs modelFamily.SimpleS
            ResultsFunctionNames.standardMsuf4SimpleEvaluationInputs, ResultsFunctions.standardMsuf4EvaluationInputs modelFamily.Simple
            ResultsFunctionNames.standardMsuf4SimpleREvaluationInputs, ResultsFunctions.standardMsuf4EvaluationInputs modelFamily.SimpleR
            ResultsFunctionNames.standardMsuf4SimpleSEvaluationInputs, ResultsFunctions.standardMsuf4EvaluationInputs modelFamily.SimpleS
            ResultsFunctionNames.standardMsuf6SimpleEvaluationInputs, ResultsFunctions.standardMsuf6EvaluationInputs modelFamily.Simple
            ResultsFunctionNames.standardMsuf6SimpleREvaluationInputs, ResultsFunctions.standardMsuf6EvaluationInputs modelFamily.SimpleR
            ResultsFunctionNames.standardMsuf6SimpleSEvaluationInputs, ResultsFunctions.standardMsuf6EvaluationInputs modelFamily.SimpleS
            ResultsFunctionNames.mutantEval, ResultsFunctions.evaluateMutants
        ]

    let require name =
        functions |> Map.tryFind name
        |> Option.defaultWith (fun () -> invalidArg "resultsFunctionName" $"Unknown results function '{name}'. Choose a registered catalog function on the module.")

    let outputs name =
        (require name).DataTypes |> Map.toList |> List.map (fun (dataType, keys) ->
            { DataType = dataType; QueryParameterKeys = keys } : resultsDataTypeConfig)
