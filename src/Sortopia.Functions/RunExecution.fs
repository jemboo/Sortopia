namespace Sortopia.Functions

open System
open System.Threading
open FSharp.UMX
open Sortopia.Runs

type runExecutionStatus = Completed = 0 | Failed = 1 | Cancelled = 2

type runFunctionResult = {
    Phase: runExecutionPhase
    FunctionName: string
    SetupSetNumber: int option
    SetNumber: int
    Parameters: parameterSet
    StoredOutputs: storedOutput list
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
    /// Only saved references pass between phases; functions read through their host.
    let execute (factory: IRunHostFactory) (progress: IProgress<runExecutionProgress>) =
        let value = factory.Run
        let cancellation = factory.CancellationToken
        let setup = ResizeArray<runFunctionResult>()
        let results = ResizeArray<runFunctionResult>()
        let mutable context = "Preparing run"
        let report status error =
            { Status = status; Setup = setup.ToArray(); Results = results.ToArray(); Error = error }
        try
            cancellation.ThrowIfCancellationRequested()
            // Resolve both names before generating data or calling either function.
            let resultsFunction =
                ResultsFunctionCatalog.functions |> Map.tryFind value.ResultsFunctionName
                |> Option.defaultWith (fun () -> invalidOp $"Unknown results function '{value.ResultsFunctionName}'.")
            let setupFunction = value.SetupFunctionName |> Option.map (fun name ->
                SetupFunctionCatalog.functions |> Map.tryFind name
                |> Option.defaultWith (fun () -> invalidOp $"Unknown setup function '{name}'."))
            FunctionConfiguration.validateRunInputs value
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
                FunctionValidation.validate resultsFunction sets.Results[i]
            match setupFunction, sets.Setup with
            | Some definition, Some parameters ->
                for i = 0 to parameters.Length - 1 do
                    context <- $"Validating setup set {i + 1} · {value.SetupFunctionName.Value}"
                    cancellation.ThrowIfCancellationRequested()
                    FunctionValidation.validate definition parameters[i]
            | _ -> ()
            let setupCount = sets.Setup |> Option.map Array.length |> Option.defaultValue 0
            let resultsCount = int64 sets.Results.Length * (if setupFunction.IsSome then int64 setupCount else 1L)
            if resultsCount > int64 Array.MaxLength then
                invalidOp "The setup × results combination count is too large. Reduce the parameter spans."
            let total = int64 setupCount + resultsCount
            let notify () =
                if not (isNull progress) then
                    progress.Report { Message = context; Completed = int64 setup.Count + int64 results.Count; Total = total }
            let invoke phase name setupNumber setNumber parameters makeHost functionValue (destination: ResizeArray<_>) =
                cancellation.ThrowIfCancellationRequested()
                let prefix = if phase = runExecutionPhase.Setup then "Setup" else "Results"
                let suffix = setupNumber |> Option.map (fun number -> $" (setup set {number})") |> Option.defaultValue ""
                context <- $"{prefix} set {setNumber}{suffix} · {name}"
                notify ()
                let host: runHost = makeHost ()
                functionValue host
                let database = if phase = runExecutionPhase.Setup then host.SetupDb else host.ResultsDb
                destination.Add { Phase = phase; FunctionName = name; SetupSetNumber = setupNumber
                                  SetNumber = setNumber; Parameters = parameters; StoredOutputs = database.StoredOutputs }
                notify ()
                cancellation.ThrowIfCancellationRequested()
            match setupFunction, sets.Setup with
            | Some functionValue, Some setupSets ->
                let branch = match value with run.BranchRun branch -> branch | _ -> invalidOp "Expected a branch run."
                let sources = branch.SourceProjectIds |> Map.filter (fun dataType _ -> List.contains dataType functionValue.SourceDataTypes)
                for i = 0 to setupSets.Length - 1 do
                    invoke runExecutionPhase.Setup (UMX.untag value.SetupFunctionName.Value) None (i + 1) setupSets[i]
                        (fun () -> factory.ForSetup setupSets[i]) (fun host -> functionValue.Execute host sources) setup
                for batch in setup do
                    for i = 0 to sets.Results.Length - 1 do
                        invoke runExecutionPhase.Results (UMX.untag value.ResultsFunctionName) (Some batch.SetNumber)
                            (i + 1) sets.Results[i] (fun () -> factory.ForResults(sets.Results[i], batch.StoredOutputs)) resultsFunction.Execute results
            | _ ->
                for i = 0 to sets.Results.Length - 1 do
                    invoke runExecutionPhase.Results (UMX.untag value.ResultsFunctionName) None (i + 1) sets.Results[i]
                        (fun () -> factory.ForResults(sets.Results[i], [])) resultsFunction.Execute results
            report runExecutionStatus.Completed ""
        with
        | :? OperationCanceledException when cancellation.IsCancellationRequested -> report runExecutionStatus.Cancelled ""
        | error -> report runExecutionStatus.Failed $"{context}: {error.Message}"
