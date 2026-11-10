namespace Sortopia.Functions

open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs
open Sortopia.Models.Sorting
open Sortopia.Evaluation
open FSharp.UMX

[<Measure>] type sortopiaDataId

/// A portable reference; paths are resolved from the workspace's project IDs.
type storedDataReference = {
    ProjectId: Guid<projectId>
    Id: Guid<sortopiaDataId>
    QueryId: Guid<sortopiaDataId>
    IsSetup: bool
    DataType: sortopiaDataType
}

type storedOutput = { Reference: storedDataReference; Reused: bool }

/// In-memory data accepted and returned by the initial catalog functions.
type functionData =
    | SortableTests of sortableTests * ce array
    | SorterModelSet of sorterModelSet
    | SorterSetEval of sorterSetEval

type resultsFunction = parameterSet -> functionData list -> Result<functionData list, string>

/// A function declares which upstream types affect an output's identity. Pure
/// generators use an empty dependency list. Each factory runs only on a miss.
type ISetupDataContext =
    abstract GetOrCreate: outputKind: string * sourceKinds: string list * factory: (unit -> Result<functionData, string>) -> Result<functionData, string>

type setupFunction = ISetupDataContext -> parameterSet -> functionData list -> Result<functionData list, string>

/// Each saved data type owns its parameter projection; the union is the input contract.
type functionDefinition<'executor> = {
    DataTypes: Map<sortopiaDataType, paramKeyDescriptor list>
    /// Setup: outputs read from source projects. Results: inputs consumed from setup.
    SourceDataTypes: sortopiaDataType list
    Execute: 'executor
}
with
    member this.SortopiaDataTypes = this.DataTypes |> Map.toList |> List.map fst
    member this.RequiredParameters = this.DataTypes |> Map.toList |> List.collect snd |> List.distinctBy (fun key -> key.Name)

module SetupDataContext =
    let transient = { new ISetupDataContext with member _.GetOrCreate(_, _, factory) = factory () }

module FunctionData =
    let kind = function
        | functionData.SortableTests _ -> "SortableTests"
        | functionData.SorterModelSet _ -> "SorterModelSet"
        | functionData.SorterSetEval _ -> "SorterSetEval"

module FunctionParameters =
    let project (keys: paramKeyDescriptor list) (parameters: parameterSet) =
        keys |> List.choose (fun key -> parameters.TryFind key.Name) |> parameterSet.fromBindings
    let validationError (required: paramKeyDescriptor list) (parameters: parameterSet) =
        let allowed = required |> List.map (fun key -> key.Name) |> Set.ofList
        let errors = [
            for key in required do
                match parameters.TryFind key.Name with
                | None -> yield $"Missing required parameter '{key.Name}'."
                | Some binding when binding.ValueType <> key.ValueType ->
                    yield $"Parameter '{key.Name}' must have type {ParameterValue.label key.ValueType}."
                | _ -> ()
            for KeyValue(key, _) in parameters.ParamMap do
                if not (Set.contains key allowed) then yield $"Parameter '{key}' is not used by this function."
        ]
        System.String.Join(" ", errors)

    let validate required parameters =
        let error = validationError required parameters
        if error <> "" then invalidArg "parameters" error

    let required (key: paramKey<'value>) (parameters: parameterSet) =
        parameters.TryFind key
        |> Option.defaultWith (fun () -> invalidArg "parameters" $"Missing or incorrectly typed parameter: {key.Name}.")

module FunctionDefinition =
    let private produced types values =
        let expected = types |> Map.toList |> List.map (fst >> SortopiaDataType.kindName) |> List.sort
        let actual = values |> List.map FunctionData.kind |> List.sort
        if expected = actual then Ok values else Error "The function did not produce exactly its declared saved data types."
    let private contract entries =
        let types = entries |> Map.ofList
        if types.IsEmpty || types.Count <> List.length entries then invalidArg "dataTypes" "Declare each saved data type exactly once."
        let kinds = types |> Map.toList |> List.map (fst >> SortopiaDataType.kindName)
        if (Set.ofList kinds).Count <> kinds.Length then invalidArg "dataTypes" "Current executors support one output of each data kind."
        let parameters = types |> Map.toList |> List.collect snd
        for dataType, keys in entries do
            SortopiaDataType.validate dataType
            if (keys |> List.distinctBy (fun key -> key.Name)).Length <> keys.Length then invalidArg "dataTypes" "Each output's parameter keys must be unique."
        for name, keys in parameters |> List.groupBy (fun key -> key.Name) do
            if (keys |> List.map (fun key -> key.ValueType) |> List.distinct).Length <> 1 then invalidArg "dataTypes" $"Conflicting types for parameter '{name}'."
        types, parameters |> List.distinctBy (fun key -> key.Name)

    let resultsWithInputs inputs dataTypes (execute: resultsFunction) : functionDefinition<resultsFunction> =
        let types, required = contract dataTypes
        { DataTypes = types; SourceDataTypes = inputs
          Execute = fun parameters inputs ->
              match FunctionParameters.validationError required parameters with
              | "" -> execute parameters inputs |> Result.bind (produced types)
              | error -> Error error }

    let results dataTypes execute = resultsWithInputs [] dataTypes execute

    let setupWithSources dataTypes sources (execute: setupFunction) : functionDefinition<setupFunction> =
        let types, required = contract dataTypes
        for source in sources do
            if not (Map.containsKey source types) then invalidArg "sources" "Each source must match a declared output type."
        { DataTypes = types; SourceDataTypes = sources
          Execute = fun context parameters inputs ->
              match FunctionParameters.validationError required parameters with
              | "" -> execute context parameters inputs |> Result.bind (produced types)
              | error -> Error error }

    let setup dataTypes execute = setupWithSources dataTypes [] execute
