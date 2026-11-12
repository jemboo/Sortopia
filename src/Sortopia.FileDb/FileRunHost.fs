namespace Sortopia.FileDb

open System
open System.Threading
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces

module internal HostStorage =
    let require = function Ok value -> value | Error message -> invalidOp message
    let unavailableDatabase reason = {
        new IRunDatabase with
            member _.SaveIfMissing(_, _, _) = invalidOp reason
            member _.Reference _ = invalidOp reason
            member _.Load _ = invalidOp reason
            member _.StoredOutputs = [] }
    let noSources = {
        new ISourceResults with
            member _.Find(_, _, _) = invalidOp "Source results are available only to setup functions with configured sources."
            member _.Load _ = invalidOp "Source results are available only to setup functions with configured sources." }

/// Saves one phase's declared outputs with their own projections and dependencies.
/// The host retains references, not payloads; factories run only on cache misses.
type FileRunDatabase(store: ISortopiaDb, plan: Map<sortopiaDataType, plannedOutput>, token: CancellationToken) =
    let saved = ResizeArray<storedOutput>()
    let reference dataType =
        saved |> Seq.tryFind (fun output -> output.Reference.DataType = dataType) |> Option.map (fun output -> output.Reference)
        |> Option.defaultWith (fun () -> invalidOp $"'{dataType}' has not been saved in this invocation.")
    interface IRunDatabase with
        member _.StoredOutputs = saved |> Seq.toList
        member _.Reference dataType = token.ThrowIfCancellationRequested(); reference dataType
        member _.Load dataType = (store.Load(reference dataType, token) |> HostStorage.require).Value
        member _.SaveIfMissing(dataType, dependencies, factory) =
            token.ThrowIfCancellationRequested()
            let output = plan |> Map.tryFind dataType |> Option.defaultWith (fun () -> invalidArg "dataType" $"The selected function does not declare '{dataType}'.")
            if saved |> Seq.exists (fun output -> output.Reference.DataType = dataType) then
                invalidOp $"'{dataType}' was already saved in this invocation."
            if List.sort dependencies <> output.Dependencies then
                invalidArg "dependencies" "The saved output's dependencies must exactly match its declared output plan."
            if not (DataCodec.supported (SortopiaDataType.kindName dataType)) then invalidOp $"Storage for '{dataType}' is not implemented."
            let make () =
                token.ThrowIfCancellationRequested()
                let value = factory ()
                if FunctionData.kind value <> SortopiaDataType.kindName dataType then invalidOp "The generated payload does not match its declared saved data type."
                Ok value
            let value, reused = store.GetOrCreate(output.Key, make, token) |> HostStorage.require
            saved.Add { Reference = value.Reference; Reused = reused }

/// Results execution needs only a project folder, a frozen configuration and exact
/// setup references. It deliberately has no workspace or upstream-project access.
module FileResultsHost =
    let plan projectId (config: resultsConfig) parameters (inputs: storedDataReference list) =
        let definition = FunctionCatalogs.requireResults config.FunctionName
        if config.FunctionVersion <> definition.Version then
            invalidOp $"Results function '{config.FunctionName}' requires version {config.FunctionVersion}; installed version is {definition.Version}."
        FunctionConfiguration.validateOutputs definition config.DataTypes
        FunctionValidation.validate definition parameters
        if inputs |> List.exists (fun reference -> reference.ProjectId <> projectId || not reference.IsSetup) then
            invalidArg "inputs" "Results inputs must be project-local setup references."
        OutputPlan.create projectId (string config.DatabaseName) false (string config.FunctionName) config.FunctionVersion
                          config.DataTypes definition.OutputDependencies parameters inputs

    let create folder projectId (config: resultsConfig) parameters (outputs: storedOutput list) (token: CancellationToken) =
        let definition = FunctionCatalogs.requireResults config.FunctionName
        // Setup can produce more types than results consumes; freeze only those needed.
        let inputs = outputs |> List.map _.Reference |> List.filter (fun reference -> List.contains reference.DataType definition.SourceDataTypes)
        let plan = plan projectId config parameters inputs
        let store = SortopiaFileDb(folder, projectId) :> ISortopiaDb
        let find dataType =
            if not (List.contains dataType definition.SourceDataTypes) then
                invalidArg "dataType" "The results function does not declare this setup input."
            match outputs |> List.filter (fun output -> output.Reference.DataType = dataType) with
            | [output] when output.Reference.ProjectId = projectId && output.Reference.IsSetup -> output.Reference
            | _ -> invalidOp $"The setup batch must provide exactly one project-local '{dataType}' input."
        let setup = {
            new IRunDatabase with
                member _.Reference dataType = token.ThrowIfCancellationRequested(); find dataType
                member _.Load dataType = (store.Load(find dataType, token) |> HostStorage.require).Value
                member _.SaveIfMissing(_, _, _) = invalidOp "The setup database is read-only during results execution."
                member _.StoredOutputs = outputs }
        { Phase = runExecutionPhase.Results; Parameters = parameters; CancellationToken = token
          SetupDb = setup
          ResultsDb = FileRunDatabase(store, plan, token)
          SourceResults = HostStorage.noSources }

