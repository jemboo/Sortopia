namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

/// Register independent setup functions by their typed catalog names.
module SetupFunctionCatalog =
    let functions : Map<string<setupFunctionName>, functionDefinition<setupFunction>> =
        Map.ofList [
            SetupFunctionNames.standardTests, SetupFunctions.standardTests
            SetupFunctionNames.evaluationSource, SetupFunctions.evaluationSource
            yield! ModelGenerationCatalog.setup () |> List.map (fun (name, definition) -> UMX.tag name, definition)
        ]

    let require name =
        functions |> Map.tryFind name
        |> Option.defaultWith (fun () -> invalidArg "setupFunctionName" $"Unknown setup function '{name}'. Choose a registered catalog function on the module.")

    let outputs name = (require name).DataTypes |> Map.toList
