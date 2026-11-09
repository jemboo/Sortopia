namespace Sortopia.Functions

open System
open System.Threading
open FSharp.UMX
open Sortopia.Runs

type runExecutionPhase = Setup = 0 | Results = 1
type runExecutionStatus = Completed = 0 | Failed = 1 | Cancelled = 2

type runFunctionResult = {
    Phase: runExecutionPhase
    FunctionName: string
    SetupSetNumber: int option
    SetNumber: int
    Parameters: parameterSet
    Outputs: functionData list
}

type runExecutionProgress = {
    Message: string
    Completed: int64
    Total: int64
}

type runExecutionReport = {
    Status: runExecutionStatus
    Setup: runFunctionResult array
    Results: runFunctionResult array
    Error: string
}

module RunExecution =
    /// Execute all setup sets before any results sets. Each setup batch is paired
    /// with every results set; batches are never combined with each other.
    /// Inputs and outputs remain typed and in memory. Cancellation is checked
    /// between synchronous catalog calls, which cannot themselves be interrupted.
    let execute (value: run) (inputs: functionData list)
                (progress: IProgress<runExecutionProgress>) (cancellation: CancellationToken) =
        let setup = ResizeArray<runFunctionResult>()
        let results = ResizeArray<runFunctionResult>()
        let mutable context = "Preparing run"
        let report status error =
            { Status = status; Setup = setup.ToArray(); Results = results.ToArray(); Error = error }
        try
            cancellation.ThrowIfCancellationRequested()
            // Resolve both names before generating data or calling either function.
            let resultsFunction =
                FunctionCatalogs.resultsFunctionCatalog |> Map.tryFind value.ResultsFunctionName
                |> Option.defaultWith (fun () -> invalidOp $"Unknown results function '{value.ResultsFunctionName}'.")
            let setupFunction = value.SetupFunctionName |> Option.map (fun name ->
                FunctionCatalogs.setupFunctionCatalog |> Map.tryFind name
                |> Option.defaultWith (fun () -> invalidOp $"Unknown setup function '{name}'."))
            let sets = RunParameterSets.create value
            let setupCount = sets.Setup |> Option.map Array.length |> Option.defaultValue 0
            let resultsCount = int64 sets.Results.Length * (if setupFunction.IsSome then int64 setupCount else 1L)
            if resultsCount > int64 Array.MaxLength then
                invalidOp "The setup × results combination count is too large. Reduce the parameter spans."
            let total = int64 setupCount + resultsCount
            let notify () =
                if not (isNull progress) then
                    progress.Report { Message = context; Completed = int64 setup.Count + int64 results.Count; Total = total }
            let invoke phase name setupNumber setNumber parameters functionInputs functionValue (destination: ResizeArray<_>) =
                cancellation.ThrowIfCancellationRequested()
                let prefix = if phase = runExecutionPhase.Setup then "Setup" else "Results"
                let suffix = setupNumber |> Option.map (fun number -> $" (setup set {number})") |> Option.defaultValue ""
                context <- $"{prefix} set {setNumber}{suffix} · {name}"
                notify ()
                match functionValue parameters functionInputs with
                | Error message -> invalidOp message
                | Ok outputs ->
                    destination.Add { Phase = phase; FunctionName = name; SetupSetNumber = setupNumber
                                      SetNumber = setNumber; Parameters = parameters; Outputs = outputs }
                    notify ()
                cancellation.ThrowIfCancellationRequested()
            match setupFunction, sets.Setup with
            | Some functionValue, Some setupSets ->
                for i = 0 to setupSets.Length - 1 do
                    invoke runExecutionPhase.Setup (UMX.untag value.SetupFunctionName.Value) None (i + 1) setupSets[i] inputs functionValue setup
                for batch in setup do
                    for i = 0 to sets.Results.Length - 1 do
                        invoke runExecutionPhase.Results (UMX.untag value.ResultsFunctionName) (Some batch.SetNumber)
                            (i + 1) sets.Results[i] (inputs @ batch.Outputs) resultsFunction results
            | _ ->
                for i = 0 to sets.Results.Length - 1 do
                    invoke runExecutionPhase.Results (UMX.untag value.ResultsFunctionName) None (i + 1) sets.Results[i] inputs resultsFunction results
            report runExecutionStatus.Completed ""
        with
        | :? OperationCanceledException when cancellation.IsCancellationRequested -> report runExecutionStatus.Cancelled ""
        | error -> report runExecutionStatus.Failed $"{context}: {error.Message}"
