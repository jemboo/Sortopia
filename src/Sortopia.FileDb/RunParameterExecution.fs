namespace Sortopia.FileDb

open System
open System.Threading
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

type runParameterGenerationReport = {
    WorkItemIds: Guid<workItemId> array
    Execution: runExecutionReport
}

type workItemOutputStatus = { Id: Guid<workItemId>; RunId: Guid<runId>; Outputs: outputInspection }

/// Transient execution events, separate from output-derived completion status.
type workItemExecutionState = Running = 0 | Complete = 1 | Failed = 2 | Cancelled = 3
type workItemExecutionProgress = { Id: Guid<workItemId>; State: workItemExecutionState; Error: string }

/// Lightweight live progress, independent of state transitions and persisted reports.
type workItemGenerationProgress = { Id: Guid<workItemId>; Generation: int }

/// Machine-local execution settings; never part of saved parameters or data IDs.
type resultsExecutionOptions(maxParallelJobs: int, cpuWorkers: int, memoryTargetBytes: int64, maxWorkersPerFile: int) =
    member _.MaxParallelJobs = maxParallelJobs
    member _.CpuWorkers = cpuWorkers
    member _.MemoryTargetBytes = memoryTargetBytes
    member _.MaxWorkersPerFile = maxWorkersPerFile
    new(maxParallelJobs, cpuWorkers, memoryTargetBytes) =
        resultsExecutionOptions(maxParallelJobs, cpuWorkers, memoryTargetBytes, ExecutionTuning.workersPerFile cpuWorkers)
    new(maxParallelJobs) = resultsExecutionOptions(maxParallelJobs, Environment.ProcessorCount, 0L)
    static member Default = resultsExecutionOptions(1)

