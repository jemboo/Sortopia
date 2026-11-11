namespace Sortopia.Functions

open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs
open Sortopia.Models.Sorting
open Sortopia.Evaluation
open FSharp.UMX
open System.Threading

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

/// Typed database payloads. Catalog functions save these through their host.
type functionData =
    | SortableTests of sortableTests * ce array
    | SorterModelSet of sorterModelSet
    | SorterSetEval of sorterSetEval

type runExecutionPhase = Setup = 0 | Results = 1

/// An invocation-scoped database view. SaveIfMissing checks the output's stable
/// key before calling its factory; reads address the exact saved setup batch.
type IRunDatabase =
    abstract SaveIfMissing: sortopiaDataType * storedDataReference list * (unit -> functionData) -> unit
    abstract Reference: sortopiaDataType -> storedDataReference
    abstract Load: sortopiaDataType -> functionData
    abstract StoredOutputs: storedOutput list

/// Read-only access to the configured source projects' results databases.
type ISourceResults =
    abstract Find: Guid<projectId> * sortopiaDataType * parameterSet -> storedDataReference
    abstract Load: storedDataReference -> functionData

/// Bound for one parameter set, with phase-appropriate database access.
type runHost = {
    Phase: runExecutionPhase
    Parameters: parameterSet
    SetupDb: IRunDatabase
    ResultsDb: IRunDatabase
    SourceResults: ISourceResults
    CancellationToken: CancellationToken
}

type sourceProjects = Map<sortopiaDataType, Guid<projectId>>
type resultsFunction = runHost -> unit
type setupFunction = runHost -> sourceProjects -> unit

/// The application supplies storage; the execution loop binds each invocation.
type IRunHostFactory =
    abstract Run: run
    abstract CancellationToken: CancellationToken
    abstract ForSetup: parameterSet -> runHost
    abstract ForResults: parameterSet * storedOutput list -> runHost

