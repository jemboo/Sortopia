namespace Sortopia.FileDb

open System
open System.Threading
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces

type runParameterGenerationReport = {
    WorkItemIds: Guid<workItemId> array
    Execution: runExecutionReport
}

type workItemOutputStatus = { Id: Guid<workItemId>; RunId: Guid<runId>; Outputs: outputInspection }

module RunParameterExecution =
    let private report status setup results error : runExecutionReport = { Status = status; Setup = setup; Results = results; Error = error }
    let private notify (progress: IProgress<runExecutionProgress>) message completed total =
        if not (isNull progress) then progress.Report { Message = message; Completed = completed; Total = total }
    let private planWorkItem (value: runParameters) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        FileResultsHost.plan value.ProjectId value.Results value.Parameters value.SetupInputs

    let private selectedItems parametersFolder runId token =
        let items = RunParameterFiles.listWorkItems parametersFolder token
        let selected = items |> Array.filter (fun item -> runId |> Option.forall ((=) item.RunId))
        if selected.Length = 0 then invalidOp "No parameter files match this selection. Generate run parameters or select another folder/run."
        selected

    /// Read-only completion checks are derived from the expected output files.
    let inspectFolder parametersFolder runId (token: CancellationToken) =
        let items = selectedItems parametersFolder runId token
        let folder = RunParameterFiles.projectFolder parametersFolder
        items |> Array.map (fun item ->
            token.ThrowIfCancellationRequested()
            let inspection =
                try
                    let store = SortopiaFileDb(folder, item.ProjectId) :> ISortopiaDb
                    OutputPlan.inspect store (planWorkItem item token) token
                with
                | :? OperationCanceledException -> reraise ()
                | error -> { State = outputState.Invalid; Available = []; ExpectedCount = 0; Error = error.GetBaseException().Message }
            { Id = item.Id; RunId = item.RunId; Outputs = inspection })
    let private verifyInputs folder projectId references (token: CancellationToken) =
        let store = SortopiaFileDb(folder, projectId) :> ISortopiaDb
        for reference in references |> Seq.distinct do
            token.ThrowIfCancellationRequested()
            store.Load(reference, token) |> HostStorage.require |> ignore

    /// Local phase: resolve source projects, execute setup, freeze each results
    /// invocation. Each atomically published parameter file is independently ready.
    let generate (workspace: workspace) workspaceRoot (value: run) progress (token: CancellationToken) =
        let setup = ResizeArray<runFunctionResult>()
        let ids = ResizeArray<Guid<workItemId>>()
        let mutable context = "Preparing run"
        let finish status error = report status (setup.ToArray()) [||] error
        try
            token.ThrowIfCancellationRequested()
            let factory = FileRunHost(workspace, workspaceRoot, value, token)
            let projectFolder = factory.ProjectFolder
            RunParameterStorage.requireProject projectFolder value.ParentProjectId
            let sets = RunParameterSets.create value
            let resultsDefinition = FunctionCatalogs.requireResults value.ResultsFunctionName
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
                let definition = FunctionCatalogs.requireSetup branch.SetupFunctionName
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

    let private execute folder (workItems: runParameters array) progress (token: CancellationToken) =
        let results = ResizeArray<runFunctionResult>()
        let mutable context = "Validating prepared results"
        try
            // Validate all selected work and outputs before writing anything.
            // Complete work does not require its setup payloads to be re-read.
            let store = SortopiaFileDb(folder, workItems[0].ProjectId) :> ISortopiaDb
            let items = workItems |> Array.map (fun item ->
                token.ThrowIfCancellationRequested()
                let plan = planWorkItem item token
                let inspection = OutputPlan.inspect store plan token
                if inspection.State = outputState.Invalid then invalidOp $"Work item {item.Id}: {inspection.Error}"
                item, plan, inspection)
            verifyInputs folder workItems[0].ProjectId
                (items |> Seq.filter (fun (_, _, inspection) -> inspection.State <> outputState.Complete)
                       |> Seq.collect (fun (item, _, _) -> item.SetupInputs)) token
            for item, plan, _ in items do
                token.ThrowIfCancellationRequested()
                context <- $"Results work item {item.Id} · {item.Results.FunctionName}"
                notify progress context (int64 results.Count) (int64 items.Length)
                // Earlier work items (or another process) may have produced these
                // same outputs since preflight. Re-check before invoking code.
                let inspection = OutputPlan.inspect store plan token
                let outputs =
                    match inspection.State with
                    | outputState.Invalid -> invalidOp inspection.Error
                    | outputState.Complete ->
                        context <- $"Skipping complete work item {item.Id} · outputs already available"
                        inspection.Available
                    | _ ->
                        let inputs = item.SetupInputs |> List.map (fun reference -> { Reference = reference; Reused = true })
                        let host = FileResultsHost.create folder item.ProjectId item.Results item.Parameters inputs token
                        (FunctionCatalogs.requireResults item.Results.FunctionName).Execute host
                        host.ResultsDb.StoredOutputs
                results.Add { Phase = runExecutionPhase.Results; FunctionName = string item.Results.FunctionName
                              SetupSetNumber = item.SetupSetNumber; SetNumber = item.SetNumber; Parameters = item.Parameters; StoredOutputs = outputs }
                notify progress context (int64 results.Count) (int64 items.Length)
            report runExecutionStatus.Completed [||] (results.ToArray()) ""
        with
        | :? OperationCanceledException -> report runExecutionStatus.Cancelled [||] (results.ToArray()) ""
        | error -> report runExecutionStatus.Failed [||] (results.ToArray()) $"{context}: {error.GetBaseException().Message}"

    /// Execute a snapshot of the folder's work, optionally restricted to one run.
    let executeFolder parametersFolder runId progress token =
        try
            let items = selectedItems parametersFolder runId token
            execute (RunParameterFiles.projectFolder parametersFolder) items progress token
        with
        | :? OperationCanceledException -> report runExecutionStatus.Cancelled [||] [||] ""
        | error -> report runExecutionStatus.Failed [||] [||] error.Message

    /// Schedulers may execute one parameter file without a manifest or workspace.
    let executeFile path progress (token: CancellationToken) =
        try
            token.ThrowIfCancellationRequested()
            let item = RunParameterFiles.loadFile path
            let folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath path))
            execute folder [| item |] progress token
        with
        | :? OperationCanceledException -> report runExecutionStatus.Cancelled [||] [||] ""
        | error -> report runExecutionStatus.Failed [||] [||] error.Message
