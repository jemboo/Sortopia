namespace Sortopia.FileDb

open System
open System.IO
open System.Threading
open MessagePack
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces
open Sortopia.Core.Serialization

/// One setup parameter set. Each catalog function asks for its own output before
/// generating it; cached outputs never invoke their factories, including partial hits.
type FileSetupDataContext(store: ISortopiaDb, branch: branchRun, parameters: parameterSet,
                          sources: storedData list, token: CancellationToken) =
    let completed = ResizeArray<storedOutput>()
    member _.StoredOutputs = completed |> Seq.toList
    interface ISetupDataContext with
        member _.GetOrCreate(kind, sourceKinds, factory) =
            try
                let output =
                    match branch.SetupDataTypes |> List.filter (fun entry -> SortopiaDataType.kindName entry.DataType = kind) with
                    | [entry] -> entry
                    | [] -> invalidOp $"The selected setup function must declare a '{kind}' output."
                    | _ -> invalidOp $"This catalog function needs exactly one configured '{kind}' setup output."
                let dependencies = sourceKinds |> List.map (fun sourceKind ->
                    match sources |> List.filter (fun source -> SortopiaDataType.kindName source.Reference.DataType = sourceKind) with
                    | [source] -> source.Reference
                    | _ -> invalidOp $"Expected one source datum of kind '{sourceKind}'.")
                let version = FunctionCatalogs.setupFunctionVersions[branch.SetupFunctionName]
                let key = DataKey.create branch.ParentProjectId (string branch.SetupDatabaseName) true (string branch.SetupFunctionName) version
                            output.DataType output.QueryParameterKeys parameters dependencies
                match store.GetOrCreate(key, factory, token) with
                | Error message -> Error message
                | Ok (value, reused) ->
                    completed.Add { Reference = value.Reference; Reused = reused }
                    Ok value.Value
            with
            | :? OperationCanceledException -> reraise ()
            | error -> Error error.Message

[<MessagePackObject>]
type dataReferenceDto = {
    [<Key(0)>] ProjectId: Guid
    [<Key(1)>] Id: Guid
    [<Key(2)>] QueryId: Guid
    [<Key(3)>] IsSetup: bool
    [<Key(4)>] Kind: string
    [<Key(5)>] Label: string
    [<Key(6)>] Reused: bool
}

[<MessagePackObject>]
type invocationReceiptDto = {
    [<Key(0)>] Phase: string
    [<Key(1)>] SetNumber: int
    [<Key(2)>] SetupSetNumber: int option
    [<Key(3)>] FunctionName: string
    [<Key(4)>] Parameters: (string * int * string) array
    [<Key(5)>] Outputs: dataReferenceDto array
}

[<MessagePackObject>]
type executionReceiptDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Id: Guid
    [<Key(2)>] RunId: Guid
    [<Key(3)>] ProjectId: Guid
    [<Key(4)>] StartedUtc: DateTime
    [<Key(5)>] FinishedUtc: DateTime
    [<Key(6)>] Status: string
    [<Key(7)>] Error: string
    [<Key(8)>] Invocations: invocationReceiptDto array
}

