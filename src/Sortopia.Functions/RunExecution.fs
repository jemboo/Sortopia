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
    StoredOutputs: storedOutput list
}

type functionOutput = { Values: functionData list; Stored: storedOutput list }

/// Execution policy is separate from the function catalog and loop.
type IRunDataAccess =
    abstract Setup: branchRun * parameterSet * functionData list * setupFunction -> Result<functionOutput, string>
    abstract Results: run * parameterSet * runFunctionResult option * functionData list * resultsFunction -> Result<functionOutput, string>

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
    let executeWith (data: IRunDataAccess) (value: run) (inputs: functionData list)
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
            match value with
            | run.RootRun root -> FunctionConfiguration.validateResults root.ProjectConfiguration.Results
            | run.BranchRun branch ->
                FunctionConfiguration.validateResults branch.ProjectConfiguration.Results
                FunctionConfiguration.validateSetup true branch.ProjectConfiguration.Setup
            // Validate both phases before generating or reusing any data.
            for i = 0 to sets.Results.Length - 1 do
                context <- $"Validating results set {i + 1} · {value.ResultsFunctionName}"
                cancellation.ThrowIfCancellationRequested()
                FunctionParameters.validate resultsFunction.RequiredParameters sets.Results[i]
            match setupFunction, sets.Setup with
            | Some definition, Some parameters ->
                for i = 0 to parameters.Length - 1 do
                    context <- $"Validating setup set {i + 1} · {value.SetupFunctionName.Value}"
                    cancellation.ThrowIfCancellationRequested()
                    FunctionParameters.validate definition.RequiredParameters parameters[i]
            | _ -> ()
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
                                      SetNumber = setNumber; Parameters = parameters; Outputs = outputs.Values; StoredOutputs = outputs.Stored }
                    notify ()
                cancellation.ThrowIfCancellationRequested()
            match setupFunction, sets.Setup with
            | Some functionValue, Some setupSets ->
                let branch = match value with run.BranchRun branch -> branch | _ -> invalidOp "Expected a branch run."
                for i = 0 to setupSets.Length - 1 do
                    invoke runExecutionPhase.Setup (UMX.untag value.SetupFunctionName.Value) None (i + 1) setupSets[i] inputs
                        (fun parameters inputs -> data.Setup(branch, parameters, inputs, functionValue.Execute)) setup
                for batch in setup do
                    for i = 0 to sets.Results.Length - 1 do
                        invoke runExecutionPhase.Results (UMX.untag value.ResultsFunctionName) (Some batch.SetNumber)
                            (i + 1) sets.Results[i] (inputs @ batch.Outputs)
                            (fun parameters inputs -> data.Results(value, parameters, Some batch, inputs, resultsFunction.Execute)) results
            | _ ->
                for i = 0 to sets.Results.Length - 1 do
                    invoke runExecutionPhase.Results (UMX.untag value.ResultsFunctionName) None (i + 1) sets.Results[i] inputs
                        (fun parameters inputs -> data.Results(value, parameters, None, inputs, resultsFunction.Execute)) results
            report runExecutionStatus.Completed ""
        with
        | :? OperationCanceledException when cancellation.IsCancellationRequested -> report runExecutionStatus.Cancelled ""
        | error -> report runExecutionStatus.Failed $"{context}: {error.Message}"

    let execute value inputs progress cancellation =
        let transient = {
            new IRunDataAccess with
                member _.Setup(_, parameters, inputs, functionValue) =
                    functionValue SetupDataContext.transient parameters inputs |> Result.map (fun outputs -> { Values = outputs; Stored = [] })
                member _.Results(_, parameters, _, inputs, functionValue) =
                    functionValue parameters inputs |> Result.map (fun outputs -> { Values = outputs; Stored = [] }) }
        executeWith transient value inputs progress cancellation
