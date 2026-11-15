namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

/// Register independent setup functions by their typed catalog names.
module SetupFunctionCatalog =
    let functions : Map<string<setupFunctionName>, functionDefinition<setupFunction>> =
        Map.ofList [
            SetupFunctionNames.standardTests, SetupFunctions.standardTests
            SetupFunctionNames.evaluationSource, SetupFunctions.evaluationSource
            SetupFunctionNames.evaluationSourcePrefix, SetupFunctions.evaluationSourcePrefix
            SetupFunctionNames.uniformMsceSimpleSorterModels, SetupFunctions.uniformMsceSorterModels "Simple"
            SetupFunctionNames.uniformMsceSimpleRSorterModels, SetupFunctions.uniformMsceSorterModels "SimpleR"
            SetupFunctionNames.uniformMsceSimpleSSorterModels, SetupFunctions.uniformMsceSorterModels "SimpleS"
            SetupFunctionNames.uniformMssiSimpleSorterModels, SetupFunctions.uniformMssiSorterModels "Simple"
            SetupFunctionNames.uniformMssiSimpleRSorterModels, SetupFunctions.uniformMssiSorterModels "SimpleR"
            SetupFunctionNames.uniformMssiSimpleSSorterModels, SetupFunctions.uniformMssiSorterModels "SimpleS"
            SetupFunctionNames.uniformMsrsSimpleSorterModels, SetupFunctions.uniformMsrsSorterModels "Simple"
            SetupFunctionNames.uniformMsrsSimpleRSorterModels, SetupFunctions.uniformMsrsSorterModels "SimpleR"
            SetupFunctionNames.uniformMsrsSimpleSSorterModels, SetupFunctions.uniformMsrsSorterModels "SimpleS"
            SetupFunctionNames.uniformMsuf4SimpleSorterModels, SetupFunctions.uniformMsuf4SorterModels "Simple"
            SetupFunctionNames.uniformMsuf4SimpleRSorterModels, SetupFunctions.uniformMsuf4SorterModels "SimpleR"
            SetupFunctionNames.uniformMsuf4SimpleSSorterModels, SetupFunctions.uniformMsuf4SorterModels "SimpleS"
            SetupFunctionNames.uniformMsuf6SimpleSorterModels, SetupFunctions.uniformMsuf6SorterModels "Simple"
            SetupFunctionNames.uniformMsuf6SimpleRSorterModels, SetupFunctions.uniformMsuf6SorterModels "SimpleR"
            SetupFunctionNames.uniformMsuf6SimpleSSorterModels, SetupFunctions.uniformMsuf6SorterModels "SimpleS"
            SetupFunctionNames.evaluationStandardMsceSimple, SetupFunctions.evaluationStandardMsce "Simple"
            SetupFunctionNames.evaluationStandardMsceSimpleR, SetupFunctions.evaluationStandardMsce "SimpleR"
            SetupFunctionNames.evaluationStandardMsceSimpleS, SetupFunctions.evaluationStandardMsce "SimpleS"
            SetupFunctionNames.evaluationStandardMssiSimple, SetupFunctions.evaluationStandardMssi "Simple"
            SetupFunctionNames.evaluationStandardMssiSimpleR, SetupFunctions.evaluationStandardMssi "SimpleR"
            SetupFunctionNames.evaluationStandardMssiSimpleS, SetupFunctions.evaluationStandardMssi "SimpleS"
            SetupFunctionNames.evaluationStandardMsrsSimple, SetupFunctions.evaluationStandardMsrs "Simple"
            SetupFunctionNames.evaluationStandardMsrsSimpleR, SetupFunctions.evaluationStandardMsrs "SimpleR"
            SetupFunctionNames.evaluationStandardMsrsSimpleS, SetupFunctions.evaluationStandardMsrs "SimpleS"
            SetupFunctionNames.evaluationStandardMsuf4Simple, SetupFunctions.evaluationStandardMsuf4 "Simple"
            SetupFunctionNames.evaluationStandardMsuf4SimpleR, SetupFunctions.evaluationStandardMsuf4 "SimpleR"
            SetupFunctionNames.evaluationStandardMsuf4SimpleS, SetupFunctions.evaluationStandardMsuf4 "SimpleS"
            SetupFunctionNames.evaluationStandardMsuf6Simple, SetupFunctions.evaluationStandardMsuf6 "Simple"
            SetupFunctionNames.evaluationStandardMsuf6SimpleR, SetupFunctions.evaluationStandardMsuf6 "SimpleR"
            SetupFunctionNames.evaluationStandardMsuf6SimpleS, SetupFunctions.evaluationStandardMsuf6 "SimpleS"
            SetupFunctionNames.evaluationMergeMsceSimple, SetupFunctions.evaluationMergeMsce "Simple"
            SetupFunctionNames.evaluationMergeMsceSimpleR, SetupFunctions.evaluationMergeMsce "SimpleR"
            SetupFunctionNames.evaluationMergeMsceSimpleS, SetupFunctions.evaluationMergeMsce "SimpleS"
            SetupFunctionNames.evaluationMergeMssiSimple, SetupFunctions.evaluationMergeMssi "Simple"
            SetupFunctionNames.evaluationMergeMssiSimpleR, SetupFunctions.evaluationMergeMssi "SimpleR"
            SetupFunctionNames.evaluationMergeMssiSimpleS, SetupFunctions.evaluationMergeMssi "SimpleS"
            SetupFunctionNames.evaluationMergeMsrsSimple, SetupFunctions.evaluationMergeMsrs "Simple"
            SetupFunctionNames.evaluationMergeMsrsSimpleR, SetupFunctions.evaluationMergeMsrs "SimpleR"
            SetupFunctionNames.evaluationMergeMsrsSimpleS, SetupFunctions.evaluationMergeMsrs "SimpleS"
            SetupFunctionNames.evaluationMergeMsuf4Simple, SetupFunctions.evaluationMergeMsuf4 "Simple"
            SetupFunctionNames.evaluationMergeMsuf4SimpleR, SetupFunctions.evaluationMergeMsuf4 "SimpleR"
            SetupFunctionNames.evaluationMergeMsuf4SimpleS, SetupFunctions.evaluationMergeMsuf4 "SimpleS"
            SetupFunctionNames.evaluationMergeMsuf6Simple, SetupFunctions.evaluationMergeMsuf6 "Simple"
            SetupFunctionNames.evaluationMergeMsuf6SimpleR, SetupFunctions.evaluationMergeMsuf6 "SimpleR"
            SetupFunctionNames.evaluationMergeMsuf6SimpleS, SetupFunctions.evaluationMergeMsuf6 "SimpleS"
            SetupFunctionNames.evaluationPrefixMsceSimple, SetupFunctions.evaluationPrefixMsce "Simple"
            SetupFunctionNames.evaluationPrefixMsceSimpleR, SetupFunctions.evaluationPrefixMsce "SimpleR"
            SetupFunctionNames.evaluationPrefixMsceSimpleS, SetupFunctions.evaluationPrefixMsce "SimpleS"
            SetupFunctionNames.evaluationPrefixMssiSimple, SetupFunctions.evaluationPrefixMssi "Simple"
            SetupFunctionNames.evaluationPrefixMssiSimpleR, SetupFunctions.evaluationPrefixMssi "SimpleR"
            SetupFunctionNames.evaluationPrefixMssiSimpleS, SetupFunctions.evaluationPrefixMssi "SimpleS"
            SetupFunctionNames.evaluationPrefixMsrsSimple, SetupFunctions.evaluationPrefixMsrs "Simple"
            SetupFunctionNames.evaluationPrefixMsrsSimpleR, SetupFunctions.evaluationPrefixMsrs "SimpleR"
            SetupFunctionNames.evaluationPrefixMsrsSimpleS, SetupFunctions.evaluationPrefixMsrs "SimpleS"
            SetupFunctionNames.evaluationPrefixMsuf4Simple, SetupFunctions.evaluationPrefixMsuf4 "Simple"
            SetupFunctionNames.evaluationPrefixMsuf4SimpleR, SetupFunctions.evaluationPrefixMsuf4 "SimpleR"
            SetupFunctionNames.evaluationPrefixMsuf4SimpleS, SetupFunctions.evaluationPrefixMsuf4 "SimpleS"
            SetupFunctionNames.evaluationPrefixMsuf6Simple, SetupFunctions.evaluationPrefixMsuf6 "Simple"
            SetupFunctionNames.evaluationPrefixMsuf6SimpleR, SetupFunctions.evaluationPrefixMsuf6 "SimpleR"
            SetupFunctionNames.evaluationPrefixMsuf6SimpleS, SetupFunctions.evaluationPrefixMsuf6 "SimpleS"
        ]

    let require name =
        functions |> Map.tryFind name
        |> Option.defaultWith (fun () -> invalidArg "setupFunctionName" $"Unknown setup function '{name}'. Choose a registered catalog function on the module.")

    let outputs name = (require name).DataTypes |> Map.toList
