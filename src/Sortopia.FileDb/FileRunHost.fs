namespace Sortopia.FileDb

open System
open System.Threading
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces

module private HostStorage =
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
type FileRunDatabase(store: ISortopiaDb, projectId: Guid<projectId>, databaseName: string, isSetup: bool,
                     functionName: string, version: string, outputs: Map<sortopiaDataType, string<paramKeyName> list>,
                     parameters: parameterSet, allowsDependency: storedDataReference -> bool, token: CancellationToken) =
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
            let keys = outputs |> Map.tryFind dataType |> Option.defaultWith (fun () -> invalidArg "dataType" $"The selected function does not declare '{dataType}'.")
            if saved |> Seq.exists (fun output -> output.Reference.DataType = dataType) then
                invalidOp $"'{dataType}' was already saved in this invocation."
            if dependencies |> List.exists (allowsDependency >> not) then
                invalidArg "dependencies" "An output may depend only on source or setup data read through this host."
            if not (DataCodec.supported (SortopiaDataType.kindName dataType)) then invalidOp $"Storage for '{dataType}' is not implemented."
            let key = DataKey.create projectId databaseName isSetup functionName version dataType keys parameters dependencies
            let make () =
                token.ThrowIfCancellationRequested()
                let value = factory ()
                if FunctionData.kind value <> SortopiaDataType.kindName dataType then invalidOp "The generated payload does not match its declared saved data type."
                Ok value
            let value, reused = store.GetOrCreate(key, make, token) |> HostStorage.require
            saved.Add { Reference = value.Reference; Reused = reused }

type private SourceResults(workspace, root, entries: setupDataTypeConfig list, token: CancellationToken) =
    let loaded = Collections.Generic.Dictionary<storedDataReference, storedData>()
    member _.Allows reference = loaded.ContainsKey reference
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
    let writer database isSetup name version outputs parameters allowed =
        FileRunDatabase(store, parent.Id, database, isSetup, name, version, outputs, parameters, allowed, token) :> IRunDatabase
    let readSetup (required: sortopiaDataType list) (outputs: storedOutput list) =
        let references = outputs |> List.map (fun output -> output.Reference)
        let find dataType =
            if not (List.contains dataType required) then invalidArg "dataType" "The results function does not declare this setup input."
            match references |> List.filter (fun reference -> reference.DataType = dataType && reference.ProjectId = parent.Id && reference.IsSetup) with
            | [reference] -> reference
            | _ -> invalidOp $"The setup batch must provide exactly one '{dataType}' input."
        required |> List.iter (find >> ignore)
        { new IRunDatabase with
            member _.Reference dataType = token.ThrowIfCancellationRequested(); find dataType
            member _.Load dataType = (store.Load(find dataType, token) |> HostStorage.require).Value
            member _.SaveIfMissing(_, _, _) = invalidOp "The setup database is read-only during results execution."
            member _.StoredOutputs = outputs }
    member _.Run = value
    member _.ProjectFolder = folder
    member _.CancellationToken = token
    member _.ForSetup parameters =
        match value with
        | run.RootRun _ -> invalidOp "A root run has no setup database."
        | run.BranchRun branch ->
            let entries = branch.SetupDataTypes |> List.filter (fun entry -> FunctionConfiguration.requiresSource branch.SetupFunctionName entry.DataType)
            let sources = SourceResults(workspace, workspaceRoot, entries, token)
            { Phase = runExecutionPhase.Setup; Parameters = parameters; CancellationToken = token
              SetupDb = writer (string branch.SetupDatabaseName) true (string branch.SetupFunctionName) branch.ProjectConfiguration.Setup.FunctionVersion
                               branch.ProjectConfiguration.Setup.DataTypes parameters sources.Allows
              ResultsDb = HostStorage.unavailableDatabase "Results writes are not allowed during setup. Read source results through SourceResults."
              SourceResults = sources }
    member _.ForResults(parameters, setupOutputs) =
        let required = FunctionCatalogs.resultsFunctionCatalog[value.ResultsFunctionName].SourceDataTypes
        let setup = readSetup required setupOutputs
        let dependencies = required |> List.map setup.Reference |> Set.ofList
        let outputs = value.ResultsDataTypes |> List.map (fun entry -> entry.DataType, entry.QueryParameterKeys) |> Map.ofList
        { Phase = runExecutionPhase.Results; Parameters = parameters; CancellationToken = token
          SetupDb = setup
          ResultsDb = writer (string value.ResultsDatabaseName) false (string value.ResultsFunctionName) parent.Snapshot.Results.FunctionVersion
                             outputs parameters (fun reference -> Set.contains reference dependencies)
          SourceResults = HostStorage.noSources }
    interface IRunHostFactory with
        member this.Run = this.Run
        member this.CancellationToken = this.CancellationToken
        member this.ForSetup parameters = this.ForSetup parameters
        member this.ForResults(parameters, outputs) = this.ForResults(parameters, outputs)
