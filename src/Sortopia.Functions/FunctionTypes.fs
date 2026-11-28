namespace Sortopia.Functions

open System
open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs
open Sortopia.Models.Sorting
open Sortopia.Evaluation
open FSharp.UMX
open System.Threading

[<Measure>] type sortopiaDataId
[<Measure>] type sortopiaQueryId

type runExecutionPhase = Setup = 0 | Results = 1

module RunExecutionPhase =
    /// Convert between the execution phase and the storage format's boolean flag.
    let toIsSetup = function
        | runExecutionPhase.Setup -> true
        | runExecutionPhase.Results -> false
        | phase -> invalidArg "phase" $"Unknown execution phase: {phase}."

    let fromIsSetup isSetup = if isSetup then runExecutionPhase.Setup else runExecutionPhase.Results

/// A portable reference; paths are resolved from the workspace's project IDs.
type storedDataReference = {
    ProjectId: Guid<projectId>
    Id: Guid<sortopiaDataId>
    QueryId: Guid<sortopiaQueryId>
    Phase: runExecutionPhase
    DataType: sortopiaDataType
}

type storedOutput = { Reference: storedDataReference; Reused: bool }

/// Typed database payloads. Catalog functions save these through their host.
type sortopiaStorageData =
    | SortableTests of sortableTests
    | SorterModelSet of sorterModelSet
    | SorterSetEval of sorterSetEval
    | SorterPoolSet of Sortopia.Evaluation.Sgd.sorterPoolSet
    | SorterPoolSummarySet of Sortopia.Evaluation.Sgd.sorterPoolSummarySet

/// One output within an invocation, including its exact query projection and inputs.
type runDataOutput = {
    DataType: sortopiaDataType
    QueryParameters: parameterSet
    Dependencies: storedDataReference list
}

module RunDataOutput =
    let create dataType (keys: string<paramKeyName> list) (parameters: parameterSet) dependencies =
        let bindings = keys |> List.map (fun key ->
            parameters.TryFind key |> Option.defaultWith (fun () -> invalidArg "parameters" $"Missing query parameter '{key}'."))
        { DataType = dataType; QueryParameters = parameterSet.fromBindings bindings; Dependencies = List.sort dependencies }

/// Invocation-scoped persistence for planned outputs and authorized inputs.
/// Save reuses cached outputs and verifies any supplied values against them.
type IRunDatabase =
    /// Generate only on a cache miss; repeated saves reuse the same output.
    abstract Save: runDataOutput * (unit -> sortopiaStorageData) -> unit
    /// Validate an already computed value, including against any cached output.
    abstract Save: runDataOutput * sortopiaStorageData -> unit
    /// Resolve exactly one frozen input or planned output of this type; reject ambiguity.
    abstract Find: sortopiaDataType -> storedDataReference
    /// Resolve an exact output without generating or loading its payload.
    abstract Find: runDataOutput -> storedDataReference
    /// Resolve a configured source project's result during setup.
    abstract Find: Guid<projectId> * sortopiaDataType * parameterSet -> storedDataReference
    abstract Load: storedDataReference -> sortopiaStorageData

/// A replaceable checkpoint also retains samples waiting for their immutable summary batch.
type temporaryEvolutionSnapshot = {
    Snapshot: Sortopia.Evaluation.Sgd.sorterPoolSet
    PendingSummaries: Sortopia.Evaluation.Sgd.sgdPoolStatistics array
}

type ITemporaryEvolutionSnapshots =
    abstract Resume: temporaryEvolutionSnapshot option
    abstract SaveTemporary: temporaryEvolutionSnapshot -> unit
    abstract ClearTemporary: completedGeneration: int -> unit

/// Read-only invocation metadata. Successful writes are reported by the database
/// implementation only after its plan, payload and replay checks have passed.
type RunOutputs(planned: runDataOutput list, stored: unit -> storedOutput list) =
    member _.Planned = planned
    member _.Stored = stored ()
    member _.Output(dataType: sortopiaDataType) =
        match planned |> List.filter (fun output -> output.DataType = dataType) with
        | [output] -> output
        | _ -> invalidArg "dataType" $"Expected exactly one planned '{dataType}' output; address multiple outputs by descriptor."

    member this.Output(dataType: sortopiaDataType, dependencies: storedDataReference list) =
        let output = this.Output dataType
        if List.sort dependencies <> output.Dependencies then invalidArg "dependencies" "The saved output's dependencies must exactly match its declared output plan."
        output

