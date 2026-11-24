namespace Sortopia.FileDb

open System
open System.Threading
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces

module internal HostStorage =
    let require = function Ok value -> value | Error message -> invalidOp message
    let noSources () = invalidOp "Source results are available only to setup functions with configured sources."

/// Saves one phase's declared outputs and reads its authorized inputs.
/// Output factories run only on cache misses; input reads never count as saved outputs.
type FileRunDatabase internal (store: ISortopiaDb, plan: Map<runDataOutput, plannedOutput>, token: CancellationToken,
                               inputs: Map<storedDataReference, unit -> sortopiaStorageData>,
                               findSource: Guid<projectId> * sortopiaDataType * parameterSet -> storedData) =
    let saved = ResizeArray<storedOutput>()
    let outputs = RunOutputs(plan |> Map.toList |> List.map fst, fun () -> saved |> Seq.toList)
    let sources = Collections.Generic.Dictionary<storedDataReference, storedData>()
    let planned descriptor =
        token.ThrowIfCancellationRequested()
        plan |> Map.tryFind descriptor |> Option.defaultWith (fun () -> invalidArg "output" "The output does not match this invocation's plan.")
    let publish descriptor factory (supplied: sortopiaStorageData option) =
        let output = planned descriptor
        let previous = saved |> Seq.tryFind (fun value -> value.Reference = output.Key.Reference)
        let validate value =
            if SortopiaStorageData.kind value <> SortopiaDataType.kindName descriptor.DataType then invalidOp "The generated payload does not match its declared saved data type."
            output.Validate value
        supplied |> Option.iter validate
        let make () =
            token.ThrowIfCancellationRequested()
            let value = factory ()
            validate value
            Ok value
        let value, reused = store.GetOrCreate(output.Key, make, token) |> HostStorage.require
        validate value.Value
        if reused then
            supplied |> Option.iter (fun expected ->
                if DataCodec.encode value.Value <> DataCodec.encode expected then
                    invalidOp $"Saved output differs from replay: {output.Key.Id}")
        if previous.IsNone then saved.Add { Reference = value.Reference; Reused = reused }
    new(store, plan, token) = FileRunDatabase(store, plan, token, Map.empty, fun _ -> HostStorage.noSources ())
    member _.Outputs = outputs
    interface IRunDatabase with
        member _.Find(output: runDataOutput) = (planned output).Key.Reference
        member _.Find(dataType: sortopiaDataType) =
            token.ThrowIfCancellationRequested()
            let references = Seq.append (inputs |> Map.toSeq |> Seq.map fst)
                                        (plan |> Map.toSeq |> Seq.map (fun (_, output) -> output.Key.Reference))
            match references |> Seq.filter (fun reference -> reference.DataType = dataType) |> Seq.distinct |> Seq.toList with
            | [reference] -> reference
            | [] -> invalidArg "dataType" $"No declared input or planned output matches '{dataType}'."
            | _ -> invalidArg "dataType" $"Ambiguous data type '{dataType}'; use an exact output descriptor or source query."
        member _.Find(projectId, dataType, parameters) =
            token.ThrowIfCancellationRequested()
            let data = findSource (projectId, dataType, parameters)
            sources[data.Reference] <- data
            data.Reference
        member _.Load reference =
            token.ThrowIfCancellationRequested()
            match sources.TryGetValue reference, Map.tryFind reference inputs with
            | (true, data), _ -> data.Value
            | _, Some read -> read ()
            | _ ->
                let output = plan |> Map.toSeq |> Seq.map snd |> Seq.tryFind (fun output -> output.Key.Reference = reference)
                             |> Option.defaultWith (fun () -> invalidArg "reference" "The reference is not an authorized input or planned output for this invocation.")
                let value =
                    match store.TryLoad(output.Key, token) |> HostStorage.require with
                    | Some saved -> saved.Value
                    | None -> invalidOp "The planned output has not been generated. Execute this invocation before loading its outputs."
                output.Validate value
                value
        member _.Save(output, factory: unit -> sortopiaStorageData) = publish output factory None
        member _.Save(output, value: sortopiaStorageData) = publish output (fun () -> value) (Some value)

