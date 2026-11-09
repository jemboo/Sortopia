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
            SetupFunctionNames.standardTests, SetupFunctions.makeStandardTests
            SetupFunctionNames.uniformSorterModels, SetupFunctions.makeUniformSorterModels
        ]
