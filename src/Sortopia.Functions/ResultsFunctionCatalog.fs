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
            yield! ModelGenerationCatalog.results () |> List.map (fun (name, definition) -> UMX.tag name, definition)
            ResultsFunctionNames.mutantEval, ResultsFunctions.evaluateMutants
        ]

    let require name =
        functions |> Map.tryFind name
        |> Option.defaultWith (fun () -> invalidArg "resultsFunctionName" $"Unknown results function '{name}'. Choose a registered catalog function on the module.")

    let outputs name =
        (require name).DataTypes |> Map.toList |> List.map (fun (dataType, keys) ->
            { DataType = dataType; QueryParameterKeys = keys } : resultsDataTypeConfig)
