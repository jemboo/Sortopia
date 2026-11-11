namespace Sortopia.FileDb

open System
open Sortopia.Functions
open Sortopia.Workspaces

module FileRunExecution =
    /// Progress and errors belong to the caller; published data is authoritative.
    let execute (workspace: workspace) workspaceRoot value progress cancellation =
        try
            let host = FileRunHost(workspace, workspaceRoot, value, cancellation)
            RunExecution.execute host progress
        with
        | :? OperationCanceledException -> { Status = runExecutionStatus.Cancelled; Setup = [||]; Results = [||]; Error = "" }
        | error -> { Status = runExecutionStatus.Failed; Setup = [||]; Results = [||]; Error = error.Message }