/// An execution-scoped cache for read-only preparation, never persisted or global.
type RunPreparationCache(sharedInputs: Set<storedDataReference>) =
    let values = System.Collections.Concurrent.ConcurrentDictionary<string, System.Lazy<obj>>()
    new() = RunPreparationCache(Set.empty)
    member _.IsSharedInput reference = Set.contains reference sharedInputs
    member _.GetOrAdd(key, factory: unit -> obj) =
        values.GetOrAdd(key, fun _ -> System.Lazy<obj>(factory, LazyThreadSafetyMode.ExecutionAndPublication)).Value

/// Bound for one parameter set, with phase-appropriate database access.
type runHost = {
    Phase: runExecutionPhase
    Parameters: parameterSet
    Database: IRunDatabase
    Outputs: RunOutputs
    CancellationToken: CancellationToken
    /// Runtime CPU allowance for evaluation inside this invocation.
    EvaluationParallelism: int
    Reporting: sgdReportingConfig option
    Sgd: sgdRunConfig option
    ResumeSnapshot: Sortopia.Evaluation.Sgd.sorterPoolSet option
    PreparationCache: RunPreparationCache option
    /// Transient completed-generation notifications; never part of saved data.
    GenerationProgress: (int -> unit) option
    /// Optional batch allocation; refreshed at each parallel phase boundary.
    EvaluationBudget: IEvaluationCpuBudget option
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

/// Context for suggesting missing defaults without replacing existing values.
type parameterDefaultContext = {
    Current: parameterBinding list
    Related: parameterBinding list
    Missing: paramKeyDescriptor list
}

/// A calculated value supplied by a function for display alongside its inputs.
type functionPreviewValue = {
    Key: string
    Label: string
    Value: parameterValue
    Hint: string
}

/// Authoritative per-parameter policy for both field feedback and function validation.
/// The value type's parsing rules are checked first.
type parameterEditorRule = {
    Choices: string list
    Hint: string
    IsValid: string -> bool
}

/// Optional behavior is represented by empty suggestions, rules, guidance, or previews.
/// These providers are read-only and do not participate in stored identities.
type functionParameterSupport = {
    Defaults: parameterDefaultContext -> parameterBinding list
    Guidance: parameterBinding list -> parameterSpan list -> string
    Preview: parameterSet -> functionPreviewValue list
    EditorRules: Map<string<paramKeyName>, parameterEditorRule>
    CombinationError: parameterBinding list -> parameterSpan list -> string
}

module FunctionParameterSupport =
    let empty = {
        Defaults = (fun _ -> []); Guidance = (fun _ _ -> ""); Preview = (fun _ -> [])
        EditorRules = Map.empty; CombinationError = (fun _ _ -> "") }

    /// The same field check is used for editor text, defaults, and every span candidate.
    let valueError (support: functionParameterSupport) key kind text =
        let error = ParameterValue.validationError kind text
        if error <> "" then error
        else
            match Map.tryFind key support.EditorRules with
            | None -> ""
            | Some rule ->
                try
                    if rule.IsValid text then "" else $"{key}: {rule.Hint}"
                with error when error :? ArgumentException || error :? FormatException || error :? OverflowException || error.GetType() = typeof<Exception> ->
                    $"{key}: {error.Message}"

    /// Missing keys are allowed; relationships run only after all supplied values pass their field rules.
    let inputError support (defaults: parameterBinding list) (spans: parameterSpan list) =
        let errors = seq {
            for binding in defaults do yield valueError support binding.KeyName binding.ValueType binding.FormattedValue
            for span in spans do
                for text in span.Values |> List.distinct do yield valueError support span.KeyName span.ValueType text }
        match errors |> Seq.tryFind (fun error -> error <> "") with
        | Some error -> error
        | None -> support.CombinationError defaults spans

/// Setup can generate from declared parameters or resolve them from selected source queries.
[<RequireQualifiedAccess>]
type setupParameterRequirements =
    | Declared of paramKeyDescriptor list
    | FromSources

/// One saved output's contributing parameters and exact input types.
type functionOutputDefinition = {
    DataType: sortopiaDataType
    /// Catalog contributions, not the project's chosen query-key projection.
    ParameterKeys: string<paramKeyName> list
    Dependencies: sortopiaDataType list
}

module internal FunctionOutputs =
    let sources (outputs: functionOutputDefinition list) =
        outputs |> List.collect _.Dependencies |> List.distinct |> List.sort

/// Required inputs are independent of the project's chosen query projections.
type functionDefinition<'executor> = {
    Version: string
    /// Human-readable catalog guidance; does not affect execution or stored identities.
    Description: string
    ParameterSupport: functionParameterSupport
    RequiredParameters: paramKeyDescriptor list
    /// Copy functions take their required keys from the selected source output projections.
    ParametersFromSources: bool
    Outputs: functionOutputDefinition list
    /// Composed from ParameterSupport's field rules and the function's combination checks.
    /// Missing keys are allowed here so module/project defaults can be checked too.
    InputError: parameterBinding list -> parameterSpan list -> string
    /// Read-only preparation before concurrent execution. Returns inputs no longer
    /// needed by Execute (their identifiers remain available). Must not save data.
    Prepare: runHost -> Set<storedDataReference>
    Execute: 'executor
}
with
    member this.DataTypes = this.Outputs |> List.map (fun output -> output.DataType, output.ParameterKeys) |> Map.ofList
    member this.OutputDependencies = this.Outputs |> List.map (fun output -> output.DataType, output.Dependencies) |> Map.ofList
    /// Setup: source-project outputs. Results: inputs consumed from setup.
    member this.SourceDataTypes = FunctionOutputs.sources this.Outputs
    member this.SortopiaDataTypes = this.Outputs |> List.map _.DataType |> List.sort
    member this.SupportsEvolutionReporting = this.SortopiaDataTypes |> List.exists (fun value -> value.IsSorterPoolSet)

