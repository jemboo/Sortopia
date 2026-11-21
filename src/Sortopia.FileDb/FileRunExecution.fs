namespace Sortopia.FileDb

open System
open Sortopia.Functions
open Sortopia.Workspaces

module FileRunExecution =
    /// Progress and errors belong to the caller; published data is authoritative.
    let execute (workspace: workspace) workspaceRoot (value: Sortopia.Runs.run) progress cancellation =
        try
            if value.Reporting.IsSome || value.Sgd.IsSome then
                let prepared = RunParameterExecution.generate workspace workspaceRoot value progress cancellation
                if prepared.Execution.Status <> runExecutionStatus.Completed then prepared.Execution else
                let folder = WorkspaceData.projectFolder workspace workspaceRoot value.ParentProjectId
                let paths = prepared.WorkItemIds |> Array.map (fun id -> System.IO.Path.Combine(folder, "runParameters", string id + ".msgpack"))
                let executed = RunParameterExecution.executeFiles paths progress cancellation
                { executed with Setup = prepared.Execution.Setup }
            else
                let host = FileRunHost(workspace, workspaceRoot, value, cancellation)
                RunExecution.execute host progress
        with
        | :? OperationCanceledException -> { Status = runExecutionStatus.Cancelled; Setup = [||]; Results = [||]; Error = "" }
        | error -> { Status = runExecutionStatus.Failed; Setup = [||]; Results = [||]; Error = error.Message }
