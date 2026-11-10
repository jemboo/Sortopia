namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

module FunctionConfiguration =
    let validateOutputs (definition: functionDefinition<'executor>) (outputs: Map<sortopiaDataType, string<paramKeyName> list>) =
        if (outputs |> Map.toList |> List.map fst) <> definition.SortopiaDataTypes then
            invalidArg "dataTypes" "Saved data types, including their text, must exactly match the selected function."
        for dataType, keys in definition.DataTypes |> Map.toList do
            let required = keys |> List.map (fun key -> key.Name) |> Set.ofList
            let actual = outputs[dataType]
            if Set.ofList actual <> required || actual.Length <> required.Count then
                invalidArg "queryParameterKeys" $"The keys for '{dataType}' must exactly match the selected function's output contract."

    let validateResults (config: resultsConfig) =
        FunctionCatalogs.resultsFunctionCatalog |> Map.tryFind config.FunctionName
        |> Option.iter (fun definition -> validateOutputs definition config.DataTypes)

    let requiresSource name dataType =
        FunctionCatalogs.setupFunctionCatalog |> Map.tryFind name
        |> Option.map (fun definition -> List.contains dataType definition.SourceDataTypes) |> Option.defaultValue true

    let validateSetup requireSources (config: setupConfig) =
        FunctionCatalogs.setupFunctionCatalog |> Map.tryFind config.FunctionName
        |> Option.iter (fun definition -> validateOutputs definition config.DataTypes)
        if requireSources then
            for entry in config.Outputs do
                if requiresSource config.FunctionName entry.DataType && UMX.untag entry.SourceProjectId = System.Guid.Empty then
                    invalidArg "sourceProjectId" $"Choose a source project for '{entry.DataType}'; the selected setup function consumes this input."
