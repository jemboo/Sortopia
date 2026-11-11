namespace Sortopia.FileDb

open System
open System.Threading
open MessagePack
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces
open Sortopia.Core.Serialization

type runPreparationReport = {
    Preparation: preparedRun option
    Execution: runExecutionReport
}

[<MessagePackObject>]
type preparedExecutionReceiptDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Id: Guid
    [<Key(2)>] PreparationId: Guid
    [<Key(3)>] WorkItemId: Guid
    [<Key(4)>] RunId: Guid
    [<Key(5)>] ProjectId: Guid
    [<Key(6)>] StartedUtc: DateTime
    [<Key(7)>] FinishedUtc: DateTime
    [<Key(8)>] Status: string
    [<Key(9)>] Error: string
    [<Key(10)>] Outputs: dataReferenceDto array
}

module PreparedRunExecution =
    let private report status setup results error : runExecutionReport = { Status = status; Setup = setup; Results = results; Error = error }
    let private notify (progress: IProgress<runExecutionProgress>) message completed total =
        if not (isNull progress) then progress.Report { Message = message; Completed = completed; Total = total }
    let private validateWorkItem folder (value: runParameters) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        let outputs = value.SetupInputs |> List.map (fun reference -> { Reference = reference; Reused = true })
        let host = FileResultsHost.create folder value.ProjectId value.Results value.Parameters outputs token
        let definition = FunctionCatalogs.requireResults value.Results.FunctionName
        if (value.SetupInputs |> List.map (fun reference -> reference.DataType) |> List.sort) <> List.sort definition.SourceDataTypes then
            invalidOp "Prepared setup inputs must match the results function's required inputs exactly."
        host
    let private verifyInputs folder projectId references (token: CancellationToken) =
        let store = SortopiaFileDb(folder, projectId) :> ISortopiaDb
        for reference in references |> Seq.distinct do
            token.ThrowIfCancellationRequested()
            store.Load(reference, token) |> HostStorage.require |> ignore

    /// Local phase: resolve source projects, execute setup, freeze each results
    /// invocation, then publish a manifest. Failure never publishes a ready batch.
    let prepare (workspace: workspace) workspaceRoot (value: run) progress (token: CancellationToken) =
        let started = DateTime.UtcNow
        let setup = ResizeArray<runFunctionResult>()
        let mutable folder = None
        let mutable context = "Preparing run"
        let finish status error = report status (setup.ToArray()) [||] error
        try
            token.ThrowIfCancellationRequested()
            let factory = FileRunHost(workspace, workspaceRoot, value, token)
            let projectFolder = factory.ProjectFolder
            folder <- Some projectFolder
            PreparedStorage.requireProject projectFolder value.ParentProjectId
            let sets = RunParameterSets.create value
            let resultsDefinition = FunctionCatalogs.requireResults value.ResultsFunctionName
            let config = match value with run.RootRun root -> root.ProjectConfiguration.Results | run.BranchRun branch -> branch.ProjectConfiguration.Results
            FunctionConfiguration.validateResults config
            let setupCount = sets.Setup |> Option.map Array.length |> Option.defaultValue 0
            let resultsCount = int64 sets.Results.Length * (if sets.Setup.IsSome then int64 setupCount else 1L)
            if resultsCount = 0L || resultsCount > int64 Array.MaxLength then invalidOp "The run must generate a nonempty, supported number of results work items. Reduce the parameter spans if the product is too large."
            for parameters in sets.Results do
                token.ThrowIfCancellationRequested()
                FunctionParameters.validate resultsDefinition.RequiredParameters parameters
            match value, sets.Setup with
            | run.BranchRun branch, Some parameters ->
                let definition = FunctionCatalogs.requireSetup branch.SetupFunctionName
                FunctionConfiguration.validateSetup true branch.ProjectConfiguration.Setup
                for set in parameters do
                    token.ThrowIfCancellationRequested()
                    FunctionParameters.validate definition.RequiredParameters set
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
            let ids = ResizeArray<Guid<workItemId>>()
            for setupNumber, inputs in batches do
                for i = 0 to sets.Results.Length - 1 do
                    token.ThrowIfCancellationRequested()
                    context <- $"Saving results work item {ids.Count + 1}"
                    notify progress context (int64 setupCount + int64 ids.Count) (int64 setupCount + resultsCount)
                    let item = {
                        Id = UMX.tag Guid.Empty; ProjectId = value.ParentProjectId; RunId = value.Id
                        Results = { config with ParameterDefaults = [] }; Parameters = sets.Results[i]
                        SetupInputs = inputs; SetNumber = i + 1; SetupSetNumber = setupNumber }
                    validateWorkItem projectFolder item token |> ignore
                    ids.Add(PreparedRunFiles.saveWorkItem projectFolder item token)
            let ready = { Id = UMX.tag (Guid.NewGuid()); ProjectId = value.ParentProjectId; RunId = value.Id; RunName = string value.RunName
                          CreatedUtc = DateTime.UtcNow; SetupSetCount = setupCount; WorkItemIds = ids.ToArray() }
            let completed = finish runExecutionStatus.Completed ""
            FileRunExecution.saveReceipt projectFolder value started completed
            PreparedRunFiles.savePreparation projectFolder ready token
            notify progress $"Prepared {ids.Count} results work items" (int64 setupCount + resultsCount) (int64 setupCount + resultsCount)
            { Preparation = Some ready; Execution = completed }
        with error ->
            let status, message =
                match error with
                | :? OperationCanceledException -> runExecutionStatus.Cancelled, ""
                | _ -> runExecutionStatus.Failed, $"{context}: {error.GetBaseException().Message}"
            let mutable failed = finish status message
            match folder with
            | Some directory ->
                try FileRunExecution.saveReceipt directory value started failed
                with receiptError -> failed <- { failed with Status = runExecutionStatus.Failed; Error = failed.Error + $" Preparation receipt could not be saved: {receiptError.Message}" }
            | None -> ()
            { Preparation = None; Execution = failed }

    let private saveReceipt folder (preparation: preparedRun) (item: runParameters) started status error outputs =
        let dto: preparedExecutionReceiptDto = {
            Version = 1; Id = Guid.NewGuid(); PreparationId = UMX.untag preparation.Id; WorkItemId = UMX.untag item.Id
            RunId = UMX.untag item.RunId; ProjectId = UMX.untag item.ProjectId; StartedUtc = started; FinishedUtc = DateTime.UtcNow
            Status = string status; Error = error
            Outputs = outputs |> List.map (fun output -> { PreparedCodec.reference output.Reference with Reused = output.Reused }) |> List.toArray }
        // Even a cancelled attempt gets a durable receipt. Its immutable inputs
        // remain unchanged, and retry safely reuses any data already published.
        PreparedStorage.publish folder "executions" dto.Id "prepared-execution" (MessagePackSetup.serialize dto) CancellationToken.None

    let private execute folder (preparation: preparedRun) ids progress (token: CancellationToken) =
        let results = ResizeArray<runFunctionResult>()
        let mutable context = "Validating prepared results"
        try
            // Preflight the entire selection before writing any results. This also
            // catches an incomplete copy, version mismatch or damaged input early.
            let published = Set.ofArray preparation.WorkItemIds
            let items = ids |> Array.map (fun id ->
                token.ThrowIfCancellationRequested()
                if not (Set.contains id published) then invalidOp "The work item is not in this published preparation."
                let item = PreparedRunFiles.loadWorkItem folder id
                if item.ProjectId <> preparation.ProjectId || item.RunId <> preparation.RunId then invalidOp "Work item identity does not match its preparation."
                match item.SetupSetNumber with
                | Some number when number > preparation.SetupSetCount -> invalidOp "Work item setup number is outside its preparation."
                | None when preparation.SetupSetCount <> 0 -> invalidOp "A branch preparation requires a setup set number on every work item."
                | _ -> ()
                validateWorkItem folder item token |> ignore
                item)
            verifyInputs folder preparation.ProjectId (items |> Seq.collect (fun item -> item.SetupInputs)) token
            for item in items do
                token.ThrowIfCancellationRequested()
                context <- $"Results work item {item.Id} · {item.Results.FunctionName}"
                notify progress context (int64 results.Count) (int64 items.Length)
                let started = DateTime.UtcNow
                let mutable outputs = []
                let status, error =
                    try
                        let host = validateWorkItem folder item token
                        try
                            (FunctionCatalogs.requireResults item.Results.FunctionName).Execute host
                            runExecutionStatus.Completed, ""
                        finally outputs <- host.ResultsDb.StoredOutputs
                    with
                    | :? OperationCanceledException -> runExecutionStatus.Cancelled, ""
                    | error -> runExecutionStatus.Failed, error.GetBaseException().Message
                try saveReceipt folder preparation item started status error outputs
                with receiptError -> invalidOp $"Results may have been published, but the execution receipt could not be saved: {receiptError.Message}"
                if status = runExecutionStatus.Cancelled then raise (OperationCanceledException(token))
                if status = runExecutionStatus.Failed then invalidOp error
                results.Add { Phase = runExecutionPhase.Results; FunctionName = string item.Results.FunctionName
                              SetupSetNumber = item.SetupSetNumber; SetNumber = item.SetNumber; Parameters = item.Parameters; StoredOutputs = outputs }
                notify progress context (int64 results.Count) (int64 items.Length)
            report runExecutionStatus.Completed [||] (results.ToArray()) ""
        with
        | :? OperationCanceledException -> report runExecutionStatus.Cancelled [||] (results.ToArray()) ""
        | error -> report runExecutionStatus.Failed [||] (results.ToArray()) $"{context}: {error.GetBaseException().Message}"

    /// Run all work items from one published preparation; never invokes setup.
    let executePreparation folder id progress token =
        try
            let preparation = PreparedRunFiles.loadPreparation folder id
            execute folder preparation preparation.WorkItemIds progress token
        with error -> report runExecutionStatus.Failed [||] [||] error.Message

    /// A scheduler may select one immutable work item by its file ID.
    let executeWorkItem folder id progress token =
        try
            let preparation = PreparedRunFiles.listPreparations folder |> Array.tryFind (fun value -> Array.contains id value.WorkItemIds)
                              |> Option.defaultWith (fun () -> invalidOp "No completed preparation includes this work item. Prepare the run locally and copy the complete project folder.")
            execute folder preparation [| id |] progress token
        with error -> report runExecutionStatus.Failed [||] [||] error.Message
