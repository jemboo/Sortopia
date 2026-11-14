namespace Sortopia.FileDb

open System
open System.Threading
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces

type runParameterGenerationReport = {
    WorkItemIds: Guid<workItemId> array
    Execution: runExecutionReport
}

type workItemOutputStatus = { Id: Guid<workItemId>; RunId: Guid<runId>; Outputs: outputInspection }

/// Transient execution events, separate from output-derived completion status.
type workItemExecutionState = Running = 0 | Complete = 1 | Failed = 2 | Cancelled = 3
type workItemExecutionProgress = { Id: Guid<workItemId>; State: workItemExecutionState; Error: string }

/// Machine-local execution settings; never part of saved parameters or data IDs.
type resultsExecutionOptions = {
    MaxParallelJobs: int
} with
    static member Default = { MaxParallelJobs = 1 }

module RunParameterExecution =
    let private report status setup results error : runExecutionReport = { Status = status; Setup = setup; Results = results; Error = error }
    let private notify (progress: IProgress<runExecutionProgress>) message completed total =
        if not (isNull progress) then progress.Report { Message = message; Completed = completed; Total = total }
    let private notifyItem (progress: IProgress<workItemExecutionProgress>) id state error =
        if not (isNull progress) then progress.Report { Id = id; State = state; Error = error }
    let private planWorkItem (value: runParameters) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        FileResultsHost.plan value.ProjectId value.Results value.Parameters value.SetupInputs

    let private selectedItems parametersFolder runId token =
        let items = RunParameterFiles.listWorkItems parametersFolder token
        let selected = items |> Array.filter (fun item -> runId |> Option.forall ((=) item.RunId))
        if selected.Length = 0 then invalidOp "No parameter files match this selection. Generate run parameters or select another folder/run."
        selected

    let private inspect folder (item: runParameters) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        let inspection =
            try
                let store = SortopiaFileDb(folder, item.ProjectId) :> ISortopiaDb
                OutputPlan.inspect store (planWorkItem item token) token
            with
            | :? OperationCanceledException -> reraise ()
            | error -> { State = outputState.Invalid; Available = []; ExpectedCount = 0; Error = error.GetBaseException().Message }
        { Id = item.Id; RunId = item.RunId; Outputs = inspection }

    /// Read-only completion checks are derived from the expected output files.
    let inspectFolder parametersFolder runId (token: CancellationToken) =
        let items = selectedItems parametersFolder runId token
        let folder = RunParameterFiles.projectFolder parametersFolder
        items |> Array.map (fun item -> inspect folder item token)

    /// Inspect one file independently, so a folder browser can report damaged
    /// files without suppressing the status of its other files.
    let inspectFile path (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        let item = RunParameterFiles.loadFile path
        inspect (RunParameterFiles.projectFolder (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath path))) item token
    let private verifyInputs folder projectId references (token: CancellationToken) =
        let store = SortopiaFileDb(folder, projectId) :> ISortopiaDb
        for reference in references |> Seq.distinct do
            token.ThrowIfCancellationRequested()
            store.Load(reference, token) |> HostStorage.require |> ignore

    /// Local phase: resolve source projects, execute setup, freeze each results
    /// invocation. Each atomically published parameter file is independently ready.
    let private generateUsing makeHost progress (token: CancellationToken) =
        let setup = ResizeArray<runFunctionResult>()
        let ids = ResizeArray<Guid<workItemId>>()
        let mutable context = "Preparing run"
        let finish status error = report status (setup.ToArray()) [||] error
        try
            token.ThrowIfCancellationRequested()
            let factory: FileRunHost = makeHost ()
            let value = factory.Run
            let projectFolder = factory.ProjectFolder
            RunParameterStorage.requireProject projectFolder value.ParentProjectId
            let sets = RunParameterSets.create value
            let resultsDefinition = ResultsFunctionCatalog.require value.ResultsFunctionName
            let config = match value with run.RootRun root -> root.ProjectConfiguration.Results | run.BranchRun branch -> branch.ProjectConfiguration.Results
            FunctionConfiguration.validateResults config
            let setupCount = sets.Setup |> Option.map Array.length |> Option.defaultValue 0
            let resultsCount = int64 sets.Results.Length * (if sets.Setup.IsSome then int64 setupCount else 1L)
            if resultsCount = 0L || resultsCount > int64 Array.MaxLength then invalidOp "The run must generate a nonempty, supported number of results work items. Reduce the parameter spans if the product is too large."
            for parameters in sets.Results do
                token.ThrowIfCancellationRequested()
                FunctionValidation.validate resultsDefinition parameters
            match value, sets.Setup with
            | run.BranchRun branch, Some parameters ->
                let definition = SetupFunctionCatalog.require branch.SetupFunctionName
                FunctionConfiguration.validateSetup true branch.ProjectConfiguration.Setup
                for set in parameters do
                    token.ThrowIfCancellationRequested()
                    FunctionValidation.validate definition set
                let sources = branch.SourceProjectIds |> Map.filter (fun dataType _ -> List.contains dataType definition.SourceDataTypes)
                for i = 0 to parameters.Length - 1 do
                    token.ThrowIfCancellationRequested()
                    context <- $"Setup set {i + 1} · {branch.SetupFunctionName}"
                    notify progress context (int64 i) (int64 setupCount + resultsCount)
                    let host = factory.ForSetup parameters[i]
                    definition.Execute host sources
                    setup.Add { Phase = runExecutionPhase.Setup; FunctionName = string branch.SetupFunctionName
                                SetupSetNumber = None; SetNumber = i + 1; Parameters = parameters[i]; StoredOutputs = host.SetupDb.StoredOutputs }
            | run.RootRun _, None -> ()
            | _ -> invalidOp "The generated parameter sets do not match the run type."
            let batches =
                if sets.Setup.IsNone then [| None, [] |]
                else setup |> Seq.map (fun batch ->
                    Some batch.SetNumber, batch.StoredOutputs |> List.map (fun output -> output.Reference)
                                         |> List.filter (fun reference -> List.contains reference.DataType resultsDefinition.SourceDataTypes)) |> Seq.toArray
            context <- "Verifying project-local setup data"
            verifyInputs projectFolder value.ParentProjectId (batches |> Seq.collect snd) token
            for setupNumber, inputs in batches do
                for i = 0 to sets.Results.Length - 1 do
                    token.ThrowIfCancellationRequested()
                    context <- $"Saving results work item {ids.Count + 1}"
                    notify progress context (int64 setupCount + int64 ids.Count) (int64 setupCount + resultsCount)
                    let item = {
                        Id = UMX.tag Guid.Empty; ProjectId = value.ParentProjectId; RunId = value.Id
                        Results = { config with ParameterDefaults = [] }; Parameters = sets.Results[i]
                        SetupInputs = inputs; SetNumber = i + 1; SetupSetNumber = setupNumber }
                    planWorkItem item token |> ignore
                    ids.Add(RunParameterFiles.saveWorkItem projectFolder item token)
            notify progress $"Generated {ids.Count} results work items" (int64 setupCount + resultsCount) (int64 setupCount + resultsCount)
            { WorkItemIds = ids.ToArray(); Execution = finish runExecutionStatus.Completed "" }
        with error ->
            let status, message =
                match error with
                | :? OperationCanceledException -> runExecutionStatus.Cancelled, ""
                | _ -> runExecutionStatus.Failed, $"{context}: {error.GetBaseException().Message}"
            { WorkItemIds = ids.ToArray(); Execution = finish status message }

    let generate (workspace: workspace) workspaceRoot (value: run) progress (token: CancellationToken) =
        generateUsing (fun () -> FileRunHost(workspace, workspaceRoot, value, token)) progress token

    /// Only the selected saved run and its direct source results are dependencies.
    let generateInScope (scope: runWorkspace) workspaceRoot progress (token: CancellationToken) =
        generateUsing (fun () -> FileRunHost(scope, workspaceRoot, token)) progress token

    let private execute folder (workItems: runParameters array) (options: resultsExecutionOptions) progress itemProgress (token: CancellationToken) =
        let results = Array.create workItems.Length None
        let errors = Array.create workItems.Length ""
        let mutable context = "Validating prepared results"
        let mutable currentItem = None
        let finish status error = report status [||] (results |> Array.choose id) error
        try
            if options.MaxParallelJobs < 1 then invalidArg "options" "Parallel jobs must be a positive whole number."
            if workItems.Length = 0 then invalidArg "workItems" "Select at least one run parameter file."
            if workItems |> Array.distinctBy _.Id |> Array.length <> workItems.Length then
                invalidArg "workItems" "Select each parameter file only once."
            if workItems |> Array.exists (fun item -> item.ProjectId <> workItems[0].ProjectId) then
                invalidArg "workItems" "Selected parameter files must belong to one project."
            // Validate all selected work and outputs before writing anything.
            // Complete work does not require its setup payloads to be re-read.
            let database = SortopiaFileDb(folder, workItems[0].ProjectId)
            let store = database :> ISortopiaDb
            let owners = Collections.Generic.Dictionary<string, Guid<workItemId>>(StringComparer.OrdinalIgnoreCase)
            let checkedInputs = Collections.Generic.HashSet<storedDataReference>()
            let items = workItems |> Array.map (fun item ->
                currentItem <- Some item.Id
                token.ThrowIfCancellationRequested()
                context <- $"Validating work item {item.Id} · {item.Results.FunctionName}"
                let plan = planWorkItem item token
                for KeyValue(_, output) in plan do
                    let path = database.GetPath output.Key.Reference
                    match owners.TryGetValue path with
                    | true, owner ->
                        let message = $"Parameter files {owner}.msgpack and {item.Id}.msgpack declare the same results output: {path}. Select only one, or revise the results query keys so each work item has distinct outputs."
                        notifyItem itemProgress owner workItemExecutionState.Failed message
                        invalidOp message
                    | _ -> owners.Add(path, item.Id)
                let inspection = OutputPlan.inspect store plan token
                if inspection.State = outputState.Invalid then invalidOp $"Work item {item.Id}: {inspection.Error}"
                if inspection.State <> outputState.Complete then
                    verifyInputs folder item.ProjectId (item.SetupInputs |> List.filter checkedInputs.Add) token
                item, plan)
            currentItem <- None
            let workers = min options.MaxParallelJobs items.Length
            let evaluationParallelism = max 1 (Environment.ProcessorCount / workers)
            let mutable next = -1
            let mutable completed = 0
            let progressGate = obj ()
            let processItem index =
                let item, plan = items[index]
                let mutable label = $"Results work item {item.Id} · {item.Results.FunctionName}"
                try
                    token.ThrowIfCancellationRequested()
                    notifyItem itemProgress item.Id workItemExecutionState.Running ""
                    token.ThrowIfCancellationRequested()
                    // Another invocation may have completed this file since preflight.
                    let inspection = OutputPlan.inspect store plan token
                    let outputs =
                        match inspection.State with
                        | outputState.Invalid -> invalidOp inspection.Error
                        | outputState.Complete ->
                            label <- $"Skipping complete work item {item.Id} · outputs already available"
                            inspection.Available
                        | _ ->
                            let inputs = item.SetupInputs |> List.map (fun reference -> { Reference = reference; Reused = true })
                            let host = FileResultsHost.createWithParallelism evaluationParallelism folder item.ProjectId item.Results item.Parameters inputs token
                            (ResultsFunctionCatalog.require item.Results.FunctionName).Execute host
                            host.ResultsDb.StoredOutputs
                    results[index] <- Some { Phase = runExecutionPhase.Results; FunctionName = string item.Results.FunctionName
                                             SetupSetNumber = item.SetupSetNumber; SetNumber = item.SetNumber; Parameters = item.Parameters; StoredOutputs = outputs }
                    notifyItem itemProgress item.Id workItemExecutionState.Complete ""
                with
                | :? OperationCanceledException when token.IsCancellationRequested ->
                    notifyItem itemProgress item.Id workItemExecutionState.Cancelled ""
                | error ->
                    let message = $"{label}: {error.GetBaseException().Message}"
                    errors[index] <- message
                    notifyItem itemProgress item.Id workItemExecutionState.Failed message
                lock progressGate (fun () ->
                    completed <- completed + 1
                    notify progress label (int64 completed) (int64 items.Length))
            let worker () =
                let mutable working = true
                while working && not token.IsCancellationRequested do
                    let index = Interlocked.Increment(&next)
                    if index >= items.Length then working <- false else processItem index
            // A fixed number of workers, rather than a task for every file.
            Array.init workers (fun _ -> Task.Run(Action worker)) |> Task.WaitAll
            let error = errors |> Array.filter (String.IsNullOrEmpty >> not) |> String.concat Environment.NewLine
            let status = if token.IsCancellationRequested then runExecutionStatus.Cancelled
                         elif error <> "" then runExecutionStatus.Failed else runExecutionStatus.Completed
            finish status error
        with
        | :? OperationCanceledException ->
            currentItem |> Option.iter (fun id -> notifyItem itemProgress id workItemExecutionState.Cancelled "")
            finish runExecutionStatus.Cancelled ""
        | error ->
            let message = $"{context}: {error.GetBaseException().Message}"
            currentItem |> Option.iter (fun id -> notifyItem itemProgress id workItemExecutionState.Failed message)
            finish runExecutionStatus.Failed message

    /// Execute a snapshot of the folder's work, optionally restricted to one run.
    let executeFolderWithOptions parametersFolder runId options progress token =
        try
            let items = selectedItems parametersFolder runId token
            execute (RunParameterFiles.projectFolder parametersFolder) items options progress null token
        with
        | :? OperationCanceledException -> report runExecutionStatus.Cancelled [||] [||] ""
        | error -> report runExecutionStatus.Failed [||] [||] error.Message

    /// Schedulers may execute one parameter file without a manifest or workspace.
    let executeFileWithOptions path options progress (token: CancellationToken) =
        try
            token.ThrowIfCancellationRequested()
            let item = RunParameterFiles.loadFile path
            let folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath path))
            execute folder [| item |] options progress null token
        with
        | :? OperationCanceledException -> report runExecutionStatus.Cancelled [||] [||] ""
        | error -> report runExecutionStatus.Failed [||] [||] error.Message

    /// Execute exactly the selected files, with the same all-selected preflight
    /// and output reuse as folder execution. Other files are not loaded.
    let executeFilesWithOptions (paths: string array) options progress itemProgress (token: CancellationToken) =
        try
            token.ThrowIfCancellationRequested()
            if isNull paths || paths.Length = 0 then invalidArg "paths" "Select at least one run parameter file."
            let paths = paths |> Array.map System.IO.Path.GetFullPath
            let comparison = if OperatingSystem.IsWindows() then StringComparison.OrdinalIgnoreCase else StringComparison.Ordinal
            let parametersFolder = System.IO.Path.GetDirectoryName paths[0]
            if paths |> Array.exists (fun path -> not (String.Equals(System.IO.Path.GetDirectoryName path, parametersFolder, comparison))) then
                invalidArg "paths" "Select parameter files from the same runParameters folder."
            let folder = RunParameterFiles.projectFolder parametersFolder
            let items = paths |> Array.map (fun path ->
                token.ThrowIfCancellationRequested()
                try RunParameterFiles.loadFile path
                with error ->
                    match Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension path) with
                    | true, id -> notifyItem itemProgress (UMX.tag id) workItemExecutionState.Failed error.Message
                    | _ -> ()
                    reraise ())
            if items |> Array.map _.Id |> Array.distinct |> Array.length <> items.Length then invalidArg "paths" "Select each parameter file only once."
            if items |> Array.map _.ProjectId |> Array.distinct |> Array.length <> 1 then invalidArg "paths" "Selected parameter files must belong to one project."
            execute folder items options progress itemProgress token
        with
        | :? OperationCanceledException -> report runExecutionStatus.Cancelled [||] [||] ""
        | error -> report runExecutionStatus.Failed [||] [||] error.Message

    let executeFolder parametersFolder runId progress token = executeFolderWithOptions parametersFolder runId resultsExecutionOptions.Default progress token
    let executeFile path progress token = executeFileWithOptions path resultsExecutionOptions.Default progress token
    let executeFilesWithStatus paths progress itemProgress token = executeFilesWithOptions paths resultsExecutionOptions.Default progress itemProgress token
    let executeFiles paths progress token = executeFilesWithStatus paths progress null token