module FileRunExecution =
    let private require = function Ok value -> value | Error message -> invalidOp message
    let private validateTypes types =
        if List.isEmpty types then invalidOp "Declare the data types produced by this function before executing."
        let kinds = types |> List.map SortopiaDataType.kindName
        if (Set.ofList kinds).Count <> kinds.Length then invalidOp "The current catalog functions require one configured output per data kind."
        for kind in kinds do
            if not (DataCodec.supported kind) then invalidOp $"Storage for data type '{kind}' is not implemented."

    let private access workspace root store token = {
        new IRunDataAccess with
            member _.Setup(branch, parameters, _, functionValue) =
                try
                    validateTypes branch.SetupSortopiaDataTypes
                    let sources = branch.SetupDataTypes |> List.filter (fun entry -> FunctionConfiguration.requiresSource branch.SetupFunctionName entry.DataType)
                                  |> List.map (fun entry -> WorkspaceData.loadSource workspace root entry parameters token)
                    let context = FileSetupDataContext(store, branch, parameters, sources, token)
                    let values = functionValue context parameters (sources |> List.map (fun source -> source.Value)) |> require
                    let stored = context.StoredOutputs
                    if stored.Length <> branch.SetupDataTypes.Length
                       || (stored |> List.map (fun output -> output.Reference.DataType) |> Set.ofList) <> Set.ofList branch.SetupSortopiaDataTypes then
                        invalidOp "The setup function did not supply every configured setup output through GetOrCreate."
                    if (values |> List.map FunctionData.kind |> List.sort) <>
                       (stored |> List.map (fun output -> SortopiaDataType.kindName output.Reference.DataType) |> List.sort) then
                        invalidOp "The setup function returned data that does not match its saved outputs."
                    Ok { Values = values; Stored = stored }
                with
                | :? OperationCanceledException -> reraise ()
                | error -> Error error.Message

            member _.Results(value, parameters, setup, inputs, functionValue) =
                try
                    validateTypes value.ResultsSortopiaDataTypes
                    let requiredInputs = FunctionCatalogs.resultsFunctionCatalog[value.ResultsFunctionName].SourceDataTypes
                    let dependencies =
                        setup |> Option.map (fun call ->
                            call.StoredOutputs |> List.map (fun output -> output.Reference)
                            |> List.filter (fun reference -> List.contains reference.DataType requiredInputs))
                        |> Option.defaultValue []
                    if dependencies.Length <> requiredInputs.Length then invalidOp "The setup outputs do not provide the results function's required input data types."
                    let version = FunctionCatalogs.resultsFunctionVersions[value.ResultsFunctionName]
                    let keys = value.ResultsDataTypes |> List.map (fun output ->
                        DataKey.create value.ParentProjectId (string value.ResultsDatabaseName) false (string value.ResultsFunctionName) version
                            output.DataType output.QueryParameterKeys parameters dependencies)
                    // The results signature remains a simple independent function.
                    // Generate its batch at most once, and only if an output is missing.
                    let generated = lazy (
                        let outputs = functionValue parameters inputs |> require
                        if (outputs |> List.map FunctionData.kind |> List.sort) <>
                           (value.ResultsSortopiaDataTypes |> List.map SortopiaDataType.kindName |> List.sort) then
                            invalidOp "The results function's outputs do not match the project's declared data types."
                        outputs)
                    let outputs = keys |> List.map (fun key ->
                        let factory () =
                            generated.Value |> List.find (fun output -> FunctionData.kind output = SortopiaDataType.kindName key.Reference.DataType) |> Ok
                        store.GetOrCreate(key, factory, token) |> require)
                    Ok { Values = outputs |> List.map (fun (data, _) -> data.Value)
                         Stored = outputs |> List.map (fun (data, reused) -> { Reference = data.Reference; Reused = reused }) }
                with
                | :? OperationCanceledException -> reraise ()
                | error -> Error error.Message }

    let private saveReceipt folder (value: run) started (report: runExecutionReport) =
        let references (output: storedOutput) = {
            ProjectId = UMX.untag output.Reference.ProjectId; Id = UMX.untag output.Reference.Id; QueryId = UMX.untag output.Reference.QueryId
            IsSetup = output.Reference.IsSetup; Kind = SortopiaDataType.kindName output.Reference.DataType
            Label = SortopiaDataType.text output.Reference.DataType; Reused = output.Reused }
        let invocation (call: runFunctionResult) = {
            Phase = string call.Phase; SetNumber = call.SetNumber; SetupSetNumber = call.SetupSetNumber; FunctionName = call.FunctionName
            Parameters = call.Parameters.ParamMap |> Map.toArray |> Array.map (fun (_, binding) -> string binding.KeyName, int binding.ValueType, binding.FormattedValue)
            Outputs = call.StoredOutputs |> List.map references |> List.toArray }
        let receipt = {
            Version = 1; Id = Guid.NewGuid(); RunId = UMX.untag value.Id; ProjectId = UMX.untag value.ParentProjectId
            StartedUtc = started; FinishedUtc = DateTime.UtcNow; Status = string report.Status; Error = report.Error
            Invocations = Array.append report.Setup report.Results |> Array.map invocation }
        let directory = Path.Combine(folder, "executions")
        if Directory.Exists directory && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint) then
            invalidOp "Execution storage does not follow filesystem links."
        Directory.CreateDirectory directory |> ignore
        let path = Path.Combine(directory, string receipt.Id + ".msgpack")
        let temporary = path + ".tmp"
        try
            do
                use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                stream.Write(MessagePackSetup.serialize receipt)
                stream.Flush(true)
            File.Move(temporary, path, false)
        finally
            if File.Exists temporary then File.Delete temporary

    /// Workspace lookup and file I/O belong at the execution boundary. Catalog
    /// maps and the shared execution loop have no filesystem dependencies.
    let execute (workspace: workspace) workspaceRoot (value: run) progress cancellation =
        let started = DateTime.UtcNow
        try
            let parent = workspace.FindProject value.ParentProjectId
                         |> Option.defaultWith (fun () -> invalidOp "The run's parent project was not found.")
            if not (parent.Runs |> Array.exists (fun saved -> saved.Id = value.Id && saved = value)) then
                invalidOp "Execute requires a saved run from the supplied workspace."
            let folder = WorkspaceData.projectFolder workspace workspaceRoot parent.Id
            let store = SortopiaFileDb(folder, parent.Id) :> ISortopiaDb
            let report = RunExecution.executeWith (access workspace workspaceRoot store cancellation) value [] progress cancellation
            try
                saveReceipt folder value started report
                report
            with error ->
                { report with Status = runExecutionStatus.Failed
                              Error = report.Error + $" Execution receipt could not be saved: {error.Message}. Published data has been retained." }
        with
        | :? OperationCanceledException -> { Status = runExecutionStatus.Cancelled; Setup = [||]; Results = [||]; Error = "" }
        | error -> { Status = runExecutionStatus.Failed; Setup = [||]; Results = [||]; Error = error.Message }