/// Required inputs are independent of each saved output's query projection.
type functionDefinition<'executor> = {
    Version: string
    RequiredParameters: paramKeyDescriptor list
    DataTypes: Map<sortopiaDataType, string<paramKeyName> list>
    /// Setup: outputs read from source projects. Results: inputs consumed from setup.
    SourceDataTypes: sortopiaDataType list
    /// Exact input types contributing to each output's stable identity.
    OutputDependencies: Map<sortopiaDataType, sortopiaDataType list>
    Execute: 'executor
}
with
    member this.SortopiaDataTypes = this.DataTypes |> Map.toList |> List.map fst

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

    let validateInputs (required: paramKeyDescriptor list) queryKeys (defaults: parameterBinding list) (spans: parameterSpan list) =
        let error = ParameterSets.inputKeysError (required |> List.map (fun key -> key.Name)) queryKeys
                        (defaults |> List.map (fun binding -> binding.KeyName)) (spans |> List.map (fun span -> span.KeyName))
        if error <> "" then invalidArg "parameters" error
        ParameterSets.validateSpans defaults spans
        for binding in defaults do
            let descriptor = required |> List.find (fun key -> key.Name = binding.KeyName)
            if binding.ValueType <> descriptor.ValueType then invalidArg "parameterDefaults" $"Parameter '{binding.KeyName}' must have type {ParameterValue.label descriptor.ValueType}."
        for span in spans do
            let descriptor = required |> List.find (fun key -> key.Name = span.KeyName)
            for text in span.Values do
                let error = ParameterValue.validationError descriptor.ValueType text
                if error <> "" then invalidArg "parameterSpans" $"Parameter '{span.KeyName}': {error}"

    let required (key: paramKey<'value>) (parameters: parameterSet) =
        parameters.TryFind key
        |> Option.defaultWith (fun () -> invalidArg "parameters" $"Missing or incorrectly typed parameter: {key.Name}.")

module FunctionDefinition =
    let private saved types (database: IRunDatabase) =
        let expected = types |> Map.toList |> List.map fst |> List.sort
        let actual = database.StoredOutputs |> List.map (fun output -> output.Reference.DataType) |> List.sort
        if expected <> actual then invalidOp "The function must save exactly its declared data types through its host database."
    let private contract version (required: paramKeyDescriptor list) entries =
        if System.String.IsNullOrWhiteSpace version then invalidArg "version" "Function version must not be blank."
        let names = required |> List.map (fun key -> key.Name)
        if names.Length <> (Set.ofList names).Count then invalidArg "requiredParameters" "Declare each required parameter exactly once."
        for key in required do
            if System.String.IsNullOrWhiteSpace (UMX.untag key.Name) then invalidArg "requiredParameters" "Required parameter keys must not be blank."
        let types = entries |> Map.ofList
        if types.IsEmpty || types.Count <> List.length entries then invalidArg "dataTypes" "Declare each saved data type exactly once."
        let kinds = types |> Map.toList |> List.map (fst >> SortopiaDataType.kindName)
        if (Set.ofList kinds).Count <> kinds.Length then invalidArg "dataTypes" "Current executors support one output of each data kind."
        for dataType, keys in entries do
            SortopiaDataType.validate dataType
            if (List.distinct keys).Length <> keys.Length then invalidArg "dataTypes" "Each output's query parameter keys must be unique."
            for key in keys do
                if not (List.contains key names) then invalidArg "dataTypes" $"Query key '{key}' is not a declared required parameter."
        types

    let private dependencies types entries =
        let dependencies = Map.ofList entries
        if dependencies.Count <> List.length entries || (dependencies |> Map.toList |> List.map fst) <> (types |> Map.toList |> List.map fst) then
            invalidArg "dependencies" "Declare dependencies for every output exactly once, including outputs without inputs."
        for _, inputs in entries do
            if (List.distinct inputs).Length <> inputs.Length then invalidArg "dependencies" "Each output's dependencies must be unique."
            inputs |> List.iter SortopiaDataType.validate
        dependencies, (entries |> List.collect snd |> List.distinct |> List.sort)

    let resultsWithInputs version required inputs dataTypes (execute: resultsFunction) : functionDefinition<resultsFunction> =
        let types = contract version required dataTypes
        let dependencies, sources = dependencies types inputs
        { Version = version; RequiredParameters = required; DataTypes = types; SourceDataTypes = sources; OutputDependencies = dependencies
          Execute = fun host ->
              if host.Phase <> runExecutionPhase.Results then invalidArg "host" "A results function requires a results host."
              host.CancellationToken.ThrowIfCancellationRequested()
              FunctionParameters.validate required host.Parameters
              execute host
              saved types host.ResultsDb }

    let results version required dataTypes execute = resultsWithInputs version required (dataTypes |> List.map (fun (dataType, _) -> dataType, [])) dataTypes execute

    let setupWithSources version required dataTypes sources (execute: setupFunction) : functionDefinition<setupFunction> =
        let types = contract version required dataTypes
        let dependencies, sources = dependencies types sources
        for source in sources do
            if not (Map.containsKey source types) then invalidArg "sources" "Each source must match a declared output type."
        { Version = version; RequiredParameters = required; DataTypes = types; SourceDataTypes = sources; OutputDependencies = dependencies
          Execute = fun host projects ->
              if host.Phase <> runExecutionPhase.Setup then invalidArg "host" "A setup function requires a setup host."
              host.CancellationToken.ThrowIfCancellationRequested()
              FunctionParameters.validate required host.Parameters
              if Set.ofList sources <> (projects |> Map.toList |> List.map fst |> Set.ofList)
                 || (projects |> Map.exists (fun _ id -> UMX.untag id = System.Guid.Empty)) then
                  invalidArg "sourceProjects" "Supply exactly the source projects required by this setup function."
              execute host projects
              saved types host.SetupDb }

    let setup version required dataTypes execute = setupWithSources version required dataTypes (dataTypes |> List.map (fun (dataType, _) -> dataType, [])) execute