module RunParameterExecution =
    let private report status setup results error : runExecutionReport = { Status = status; Setup = setup; Results = results; Error = error }
    let private notify (progress: IProgress<runExecutionProgress>) message completed total =
        if not (isNull progress) then progress.Report { Message = message; Completed = completed; Total = total }
    let private notifyItem (progress: IProgress<workItemExecutionProgress>) id state error =
        if not (isNull progress) then progress.Report { Id = id; State = state; Error = error }
    let private planWorkItem (value: runParameters) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        RunOutputFiles.plan value

    let private selectedItems parametersFolder runId token =
        let items = RunParameterFiles.listWorkItems parametersFolder token
        let selected = items |> Array.filter (fun item -> runId |> Option.forall ((=) item.RunId))
        if selected.Length = 0 then invalidOp "No parameter files match this selection. Generate run parameters or select another folder/run."
        selected

    let private inspect folder (item: runParameters) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        let inspection =
            try
                OutputPlan.inspect (SortopiaFileDb(folder, item.ProjectId)) (planWorkItem item token) token
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

    /// A quick status check, not a substitute for inspectFile before consuming
    /// results. Every ordinary output, snapshot and summary has its identity,
    /// envelope bounds and compressed payload checksum checked.
    let inspectFileIntegrity path (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        let item = RunParameterFiles.loadFile path
        let folder = RunParameterFiles.projectFolder (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath path))
        let outputs =
            try
                let keys = planWorkItem item token |> Map.toArray |> Array.map (snd >> _.Key)
                OutputPlan.inspectIntegrity (SortopiaFileDb(folder, item.ProjectId)) keys token
            with
            | :? OperationCanceledException -> reraise ()
            | error -> { State = outputState.Invalid; Available = []; ExpectedCount = 0; Error = error.GetBaseException().Message }
        { Id = item.Id; RunId = item.RunId; Outputs = outputs }
    let private verifyInputs folder projectId references (token: CancellationToken) =
        let store = SortopiaFileDb(folder, projectId) :> ISortopiaDb
        for reference in references |> Seq.distinct do
            token.ThrowIfCancellationRequested()
            store.Load(reference, token) |> HostStorage.require |> ignore

    /// Local phase: resolve source projects, execute setup, freeze each results
    /// invocation. Each atomically published parameter file is independently ready.
    let private generateUsing makeHost beforeWrite progress (token: CancellationToken) =
        let setup = ResizeArray<runFunctionResult>()
        let setupSuffixStages = ResizeArray<int option>()
        let ids = ResizeArray<Guid<workItemId>>()
        let mutable context = "Preparing run"
        let finish status error = report status (setup.ToArray()) [||] error
        try
            token.ThrowIfCancellationRequested()
            let factory: FileRunHost = makeHost ()
            let value = factory.Run
            FunctionConfiguration.validateRunInputs value
            let projectFolder = factory.ProjectFolder
            RunParameterStorage.requireProject projectFolder value.ParentProjectId
            let sets = RunParameterSets.create value
            let config = value.ProjectConfiguration.Results
            let resultsDefinition = ResultsFunctionCatalog.requireVersion value.ResultsFunctionName config.FunctionVersion
            FunctionConfiguration.validateResults config
            let setupCount = sets.Setup |> Option.map Array.length |> Option.defaultValue 0
            let resultsCount = int64 sets.Results.Length * (if sets.Setup.IsSome then int64 setupCount else 1L)
            if resultsCount = 0L || resultsCount > int64 Array.MaxLength then invalidOp "The run must generate a nonempty, supported number of results work items. Reduce the parameter spans if the product is too large."
            for parameters in sets.Results do
                token.ThrowIfCancellationRequested()
                FunctionValidation.validate resultsDefinition parameters
            match value.Setup, sets.Setup with
            | Some settings, Some parameters ->
                let definition = SetupFunctionCatalog.require settings.FunctionName
                FunctionConfiguration.validateSetup true settings
                for set in parameters do
                    token.ThrowIfCancellationRequested()
                    FunctionValidation.validate definition set
                token.ThrowIfCancellationRequested()
                beforeWrite ()
                let sources = settings.SourceProjectIds |> Map.filter (fun dataType _ -> List.contains dataType definition.SourceDataTypes)
                for i = 0 to parameters.Length - 1 do
                    token.ThrowIfCancellationRequested()
                    context <- $"Setup set {i + 1} · {settings.FunctionName}"
                    notify progress context (int64 i) (int64 setupCount + resultsCount)
                    let host = factory.ForSetup parameters[i]
                    definition.Execute host sources
                    let suffixStages =
                        if definition.ParametersFromSources then
                            let output = host.Outputs.Output (sortopiaDataType.SorterModelSet "")
                            match host.Database.Load(host.Database.Find output) with
                            | sortopiaStorageData.SorterModelSet models ->
                                models.SorterModels |> Array.map (function
                                    | Sortopia.Models.Sorting.sorterModel.Simple model -> Some model
                                    | Sortopia.Models.Sorting.sorterModel.SimpleR model -> Some model.Model
                                    | Sortopia.Models.Sorting.sorterModel.SimpleS model -> Some model.Model
                                    | _ -> None)
                                |> Array.map (Option.map (SimpleSorterModel.getStageLength >> int))
                                |> Array.distinct |> function [| Some stages |] -> Some stages | _ -> None
                            | _ -> invalidOp "Expected copied sorter models."
                        else FunctionDefinition.tryPreviewValue "SuffixStages" definition parameters[i] |> Option.bind ParameterValue.tryGet<int>
                    setupSuffixStages.Add suffixStages
                    setup.Add { Phase = runExecutionPhase.Setup; FunctionName = string settings.FunctionName
                                SetupSetNumber = None; SetNumber = i + 1; Parameters = parameters[i]; StoredOutputs = host.Outputs.Stored }
            | None, None ->
                token.ThrowIfCancellationRequested()
                beforeWrite ()
            | _ -> invalidOp "The generated parameter sets do not match the run type."
            let batches =
                if sets.Setup.IsNone then [| None, [], None |]
                else setup |> Seq.mapi (fun i batch ->
                    Some batch.SetNumber, batch.StoredOutputs |> List.map (fun output -> output.Reference)
                                         |> List.filter (fun reference -> List.contains reference.DataType resultsDefinition.SourceDataTypes),
                    setupSuffixStages[i]) |> Seq.toArray
            context <- "Verifying project-local setup data"
            verifyInputs projectFolder value.ParentProjectId (batches |> Seq.collect (fun (_, inputs, _) -> inputs)) token
            for setupNumber, inputs, setupStages in batches do
                for i = 0 to sets.Results.Length - 1 do
                    token.ThrowIfCancellationRequested()
                    context <- $"Saving results work item {ids.Count + 1}"
                    notify progress context (int64 setupCount + int64 ids.Count) (int64 setupCount + resultsCount)
                    let item = {
                        Id = UMX.tag Guid.Empty; ProjectId = value.ParentProjectId; RunId = value.Id
                        Results = { config with ParameterDefaults = [] }; Parameters = sets.Results[i]
                        SetupInputs = inputs; SetNumber = i + 1; SetupSetNumber = setupNumber
                        SetupSuffixStages = setupStages
                        ResultsSuffixStages = FunctionDefinition.tryPreviewValue "SuffixStages" resultsDefinition sets.Results[i] |> Option.bind ParameterValue.tryGet<int>
                        Reporting = value.Reporting; Sgd = value.Sgd }
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

    /// Only the selected saved run and its direct source results are dependencies.
    let generateInScope (scope: RunExecutionScope) workspaceRoot progress (token: CancellationToken) =
        generateUsing (fun () -> FileRunHost(scope, workspaceRoot, token)) ignore progress token

    /// Persist the parent snapshot after validation, before any setup or work-item writes.
    let generateInScopeBeforeWrite (scope: RunExecutionScope) workspaceRoot beforeWrite progress (token: CancellationToken) =
        generateUsing (fun () -> FileRunHost(scope, workspaceRoot, token)) beforeWrite progress token

    let private execute folder (workItems: runParameters array) (nextPath: Func<string>) (options: resultsExecutionOptions) progress itemProgress (generationProgress: IProgress<workItemGenerationProgress>) (token: CancellationToken) =
        let results = Collections.Concurrent.ConcurrentDictionary<Guid<workItemId>, runFunctionResult>()
        let errors = Collections.Concurrent.ConcurrentDictionary<Guid<workItemId>, string>()
        let order = ResizeArray(workItems |> Array.map _.Id)
        let mutable context = "Validating prepared results"
        let mutable currentItem = None
        let finish status error = report status [||] (order |> Seq.choose (fun id -> match results.TryGetValue id with true, value -> Some value | _ -> None) |> Seq.toArray) error
        try
            if options.MaxParallelJobs < 1 then invalidArg "options" "Parallel jobs must be a positive whole number."
            if options.CpuWorkers < 1 || options.MemoryTargetBytes < 0L then invalidArg "options" "Invalid execution resource budget."
            if options.MaxWorkersPerFile < 1 || options.MaxWorkersPerFile > options.CpuWorkers then invalidArg "options" "Workers per file must be within the CPU budget."
            if workItems.Length = 0 then invalidArg "workItems" "Select at least one run parameter file."
            if workItems |> Array.distinctBy _.Id |> Array.length <> workItems.Length then
                invalidArg "workItems" "Select each parameter file only once."
            if workItems |> Array.exists (fun item -> item.ProjectId <> workItems[0].ProjectId) then
                invalidArg "workItems" "Selected parameter files must belong to one project."
            // Validate all selected work and outputs before writing anything.
            // Complete work does not require its setup payloads to be re-read.
            let database = SortopiaFileDb(folder, workItems[0].ProjectId)
            let memory = ExecutionMemoryGate(options.MemoryTargetBytes)
            let cpu = ExecutionCpuBudget(options.CpuWorkers)
            let estimates = Collections.Concurrent.ConcurrentDictionary<Guid<workItemId>, int64>()
            let validated = Collections.Concurrent.ConcurrentDictionary<Guid<workItemId>, byte array array * outputInspection>()
            let store = database :> ISortopiaDb
            let owners = Collections.Generic.Dictionary<string, Guid<workItemId>>(StringComparer.OrdinalIgnoreCase)
            let checkedInputs = Collections.Generic.HashSet<storedDataReference>()
            // Share validated read-only inputs common to every invocation. Mutation
            // sweeps otherwise deserialize the same large model set in every worker.
            // Inputs that vary across the batch are released after preflight, so a
            // source sweep does not retain all of its model sets at once.
            let sharedReferences = workItems |> Array.map (fun item -> Set.ofList item.SetupInputs) |> Set.intersectMany
            let mutable preparedInputs = Map.empty
            let pending = Collections.Generic.HashSet<Guid<workItemId>>()
            let claimOutputs notifyOwner (item: runParameters) (plan: Map<runDataOutput, plannedOutput>) =
                for KeyValue(_, output) in plan do
                    let path = database.GetPath output.Key.Reference
                    match owners.TryGetValue path with
                    | true, owner ->
                        let message = $"Parameter files {owner}.msgpack and {item.Id}.msgpack declare the same results output: {path}. Select only one, or revise the results query keys so each work item has distinct outputs."
                        if notifyOwner then notifyItem itemProgress owner workItemExecutionState.Failed message
                        invalidOp message
                    | _ -> ()
                for KeyValue(_, output) in plan do owners.Add(database.GetPath output.Key.Reference, item.Id)
            let validateItem notifyOwner (item: runParameters) =
                token.ThrowIfCancellationRequested()
                if item.ProjectId <> workItems[0].ProjectId then invalidOp "Selected parameter files must belong to one project."
                let plan = planWorkItem item token
                claimOutputs notifyOwner item plan
                let generationEstimate =
                    if options.MemoryTargetBytes = 0L || (item.Parameters.TryFind ParamKeys.sorterCountKey).IsNone then 0L
                    else
                        let definition = ResultsFunctionCatalog.requireVersion item.Results.FunctionName item.Results.FunctionVersion
                        let stages = FunctionDefinition.tryPreviewValue "SuffixStages" definition item.Parameters
                                     |> Option.bind ParameterValue.tryGet<int> |> Option.defaultValue 0
                        ExecutionTuning.modelGenerationReservationBytes item.Parameters stages
                // Completed model sets also need space when their payload is decoded
                // for the execution-time completion check.
                estimates[item.Id] <- generationEstimate
                let before = OutputPlan.fingerprints database plan token
                let inspection = OutputPlan.inspect store plan token
                if inspection.State = outputState.Invalid then invalidOp $"Work item {item.Id}: {inspection.Error}"
                if inspection.State = outputState.Complete then
                    // Hash both sides of full decoding so a concurrent replacement
                    // cannot associate a successful check with unvalidated bytes.
                    match before with
                    | Some hashes when OutputPlan.fingerprints database plan token = before ->
                        validated[item.Id] <- hashes, inspection
                    | _ -> ()
                if inspection.State <> outputState.Complete then
                    pending.Add item.Id |> ignore
                    if options.MemoryTargetBytes > 0L then
                        // Serialized size is a conservative admission hint, not a bound
                        // on decoded object graphs or future generations. An optional OS cap
                        // is enforced separately; exceeding an estimate is allowed.
                        let raw = item.SetupInputs |> List.filter (fun reference -> not (Set.contains reference sharedReferences)) |> List.sumBy (fun reference ->
                            int64 (DataEnvelope.readHeader (database.GetPath reference) token).Envelope.RawLength)
                        estimates[item.Id] <- max generationEstimate (max (max (64L * 1024L * 1024L) (raw * 3L)) (ExecutionTuning.populationReservationBytes item.Parameters))
                    let references = item.SetupInputs |> List.filter checkedInputs.Add
                    for reference in references do
                        let value = (store.Load(reference, token) |> HostStorage.require).Value
                        if Set.contains reference sharedReferences then
                            preparedInputs <- Map.add reference value preparedInputs
                item, plan
            let items = workItems |> Array.map (fun item ->
                currentItem <- Some item.Id
                context <- $"Validating work item {item.Id} · {item.Results.FunctionName}"
                validateItem true item)
            let preparationCache = RunPreparationCache(sharedReferences)
            // Expensive source preparation runs once per cache key with the tuned
            // per-file ceiling, before launching mutation workers. The cache keeps
            // selected seed models, not every large source pool.
            let prepareItem (item: runParameters) =
                if pending.Contains item.Id then
                    token.ThrowIfCancellationRequested()
                    notify progress $"Preparing shared inputs for work item {item.Id}" 0L (int64 items.Length)
                    use allocation = cpu.Register()
                    let inputs = item.SetupInputs |> List.map (fun reference -> { Reference = reference; Reused = true })
                    let host = FileResultsHost.createWithPreparedInputs options.MaxWorkersPerFile folder item.ProjectId item.Results item.Parameters inputs preparedInputs item.Reporting item.Sgd token
                    (ResultsFunctionCatalog.requireVersion item.Results.FunctionName item.Results.FunctionVersion).Prepare
                        { host with PreparationCache = Some preparationCache; EvaluationBudget = Some allocation }
                else Set.empty
            let mutable executionInputs = Set.empty
            for item, _ in items do
                currentItem <- Some item.Id
                context <- $"Preparing shared inputs for work item {item.Id}"
                let released = prepareItem item
                if pending.Contains item.Id then
                    executionInputs <- Set.union executionInputs (Set.difference (Set.ofList item.SetupInputs) released)
            // Keep only inputs still needed by a pending consumer. SGD preparation
            // owns its selected seeds; retaining the full source defeats that saving.
            // A live queue can add consumers later, so retain shared inputs there.
            if isNull nextPath then
                preparedInputs <- preparedInputs |> Map.filter (fun reference _ -> Set.contains reference executionInputs)
            currentItem <- None
            // Initial preflight still detects collisions before any writes. Live
            // ownership starts at dispatch, so unchecked pending files do not
            // reserve outputs against a newly checked replacement.
            if not (isNull nextPath) then owners.Clear()
            let maxWorkers = min options.MaxParallelJobs options.CpuWorkers
            let workers = if isNull nextPath then min maxWorkers items.Length else maxWorkers
            let mutable next = -1
            let mutable completed = 0
            let progressGate = obj ()
            let processItem (item: runParameters, plan) =
                let mutable label = $"Results work item {item.Id} · {item.Results.FunctionName}"
                try
                    token.ThrowIfCancellationRequested()
                    notifyItem itemProgress item.Id workItemExecutionState.Running ""
                    token.ThrowIfCancellationRequested()
                    let reusable =
                        match validated.TryGetValue item.Id with
                        | true, (hashes, inspection) when OutputPlan.fingerprints database plan token = Some hashes -> Some inspection
                        | _ -> None
                    let skip (inspection: outputInspection) =
                        label <- $"Skipping complete work item {item.Id} · outputs already available"
                        inspection.Available
                    let outputs =
                        match reusable with
                        | Some inspection -> skip inspection
                        | None ->
                            // Pending or changed outputs still need a full check under
                            // admission, including work completed since preflight.
                            let estimate = match estimates.TryGetValue item.Id with true, size -> size | _ -> 0L
                            use admission = memory.Acquire(estimate, token)
                            let inspection = OutputPlan.inspect store plan token
                            match inspection.State with
                            | outputState.Invalid -> invalidOp inspection.Error
                            | outputState.Complete -> skip inspection
                            | _ ->
                                use allocation = cpu.Register()
                                let inputs = item.SetupInputs |> List.map (fun reference -> { Reference = reference; Reused = true })
                                let host = FileResultsHost.createWithPreparedInputs options.MaxWorkersPerFile folder item.ProjectId item.Results item.Parameters inputs preparedInputs item.Reporting item.Sgd token
                                let host = { host with PreparationCache = Some preparationCache
                                                       EvaluationBudget = Some allocation
                                                       GenerationProgress =
                                                           if isNull generationProgress then None
                                                           else Some (fun generation ->
                                                               if not (isNull generationProgress) then generationProgress.Report { Id = item.Id; Generation = generation }) }
                                (ResultsFunctionCatalog.requireVersion item.Results.FunctionName item.Results.FunctionVersion).Execute host
                                host.Outputs.Stored
                    results[item.Id] <-
                        { Phase = runExecutionPhase.Results; FunctionName = string item.Results.FunctionName
                          SetupSetNumber = item.SetupSetNumber; SetNumber = item.SetNumber; Parameters = item.Parameters; StoredOutputs = outputs }
                    notifyItem itemProgress item.Id workItemExecutionState.Complete ""
                with
                | :? OperationCanceledException when token.IsCancellationRequested ->
                    notifyItem itemProgress item.Id workItemExecutionState.Cancelled ""
                | error ->
                    let message = $"{label}: {error.GetBaseException().Message}"
                    errors[item.Id] <- message
                    notifyItem itemProgress item.Id workItemExecutionState.Failed message
                lock progressGate (fun () ->
                    completed <- completed + 1
                    notify progress label (int64 completed) (int64 items.Length))
            let prepared = Collections.Generic.Dictionary<Guid<workItemId>, _>(items |> Array.map (fun (item, plan) -> Collections.Generic.KeyValuePair(item.Id, (item, plan))))
            let claimed = Collections.Generic.HashSet<Guid<workItemId>>()
            let queueGate = obj ()
            let takeNext () =
                if isNull nextPath then
                    let index = Interlocked.Increment(&next)
                    if index >= items.Length then None else Some items[index]
                else
                    // Claim on the caller's UI context, then validate newly checked work
                    // serially. Execution stays parallel and shares the preparation cache.
                    lock queueGate (fun () ->
                        let mutable result = None
                        let mutable searching = true
                        while searching && not token.IsCancellationRequested do
                            let path = nextPath.Invoke()
                            if String.IsNullOrEmpty path then searching <- false
                            else
                                let mutable id = match Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension path) with true, id -> Some (UMX.tag id) | _ -> None
                                try
                                    let fullPath = System.IO.Path.GetFullPath path
                                    let comparison = if OperatingSystem.IsWindows() then StringComparison.OrdinalIgnoreCase else StringComparison.Ordinal
                                    if not (String.Equals(System.IO.Path.GetDirectoryName fullPath, System.IO.Path.Combine(folder, "runParameters"), comparison)) then
                                        invalidOp "Select parameter files from the same runParameters folder."
                                    let item = RunParameterFiles.loadFile fullPath
                                    id <- Some item.Id
                                    if not (claimed.Add item.Id) then invalidOp "Select each parameter file only once."
                                    let ready =
                                        match prepared.TryGetValue item.Id with
                                        | true, (item, plan) ->
                                            claimOutputs false item plan
                                            item, plan
                                        | _ ->
                                            order.Add item.Id
                                            let ready = validateItem false item
                                            prepareItem item |> ignore
                                            prepared.Add(item.Id, ready)
                                            ready
                                    result <- Some ready
                                    searching <- false
                                with
                                | :? OperationCanceledException when token.IsCancellationRequested ->
                                    id |> Option.iter (fun id -> notifyItem itemProgress id workItemExecutionState.Cancelled "")
                                    searching <- false
                                | error ->
                                    let message = $"Validating work item {path}: {error.GetBaseException().Message}"
                                    id |> Option.iter (fun id ->
                                        errors[id] <- message
                                        notifyItem itemProgress id workItemExecutionState.Failed message)
                        result)
            let worker () =
                let mutable working = true
                while working && not token.IsCancellationRequested do
                    match takeNext () with
                    | None -> working <- false
                    | Some item -> processItem item
            // A fixed number of workers, rather than a task for every file.
            Array.init workers (fun _ -> Task.Run(Action worker)) |> Task.WaitAll
            let error = errors |> Seq.sortBy _.Key |> Seq.map _.Value |> String.concat Environment.NewLine
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
            execute (RunParameterFiles.projectFolder parametersFolder) items null options progress null null token
        with
        | :? OperationCanceledException -> report runExecutionStatus.Cancelled [||] [||] ""
        | error -> report runExecutionStatus.Failed [||] [||] error.Message

    /// Schedulers may execute one parameter file without a manifest or workspace.
    let executeFileWithOptions path options progress (token: CancellationToken) =
        try
            token.ThrowIfCancellationRequested()
            let item = RunParameterFiles.loadFile path
            let folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath path))
            execute folder [| item |] null options progress null null token
        with
        | :? OperationCanceledException -> report runExecutionStatus.Cancelled [||] [||] ""
        | error -> report runExecutionStatus.Failed [||] [||] error.Message

    /// Execute exactly the selected files, with the same all-selected preflight
    /// and output reuse as folder execution. Other files are not loaded.
    let private executeFilesUsing (paths: string array) nextPath options progress itemProgress generationProgress (token: CancellationToken) =
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
            execute folder items nextPath options progress itemProgress generationProgress token
        with
        | :? OperationCanceledException -> report runExecutionStatus.Cancelled [||] [||] ""
        | error -> report runExecutionStatus.Failed [||] [||] error.Message

    let executeFilesWithOptions paths options progress itemProgress token = executeFilesUsing paths null options progress itemProgress null token

    /// Preflight the initially checked files, then claim the next checked file only
    /// when a worker is available. A null/empty path ends that worker's queue.
    /// Newly checked files receive the same validation before writing any outputs.
    let executeQueuedFilesWithProgress paths (nextPath: Func<string>) options progress itemProgress generationProgress token =
        if isNull nextPath then nullArg "nextPath"
        executeFilesUsing paths nextPath options progress itemProgress generationProgress token

    let executeQueuedFilesWithOptions paths nextPath options progress itemProgress token =
        executeQueuedFilesWithProgress paths nextPath options progress itemProgress null token

    let executeFolder parametersFolder runId progress token = executeFolderWithOptions parametersFolder runId resultsExecutionOptions.Default progress token
    let executeFile path progress token = executeFileWithOptions path resultsExecutionOptions.Default progress token
    let executeFilesWithStatus paths progress itemProgress token = executeFilesWithOptions paths resultsExecutionOptions.Default progress itemProgress token
    let executeFiles paths progress token = executeFilesWithStatus paths progress null token