type private SourceResults(workspace, root, entries: setupDataTypeConfig list, token: CancellationToken) =
    let loaded = Collections.Generic.Dictionary<storedDataReference, storedData>()
    interface ISourceResults with
        member _.Find(projectId, dataType, parameters) =
            token.ThrowIfCancellationRequested()
            let entry = entries |> List.tryFind (fun entry -> entry.SourceProjectId = projectId && entry.DataType = dataType)
                        |> Option.defaultWith (fun () -> invalidArg "sourceProjectId" "This project and data type are not configured as a source for this setup function.")
            let data = WorkspaceData.loadSource workspace root entry parameters token
            loaded[data.Reference] <- data
            data.Reference
        member _.Load reference =
            token.ThrowIfCancellationRequested()
            match loaded.TryGetValue reference with
            | true, data -> data.Value
            | _ -> invalidArg "reference" "Find the source result through this host before loading it."

/// Binds a saved run to setup/results storage and read-only source access, much
/// like GeneSort's runHost/runStorage/workflowSources, without catalog registration.
type FileRunHost(workspace: workspace, workspaceRoot: string, value: run, token: CancellationToken) =
    do FunctionConfiguration.validateRunInputs value
    let parent = workspace.FindProject value.ParentProjectId
                 |> Option.defaultWith (fun () -> invalidOp "The run's parent project was not found.")
    do
        if not (parent.Runs |> Array.exists (fun saved -> saved.Id = value.Id && saved = value)) then
            invalidOp "Execute requires a saved run from the supplied workspace."
    let folder = WorkspaceData.projectFolder workspace workspaceRoot parent.Id
    let store = SortopiaFileDb(folder, parent.Id) :> ISortopiaDb
    member _.Run = value
    member _.ProjectFolder = folder
    member _.CancellationToken = token
    member _.ForSetup parameters =
        match value with
        | run.RootRun _ -> invalidOp "A root run has no setup database."
        | run.BranchRun branch ->
            let entries = branch.SetupDataTypes |> List.filter (fun entry -> FunctionConfiguration.requiresSource branch.SetupFunctionName entry.DataType)
            let sources = SourceResults(workspace, workspaceRoot, entries, token)
            let definition = FunctionCatalogs.requireSetup branch.SetupFunctionName
            let inputs = entries |> List.map (fun entry -> (sources :> ISourceResults).Find(entry.SourceProjectId, entry.DataType, parameters))
            let plan = OutputPlan.create parent.Id (string branch.SetupDatabaseName) true (string branch.SetupFunctionName)
                                        branch.ProjectConfiguration.Setup.FunctionVersion branch.ProjectConfiguration.Setup.DataTypes
                                        definition.OutputDependencies parameters inputs
            { Phase = runExecutionPhase.Setup; Parameters = parameters; CancellationToken = token
              SetupDb = FileRunDatabase(store, plan, token)
              ResultsDb = HostStorage.unavailableDatabase "Results writes are not allowed during setup. Read source results through SourceResults."
              SourceResults = sources }
    member _.ForResults(parameters, setupOutputs) =
        FileResultsHost.create folder parent.Id parent.Snapshot.Results parameters setupOutputs token
    interface IRunHostFactory with
        member this.Run = this.Run
        member this.CancellationToken = this.CancellationToken
        member this.ForSetup parameters = this.ForSetup parameters
        member this.ForResults(parameters, outputs) = this.ForResults(parameters, outputs)
