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
    let internal saveReceipt folder (value: run) started (report: runExecutionReport) =
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
            let host = FileRunHost(workspace, workspaceRoot, value, cancellation)
            let folder = host.ProjectFolder
            let report = RunExecution.execute host progress
            try
                saveReceipt folder value started report
                report
            with error ->
                { report with Status = runExecutionStatus.Failed
                              Error = report.Error + $" Execution receipt could not be saved: {error.Message}. Published data has been retained." }
        with
        | :? OperationCanceledException -> { Status = runExecutionStatus.Cancelled; Setup = [||]; Results = [||]; Error = "" }
        | error -> { Status = runExecutionStatus.Failed; Setup = [||]; Results = [||]; Error = error.Message }