module SortopiaStorageData =
    let kind = function
        | sortopiaStorageData.SortableTests _ -> "SortableTests"
        | sortopiaStorageData.SorterModelSet _ -> "SorterModelSet"
        | sortopiaStorageData.SorterSetEval _ -> "SorterSetEval"
        | sortopiaStorageData.SorterPoolSet _ -> "SorterPoolSet"
        | sortopiaStorageData.SorterPoolSummarySet _ -> "SorterPoolSummarySet"

module FunctionParameters =
    let sourceRequirements (names: string<paramKeyName> list) =
        names |> SourceContracts.resolve |> SourceContracts.requireParameters

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
            if span.ValueType <> descriptor.ValueType then invalidArg "parameterSpans" $"Parameter '{span.KeyName}' must have type {ParameterValue.label descriptor.ValueType}."
            for text in span.Values do
                let error = ParameterValue.validationError descriptor.ValueType text
                if error <> "" then invalidArg "parameterSpans" $"Parameter '{span.KeyName}': {error}"

    let required (key: paramKey<'value>) (parameters: parameterSet) =
        parameters.TryFind key
        |> Option.defaultWith (fun () -> invalidArg "parameters" $"Missing or incorrectly typed parameter: {key.Name}.")

    /// Distinct candidates for validating partial defaults and every span value without expanding the product.
    let inputValues (key: paramKey<'value>) (defaults: parameterBinding list) (spans: parameterSpan list) =
        let read value = value |> Option.defaultWith (fun () -> invalidArg "parameters" $"Invalid value for {key.Name}.")
        [ for binding in defaults do
              if binding.KeyName = key.Name then yield ParameterValue.tryGet<'value> binding.Value |> read
          for span in spans do
              if span.KeyName = key.Name then
                  for text in span.Values do yield key.TryParse text |> read ] |> List.distinct

module FunctionValidation =
    let require error = if error <> "" then invalidArg "parameters" error

    let validateDefaults (definition: functionDefinition<'executor>) defaults =
        definition.InputError defaults [] |> require

    let validateInputs (definition: functionDefinition<'executor>) queryKeys defaults spans =
        FunctionParameters.validateInputs definition.RequiredParameters queryKeys defaults spans
        definition.InputError defaults spans |> require

    let validate (definition: functionDefinition<'executor>) (parameters: parameterSet) =
        let required = if definition.ParametersFromSources then FunctionParameters.sourceRequirements (parameters.ParamMap |> Map.toList |> List.map fst) else definition.RequiredParameters
        FunctionParameters.validate required parameters
        parameters.ParamMap |> Map.toList |> List.map snd |> validateDefaults definition

module FunctionDefinition =
    let tryPreviewValue key (definition: functionDefinition<'executor>) parameters =
        definition.ParameterSupport.Preview parameters |> List.tryFind (fun value -> value.Key = key) |> Option.map _.Value

    let describe description (definition: functionDefinition<'executor>) =
        if System.String.IsNullOrWhiteSpace description then invalidArg "description" "A function description must not be blank."
        { definition with Description = description }

    let private saved (host: runHost) =
        let expected = host.Outputs.Planned |> List.map host.Database.Find |> List.sort
        let actual = host.Outputs.Stored |> List.map _.Reference |> List.sort
        if expected <> actual then invalidOp "The function must save exactly its planned outputs through its host database."
    let private contract version (required: paramKeyDescriptor list) (outputs: functionOutputDefinition list) =
        if System.String.IsNullOrWhiteSpace version then invalidArg "version" "Function version must not be blank."
        let names = required |> List.map (fun key -> key.Name)
        if names.Length <> (Set.ofList names).Count then invalidArg "requiredParameters" "Declare each required parameter exactly once."
        for key in required do
            if System.String.IsNullOrWhiteSpace (UMX.untag key.Name) then invalidArg "requiredParameters" "Required parameter keys must not be blank."
        let types = outputs |> List.map _.DataType
        if types.IsEmpty || (List.distinct types).Length <> types.Length then invalidArg "outputs" "Declare each saved data type exactly once."
        let kinds = types |> List.map SortopiaDataType.kindName
        if (Set.ofList kinds).Count <> kinds.Length then invalidArg "dataTypes" "Current executors support one output of each data kind."
        for output in outputs do
            SortopiaDataType.validate output.DataType
            let keys = output.ParameterKeys
            if (List.distinct keys).Length <> keys.Length then invalidArg "dataTypes" "Each output's contributing parameter keys must be unique."
            for key in keys do
                if not (List.contains key names) then invalidArg "dataTypes" $"Output parameter '{key}' is not a declared required parameter."
            if (List.distinct output.Dependencies).Length <> output.Dependencies.Length then
                invalidArg "dependencies" "Each output's dependencies must be unique."
            output.Dependencies |> List.iter SortopiaDataType.validate
        outputs

    /// Build a results function with one declaration per output and shared validation.
    let results version required outputs support (execute: resultsFunction) : functionDefinition<resultsFunction> =
        let outputs = contract version required outputs
        let inputError = FunctionParameterSupport.inputError support
        { Version = version; Description = ""; ParameterSupport = support; RequiredParameters = required; ParametersFromSources = false; Outputs = outputs
          InputError = inputError; Prepare = (fun _ -> Set.empty)
          Execute = fun host ->
              if host.Phase <> runExecutionPhase.Results then invalidArg "host" "A results function requires a results host."
              host.CancellationToken.ThrowIfCancellationRequested()
              FunctionParameters.validate required host.Parameters
              inputError (host.Parameters.ParamMap |> Map.toList |> List.map snd) [] |> FunctionValidation.require
              execute host
              saved host }

    /// Build a setup function with declared or source-derived requirements and shared validation.
    let setup version parameterRequirements outputs support (execute: setupFunction) : functionDefinition<setupFunction> =
        let required, fromSources =
            match parameterRequirements with
            | setupParameterRequirements.Declared required -> required, false
            | setupParameterRequirements.FromSources -> [], true
        let outputs = contract version required outputs
        let sources = FunctionOutputs.sources outputs
        for source in sources do
            if not (outputs |> List.exists (fun output -> output.DataType = source)) then invalidArg "sources" "Each source must match a declared output type."
        if fromSources && sources.IsEmpty then invalidArg "sources" "Source-derived parameters require source inputs."
        let inputError = FunctionParameterSupport.inputError support
        { Version = version; Description = ""; ParameterSupport = support; RequiredParameters = required; ParametersFromSources = fromSources; Outputs = outputs
          InputError = inputError; Prepare = (fun _ -> Set.empty)
          Execute = fun host projects ->
              if host.Phase <> runExecutionPhase.Setup then invalidArg "host" "A setup function requires a setup host."
              host.CancellationToken.ThrowIfCancellationRequested()
              let required = if fromSources then FunctionParameters.sourceRequirements (host.Parameters.ParamMap |> Map.toList |> List.map fst) else required
              FunctionParameters.validate required host.Parameters
              inputError (host.Parameters.ParamMap |> Map.toList |> List.map snd) [] |> FunctionValidation.require
              if Set.ofList sources <> (projects |> Map.toList |> List.map fst |> Set.ofList)
                 || (projects |> Map.exists (fun _ id -> UMX.untag id = System.Guid.Empty)) then
                  invalidArg "sourceProjects" "Supply exactly the source projects required by this setup function."
              execute host projects
              saved host }
