namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

/// Register independent setup functions by their typed catalog names.
module SetupFunctionCatalog =
    let functions : Map<string<setupFunctionName>, functionDefinition<setupFunction>> =
        Map.ofList [
            SetupFunctionNames.standardTests, SetupFunctions.standardTests
            SetupFunctionNames.evaluationSource, SetupFunctions.evaluationSource
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

    let require name =
        functions |> Map.tryFind name
        |> Option.defaultWith (fun () -> invalidArg "setupFunctionName" $"Unknown setup function '{name}'. Choose a registered catalog function on the module.")

    let outputs name = (require name).DataTypes |> Map.toList
