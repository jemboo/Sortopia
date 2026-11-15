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
            ResultsFunctionNames.uniformMsceSimpleSorterModels, ResultsFunctions.uniformMsceSorterModels "Simple"
            ResultsFunctionNames.uniformMsceSimpleRSorterModels, ResultsFunctions.uniformMsceSorterModels "SimpleR"
            ResultsFunctionNames.uniformMsceSimpleSSorterModels, ResultsFunctions.uniformMsceSorterModels "SimpleS"
            ResultsFunctionNames.uniformMssiSimpleSorterModels, ResultsFunctions.uniformMssiSorterModels "Simple"
            ResultsFunctionNames.uniformMssiSimpleRSorterModels, ResultsFunctions.uniformMssiSorterModels "SimpleR"
            ResultsFunctionNames.uniformMssiSimpleSSorterModels, ResultsFunctions.uniformMssiSorterModels "SimpleS"
            ResultsFunctionNames.uniformMsrsSimpleSorterModels, ResultsFunctions.uniformMsrsSorterModels "Simple"
            ResultsFunctionNames.uniformMsrsSimpleRSorterModels, ResultsFunctions.uniformMsrsSorterModels "SimpleR"
            ResultsFunctionNames.uniformMsrsSimpleSSorterModels, ResultsFunctions.uniformMsrsSorterModels "SimpleS"
            ResultsFunctionNames.uniformMsuf4SimpleSorterModels, ResultsFunctions.uniformMsuf4SorterModels "Simple"
            ResultsFunctionNames.uniformMsuf4SimpleRSorterModels, ResultsFunctions.uniformMsuf4SorterModels "SimpleR"
            ResultsFunctionNames.uniformMsuf4SimpleSSorterModels, ResultsFunctions.uniformMsuf4SorterModels "SimpleS"
            ResultsFunctionNames.uniformMsuf6SimpleSorterModels, ResultsFunctions.uniformMsuf6SorterModels "Simple"
            ResultsFunctionNames.uniformMsuf6SimpleRSorterModels, ResultsFunctions.uniformMsuf6SorterModels "SimpleR"
            ResultsFunctionNames.uniformMsuf6SimpleSSorterModels, ResultsFunctions.uniformMsuf6SorterModels "SimpleS"
            ResultsFunctionNames.prefixMsceSimpleSorterModels, ResultsFunctions.prefixMsceSorterModels "Simple"
            ResultsFunctionNames.prefixMsceSimpleRSorterModels, ResultsFunctions.prefixMsceSorterModels "SimpleR"
            ResultsFunctionNames.prefixMsceSimpleSSorterModels, ResultsFunctions.prefixMsceSorterModels "SimpleS"
            ResultsFunctionNames.prefixMssiSimpleSorterModels, ResultsFunctions.prefixMssiSorterModels "Simple"
            ResultsFunctionNames.prefixMssiSimpleRSorterModels, ResultsFunctions.prefixMssiSorterModels "SimpleR"
            ResultsFunctionNames.prefixMssiSimpleSSorterModels, ResultsFunctions.prefixMssiSorterModels "SimpleS"
            ResultsFunctionNames.prefixMsrsSimpleSorterModels, ResultsFunctions.prefixMsrsSorterModels "Simple"
            ResultsFunctionNames.prefixMsrsSimpleRSorterModels, ResultsFunctions.prefixMsrsSorterModels "SimpleR"
            ResultsFunctionNames.prefixMsrsSimpleSSorterModels, ResultsFunctions.prefixMsrsSorterModels "SimpleS"
            ResultsFunctionNames.prefixMsuf4SimpleSorterModels, ResultsFunctions.prefixMsuf4SorterModels "Simple"
            ResultsFunctionNames.prefixMsuf4SimpleRSorterModels, ResultsFunctions.prefixMsuf4SorterModels "SimpleR"
            ResultsFunctionNames.prefixMsuf4SimpleSSorterModels, ResultsFunctions.prefixMsuf4SorterModels "SimpleS"
            ResultsFunctionNames.prefixMsuf6SimpleSorterModels, ResultsFunctions.prefixMsuf6SorterModels "Simple"
            ResultsFunctionNames.prefixMsuf6SimpleRSorterModels, ResultsFunctions.prefixMsuf6SorterModels "SimpleR"
            ResultsFunctionNames.prefixMsuf6SimpleSSorterModels, ResultsFunctions.prefixMsuf6SorterModels "SimpleS"
            ResultsFunctionNames.standardMsceSimpleEvaluationInputs, ResultsFunctions.standardMsceEvaluationInputs "Simple"
            ResultsFunctionNames.standardMsceSimpleREvaluationInputs, ResultsFunctions.standardMsceEvaluationInputs "SimpleR"
            ResultsFunctionNames.standardMsceSimpleSEvaluationInputs, ResultsFunctions.standardMsceEvaluationInputs "SimpleS"
            ResultsFunctionNames.standardMssiSimpleEvaluationInputs, ResultsFunctions.standardMssiEvaluationInputs "Simple"
            ResultsFunctionNames.standardMssiSimpleREvaluationInputs, ResultsFunctions.standardMssiEvaluationInputs "SimpleR"
            ResultsFunctionNames.standardMssiSimpleSEvaluationInputs, ResultsFunctions.standardMssiEvaluationInputs "SimpleS"
            ResultsFunctionNames.standardMsrsSimpleEvaluationInputs, ResultsFunctions.standardMsrsEvaluationInputs "Simple"
            ResultsFunctionNames.standardMsrsSimpleREvaluationInputs, ResultsFunctions.standardMsrsEvaluationInputs "SimpleR"
            ResultsFunctionNames.standardMsrsSimpleSEvaluationInputs, ResultsFunctions.standardMsrsEvaluationInputs "SimpleS"
            ResultsFunctionNames.standardMsuf4SimpleEvaluationInputs, ResultsFunctions.standardMsuf4EvaluationInputs "Simple"
            ResultsFunctionNames.standardMsuf4SimpleREvaluationInputs, ResultsFunctions.standardMsuf4EvaluationInputs "SimpleR"
            ResultsFunctionNames.standardMsuf4SimpleSEvaluationInputs, ResultsFunctions.standardMsuf4EvaluationInputs "SimpleS"
            ResultsFunctionNames.standardMsuf6SimpleEvaluationInputs, ResultsFunctions.standardMsuf6EvaluationInputs "Simple"
            ResultsFunctionNames.standardMsuf6SimpleREvaluationInputs, ResultsFunctions.standardMsuf6EvaluationInputs "SimpleR"
            ResultsFunctionNames.standardMsuf6SimpleSEvaluationInputs, ResultsFunctions.standardMsuf6EvaluationInputs "SimpleS"
            ResultsFunctionNames.mutantEval, ResultsFunctions.evaluateMutants
        ]

    let require name =
        functions |> Map.tryFind name
        |> Option.defaultWith (fun () -> invalidArg "resultsFunctionName" $"Unknown results function '{name}'. Choose a registered catalog function on the module.")

    let outputs name =
        (require name).DataTypes |> Map.toList |> List.map (fun (dataType, keys) ->
            { DataType = dataType; QueryParameterKeys = keys } : resultsDataTypeConfig)
