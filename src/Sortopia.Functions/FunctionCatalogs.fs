namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

/// Register an independent function by adding its typed name and function here.
module FunctionCatalogs =
    let resultsFunctionCatalog : Map<string<resultsFunctionName>, resultsFunction> =
        Map.ofList [
            ResultsFunctionNames.sortableTests, ResultsFunctions.makeSortableTests
            ResultsFunctionNames.sorterEval, ResultsFunctions.evaluateSorters
        ]

    let setupFunctionCatalog : Map<string<setupFunctionName>, setupFunction> =
        Map.ofList [
            SetupFunctionNames.standardTests, SetupFunctions.standardTests
            SetupFunctionNames.uniformSorterModels, SetupFunctions.uniformSorterModels
        ]

    // Bump when a function's output semantics change. Names remain simple map keys.
    let resultsFunctionVersions = resultsFunctionCatalog |> Map.map (fun _ _ -> "1")
    let setupFunctionVersions = setupFunctionCatalog |> Map.map (fun _ _ -> "1")
