namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

/// Register independent setup functions by their typed catalog names.
module SetupFunctionCatalog =
    let functions : Map<string<setupFunctionName>, functionDefinition<setupFunction>> =
        Map.ofList [
            SetupFunctionNames.standardTests, SetupFunctions.standardTests
            SetupFunctionNames.evaluationSource, SetupFunctions.evaluationSource
            SetupFunctionNames.uniformMsceSimpleSorterModels, SetupFunctions.uniformMsceSorterModels modelFamily.Simple
            SetupFunctionNames.uniformMsceSimpleRSorterModels, SetupFunctions.uniformMsceSorterModels modelFamily.SimpleR
            SetupFunctionNames.uniformMsceSimpleSSorterModels, SetupFunctions.uniformMsceSorterModels modelFamily.SimpleS
            SetupFunctionNames.uniformMssiSimpleSorterModels, SetupFunctions.uniformMssiSorterModels modelFamily.Simple
            SetupFunctionNames.uniformMssiSimpleRSorterModels, SetupFunctions.uniformMssiSorterModels modelFamily.SimpleR
            SetupFunctionNames.uniformMssiSimpleSSorterModels, SetupFunctions.uniformMssiSorterModels modelFamily.SimpleS
            SetupFunctionNames.uniformMsrsSimpleSorterModels, SetupFunctions.uniformMsrsSorterModels modelFamily.Simple
            SetupFunctionNames.uniformMsrsSimpleRSorterModels, SetupFunctions.uniformMsrsSorterModels modelFamily.SimpleR
            SetupFunctionNames.uniformMsrsSimpleSSorterModels, SetupFunctions.uniformMsrsSorterModels modelFamily.SimpleS
            SetupFunctionNames.uniformMsuf4SimpleSorterModels, SetupFunctions.uniformMsuf4SorterModels modelFamily.Simple
            SetupFunctionNames.uniformMsuf4SimpleRSorterModels, SetupFunctions.uniformMsuf4SorterModels modelFamily.SimpleR
            SetupFunctionNames.uniformMsuf4SimpleSSorterModels, SetupFunctions.uniformMsuf4SorterModels modelFamily.SimpleS
            SetupFunctionNames.uniformMsuf6SimpleSorterModels, SetupFunctions.uniformMsuf6SorterModels modelFamily.Simple
            SetupFunctionNames.uniformMsuf6SimpleRSorterModels, SetupFunctions.uniformMsuf6SorterModels modelFamily.SimpleR
            SetupFunctionNames.uniformMsuf6SimpleSSorterModels, SetupFunctions.uniformMsuf6SorterModels modelFamily.SimpleS
            SetupFunctionNames.evaluationStandardMsceSimple, SetupFunctions.evaluationStandardMsce modelFamily.Simple
            SetupFunctionNames.evaluationStandardMsceSimpleR, SetupFunctions.evaluationStandardMsce modelFamily.SimpleR
            SetupFunctionNames.evaluationStandardMsceSimpleS, SetupFunctions.evaluationStandardMsce modelFamily.SimpleS
            SetupFunctionNames.evaluationStandardMssiSimple, SetupFunctions.evaluationStandardMssi modelFamily.Simple
            SetupFunctionNames.evaluationStandardMssiSimpleR, SetupFunctions.evaluationStandardMssi modelFamily.SimpleR
            SetupFunctionNames.evaluationStandardMssiSimpleS, SetupFunctions.evaluationStandardMssi modelFamily.SimpleS
            SetupFunctionNames.evaluationStandardMsrsSimple, SetupFunctions.evaluationStandardMsrs modelFamily.Simple
            SetupFunctionNames.evaluationStandardMsrsSimpleR, SetupFunctions.evaluationStandardMsrs modelFamily.SimpleR
            SetupFunctionNames.evaluationStandardMsrsSimpleS, SetupFunctions.evaluationStandardMsrs modelFamily.SimpleS
            SetupFunctionNames.evaluationStandardMsuf4Simple, SetupFunctions.evaluationStandardMsuf4 modelFamily.Simple
            SetupFunctionNames.evaluationStandardMsuf4SimpleR, SetupFunctions.evaluationStandardMsuf4 modelFamily.SimpleR
            SetupFunctionNames.evaluationStandardMsuf4SimpleS, SetupFunctions.evaluationStandardMsuf4 modelFamily.SimpleS
            SetupFunctionNames.evaluationStandardMsuf6Simple, SetupFunctions.evaluationStandardMsuf6 modelFamily.Simple
            SetupFunctionNames.evaluationStandardMsuf6SimpleR, SetupFunctions.evaluationStandardMsuf6 modelFamily.SimpleR
            SetupFunctionNames.evaluationStandardMsuf6SimpleS, SetupFunctions.evaluationStandardMsuf6 modelFamily.SimpleS
        ]

    let require name =
        functions |> Map.tryFind name
        |> Option.defaultWith (fun () -> invalidArg "setupFunctionName" $"Unknown setup function '{name}'. Choose a registered catalog function on the module.")

    let outputs name = (require name).DataTypes |> Map.toList
