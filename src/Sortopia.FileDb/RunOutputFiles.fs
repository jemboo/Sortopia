namespace Sortopia.FileDb

open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Evaluation.Sgd

type evolutionOutputPlan = { Snapshots: (int * dataKey) array; Summaries: (int array * dataKey) array }
type evolutionSamples = { Generation: int; Snapshots: int array; SummaryGenerations: int array; PoolCount: int }

/// One output plan and database for final results, snapshots and summary batches.
module RunOutputFiles =
    let plan (item: runParameters) =
        FileResultsHost.planWithReporting item.ProjectId item.Results item.Parameters item.SetupInputs item.Reporting

    let keys item = plan item |> Map.toArray |> Array.map (snd >> _.Key)

    let openDatabase folder (item: runParameters) token : IRunDatabase =
        FileRunDatabase(SortopiaFileDb(folder, item.ProjectId), plan item, token)

    let inspect folder (item: runParameters) token =
        OutputPlan.inspect (SortopiaFileDb(folder, item.ProjectId)) (plan item) token

    let private sampling (item: runParameters) (outputs: Map<runDataOutput, plannedOutput>) =
        if item.Reporting.IsNone then { Snapshots = [||]; Summaries = [||] } : evolutionSamplingPlan
        else
            let final = outputs |> Map.toList |> List.map fst |> List.find (fun output -> output.DataType = sortopiaDataType.SorterPoolSet "")
            EvolutionSampling.plan item.Parameters final item.Reporting

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

    let loadSnapshot folder item generation token =
        let database = openDatabase folder item token
        match database.Load (snapshot item generation).Reference with
        | sortopiaStorageData.SorterPoolSet value -> value
        | _ -> invalidOp "Expected a sampled pool set."

    let loadSamples folder (item: runParameters) token =
        if item.Reporting.IsNone then invalidOp "This parameter combination has no evolution reporting schedule."
        let database = openDatabase folder item token
        let samples = samples item
        let rows = samples.Summaries |> Array.collect (fun (_, key) ->
            match database.Load key.Reference with
            | sortopiaStorageData.SorterPoolSummarySet summary -> summary.Rows
            | _ -> invalidOp "Expected a pool summary set.")
        { Generation = FunctionParameters.required ParamKeys.generationCountKey item.Parameters
          Snapshots = samples.Snapshots |> Array.map fst
          SummaryGenerations = samples.Summaries |> Array.collect fst
          PoolCount = FunctionParameters.required ParamKeys.sorterPoolCountKey item.Parameters }, rows