/// Results execution needs only a project folder, a frozen configuration and exact
/// setup references. It deliberately has no workspace or upstream-project access.
module FileResultsHost =
    let plan projectId (config: resultsConfig) parameters (inputs: storedDataReference list) =
        let definition = ResultsFunctionCatalog.requireVersion config.FunctionName config.FunctionVersion
        FunctionConfiguration.validateOutputs definition config.DataTypes
        FunctionValidation.validate definition parameters
        if inputs |> List.exists (fun reference -> reference.ProjectId <> projectId || reference.Phase <> runExecutionPhase.Setup) then
            invalidArg "inputs" "Results inputs must be project-local setup references."
        OutputPlan.create projectId (string config.DatabaseName) false (string config.FunctionName) config.FunctionVersion
                          definition.Outputs config.DataTypes parameters inputs

    let planForRun projectId (config: resultsConfig) parameters inputs reporting sgd =
        let ordinary = plan projectId config parameters inputs
        if not (ResultsFunctionCatalog.require config.FunctionName).SupportsEvolutionReporting then
            if Option.isSome reporting || Option.isSome sgd then
                invalidArg "reporting" "The selected results function does not support evolution reporting."
            ordinary
        else
            let final = ordinary |> Map.toList |> List.map fst |> List.find (fun output -> output.DataType = sortopiaDataType.SorterPoolSet "")
            let samples = EvolutionSampling.planForRun parameters final reporting sgd |> EvolutionSampling.outputs parameters
            let sampled = OutputPlan.bind projectId (string config.DatabaseName) false (string config.FunctionName) config.FunctionVersion samples
            sampled

    let private resumeSnapshot (store: ISortopiaDb) (plan: Map<runDataOutput, plannedOutput>) sgd token =
        if Option.isNone sgd then None else
        let mutable completePrefix = true
        let mutable latest = None
        // A checkpoint is usable once every preceding snapshot and summary batch exists.
        // Missing reporting files therefore cause a replay only of the affected interval.
        let ordered = plan |> Map.toArray |> Array.sortBy (fun (descriptor, _) ->
            FunctionParameters.required ParamKeys.generationCurrentKey descriptor.QueryParameters,
            if descriptor.DataType.IsSorterPoolSet then 1 else 0)
        for descriptor, output in ordered do
            if completePrefix then
                match store.TryLoad(output.Key, token) |> HostStorage.require with
                | None -> completePrefix <- false
                | Some saved ->
                    output.Validate saved.Value
                    match saved.Value with
                    | sortopiaStorageData.SorterPoolSet snapshot -> latest <- Some snapshot
                    | _ -> ()
        latest

    let internal createWithPreparedInputs evaluationParallelism folder projectId (config: resultsConfig) parameters (outputs: storedOutput list)
                                         (prepared: Map<storedDataReference, sortopiaStorageData>) reporting sgd (token: CancellationToken) =
        if evaluationParallelism < 1 then invalidArg "evaluationParallelism" "Evaluation parallelism must be positive."
        let definition = ResultsFunctionCatalog.require config.FunctionName
        // Setup can produce more types than results consumes; freeze only those needed.
        let inputs = outputs |> List.map _.Reference |> List.filter (fun reference -> List.contains reference.DataType definition.SourceDataTypes)
        let plan = planForRun projectId config parameters inputs reporting sgd
        let store = SortopiaFileDb(folder, projectId) :> ISortopiaDb
        let reads = inputs |> List.map (fun reference ->
            reference, fun () ->
                match prepared |> Map.tryFind reference with
                | Some value -> value
                | None -> (store.Load(reference, token) |> HostStorage.require).Value) |> Map.ofList
        let database = FileRunDatabase(store, plan, token, reads, fun _ -> HostStorage.noSources ())
        { Phase = runExecutionPhase.Results; Parameters = parameters; CancellationToken = token; EvaluationParallelism = evaluationParallelism; Reporting = reporting; PreparationCache = None
          Database = database; Outputs = database.Outputs; GenerationProgress = None; Sgd = sgd; ResumeSnapshot = resumeSnapshot store plan sgd token }

    let createWithParallelism evaluationParallelism folder projectId config parameters outputs token =
        createWithPreparedInputs evaluationParallelism folder projectId config parameters outputs Map.empty None None token

    let create folder projectId config parameters outputs token =
        createWithParallelism Environment.ProcessorCount folder projectId config parameters outputs token

/// Binds a saved run to setup/results storage and read-only source access, much
/// like GeneSort's runHost/runStorage/workflowSources, without catalog registration.
type FileRunHost(scope: RunExecutionScope, workspaceRoot: string, token: CancellationToken) =
    let value = scope.Run
    let parent = scope.Parent
    let folder = WorkspaceData.scopedProjectFolder scope workspaceRoot parent.Id
    let store = SortopiaFileDb(folder, parent.Id) :> ISortopiaDb
    member _.Run = value
    member _.ProjectFolder = folder
    member _.CancellationToken = token
    member _.ForSetup parameters =
        match value.Setup with
        | None -> invalidOp "A root run has no setup database."
        | Some setup ->
            let entries = setup.Outputs |> List.filter (fun entry -> FunctionConfiguration.requiresSource setup.FunctionName entry.DataType)
            let findSource (projectId, dataType, parameters) =
                token.ThrowIfCancellationRequested()
                let entry = entries |> List.tryFind (fun entry -> entry.SourceProjectId = projectId && entry.DataType = dataType)
                            |> Option.defaultWith (fun () -> invalidArg "sourceProjectId" "This project and data type are not configured as a source for this setup function.")
                WorkspaceData.loadScopedSource scope workspaceRoot entry parameters token
            let definition = SetupFunctionCatalog.require setup.FunctionName
            let sources = entries |> List.map (fun entry -> findSource (entry.SourceProjectId, entry.DataType, parameters))
            let inputs = sources |> List.map _.Reference
            let plan = OutputPlan.create parent.Id (string setup.DatabaseName) true (string setup.FunctionName)
                                        setup.FunctionVersion definition.Outputs
                                        setup.DataTypes parameters inputs
            let reads = sources |> List.map (fun data -> data.Reference, fun () -> data.Value) |> Map.ofList
            let database = FileRunDatabase(store, plan, token, reads, findSource)
            { Phase = runExecutionPhase.Setup; Parameters = parameters; CancellationToken = token; EvaluationParallelism = Environment.ProcessorCount; Reporting = None; PreparationCache = None
              Database = database; Outputs = database.Outputs; GenerationProgress = None; Sgd = None; ResumeSnapshot = None }
    member _.ForResults(parameters, setupOutputs) =
        FileResultsHost.createWithPreparedInputs Environment.ProcessorCount folder parent.Id parent.Configuration.Results parameters setupOutputs Map.empty value.Reporting value.Sgd token
    interface IRunHostFactory with
        member this.Run = this.Run
        member this.CancellationToken = this.CancellationToken
        member this.ForSetup parameters = this.ForSetup parameters
        member this.ForResults(parameters, outputs) = this.ForResults(parameters, outputs)
