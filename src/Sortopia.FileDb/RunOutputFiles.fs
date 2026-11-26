namespace Sortopia.FileDb

open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Evaluation.Sgd
open FSharp.UMX

type evolutionOutputPlan = { Snapshots: (int * dataKey) array; Summaries: (int array * dataKey) array }
type evolutionSamples = { Generation: int; Snapshots: int array; SummaryGenerations: int array; PoolCount: int }
type evolutionSummaryHistory = {
    Summary: sorterPoolSummarySet
    Files: (int array * string) array
    MissingGenerations: int array
}

/// One output plan and database for final results, snapshots and summary batches.
module RunOutputFiles =
    let plan (item: runParameters) =
        FileResultsHost.planForRun item.ProjectId item.Results item.Parameters item.SetupInputs item.Reporting item.Sgd

    let keys item = plan item |> Map.toArray |> Array.map (snd >> _.Key)

    let openDatabase folder (item: runParameters) token : IRunDatabase =
        FileRunDatabase(SortopiaFileDb(folder, item.ProjectId), plan item, token)

    let inspect folder (item: runParameters) token =
        OutputPlan.inspect (SortopiaFileDb(folder, item.ProjectId)) (plan item) token

    let private sampling (item: runParameters) (outputs: Map<runDataOutput, plannedOutput>) =
        if item.Reporting.IsNone && item.Sgd.IsNone then { Snapshots = [||]; Summaries = [||] } : evolutionSamplingPlan
        else
            let final = outputs |> Map.toList |> List.map fst |> List.find (fun output -> output.DataType = sortopiaDataType.SorterPoolSet "")
            EvolutionSampling.planForRun item.Parameters final item.Reporting item.Sgd

    /// File identities for report links and inspection; sampling itself has no file access.
    let samples item : evolutionOutputPlan =
        let outputs = plan item
        let samples = sampling item outputs
        { Snapshots = samples.Snapshots |> Array.map (fun (generation, output) -> generation, outputs[output].Key)
          Summaries = samples.Summaries |> Array.map (fun (generations, output) -> generations, outputs[output].Key) }

    let private snapshot item generation =
        (samples item).Snapshots |> Array.tryFind (fst >> (=) generation) |> Option.map snd
        |> Option.defaultWith (fun () -> invalidArg "generation" "This generation is not a configured snapshot.")

    let snapshotPath folder (item: runParameters) generation =
        SortopiaFileDb(folder, item.ProjectId).GetPath (snapshot item generation).Reference

    let temporarySnapshotPath folder (item: runParameters) =
        TemporarySnapshots.path folder (TemporarySnapshots.identity (snapshot item 0) item.Reporting)

    /// Latest published summary for this invocation, ordered by generation rather than file timestamps.
    let latestSummaryPath folder (item: runParameters) (token: System.Threading.CancellationToken) =
        if item.Sgd.IsNone then None else
        let store = SortopiaFileDb(folder, item.ProjectId)
        (samples item).Summaries
        |> Array.sortByDescending (fst >> Array.last)
        |> Array.tryPick (fun (_, key) ->
            token.ThrowIfCancellationRequested()
            let path = store.GetPath key.Reference
            if not (System.IO.File.Exists path) then None else
            let header = (DataEnvelope.readHeader path token).Envelope
            if header.Id <> UMX.untag key.Id then invalidOp "Pool summary identity mismatch."
            Some path)

    /// Combine only this invocation's published batches, including an unfinished run's history.
    /// Missing future batches are expected; missing generations before the latest batch are reported.
    let loadSummaryHistory folder (item: runParameters) (token: System.Threading.CancellationToken) =
        if item.Sgd.IsNone then None else
        let outputs = plan item
        let batches = (sampling item outputs).Summaries |> Array.sortBy (fst >> Array.head)
        let store = SortopiaFileDb(folder, item.ProjectId)
        let database = FileRunDatabase(store, outputs, token) :> IRunDatabase
        let rows = ResizeArray<sgdPoolStatistics>()
        let files = ResizeArray<int array * string>()
        let missing = ResizeArray<int>()
        let mutable testsId = System.Guid.Empty
        for generations, output in batches do
            token.ThrowIfCancellationRequested()
            let key = outputs[output].Key
            let path = store.GetPath key.Reference
            if not (System.IO.File.Exists path) then missing.AddRange generations else
            let summary =
                try
                    match database.Load key.Reference with
                    | sortopiaStorageData.SorterPoolSummarySet summary -> summary
                    | _ -> invalidOp "Expected a pool summary set."
                with
                | :? System.OperationCanceledException -> reraise ()
                | error -> raise (System.IO.InvalidDataException($"Cannot read pool summary '{path}': {error.Message}", error))
            if testsId <> System.Guid.Empty && testsId <> summary.TestsId then
                invalidOp $"Pool summary '{path}' belongs to a different test set."
            testsId <- summary.TestsId
            rows.AddRange summary.Rows
            files.Add(generations, path)
        token.ThrowIfCancellationRequested()
        if rows.Count = 0 then None else
        let combined = { TestsId = testsId; Rows = rows.ToArray() |> Array.sortBy (fun row -> row.Generation, row.PoolIndex) }
                       |> SorterPoolSummarySet.validate
        let last = combined.Rows[combined.Rows.Length - 1].Generation
        Some { Summary = combined; Files = files.ToArray(); MissingGenerations = missing.ToArray() |> Array.filter (fun generation -> generation <= last) }

    /// Read atomic snapshot metadata on request or publication notification, without decoding populations.
    let completedGeneration folder (item: runParameters) (token: System.Threading.CancellationToken) =
        if item.Sgd.IsNone then None else
        let samples = samples item
        let store = SortopiaFileDb(folder, item.ProjectId)
        // Read the replaceable file first: a retained snapshot can retire it concurrently.
        let temporary = TemporarySnapshots.tryGeneration folder (TemporarySnapshots.identity (snd samples.Snapshots[0]) item.Reporting) token
                        |> Option.filter (fun generation -> generation <= SgdRun.target item.Sgd.Value)
        let permanent = samples.Snapshots |> Array.rev |> Array.tryPick (fun (generation, key) ->
            token.ThrowIfCancellationRequested()
            let path = store.GetPath key.Reference
            if not (System.IO.File.Exists path) then None else
            let header = (DataEnvelope.readHeader path token).Envelope
            if header.Id <> UMX.untag key.Id then invalidOp "Snapshot identity mismatch."
            Some generation)
        [permanent; temporary] |> List.choose id |> function [] -> None | values -> Some (List.max values)

    let loadSnapshot folder item generation token =
        let database = openDatabase folder item token
        match database.Load (snapshot item generation).Reference with
        | sortopiaStorageData.SorterPoolSet value -> value
        | _ -> invalidOp "Expected a sampled pool set."

    let loadSamples folder (item: runParameters) token =
        if item.Reporting.IsNone && item.Sgd.IsNone then invalidOp "This parameter combination has no evolution reporting schedule."
        let database = openDatabase folder item token
        let samples = samples item
        let rows = samples.Summaries |> Array.collect (fun (_, key) ->
            match database.Load key.Reference with
            | sortopiaStorageData.SorterPoolSummarySet summary -> summary.Rows
            | _ -> invalidOp "Expected a pool summary set.")
        { Generation = EvolutionSampling.target item.Sgd
          Snapshots = samples.Snapshots |> Array.map fst
          SummaryGenerations = samples.Summaries |> Array.collect fst
          PoolCount = FunctionParameters.required ParamKeys.sorterPoolCountKey item.Parameters }, rows
