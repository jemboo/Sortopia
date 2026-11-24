namespace Sortopia.Functions

open System
open System.Threading
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Evaluation
open Sortopia.Evaluation.Sgd

type evolutionSamplingPlan = {
    Snapshots: (int * runDataOutput) array
    Summaries: (int array * runDataOutput) array
}

/// Sampling policy and output descriptions, independent of any storage backend.
module EvolutionSampling =
    let requireControls sgd =
        sgd |> Option.defaultWith (fun () -> invalidArg "sgd" "Set SnapshotInterval and SnapshotCount on the SGD run.")
        |> SgdRun.validate

    let target sgd = requireControls sgd |> SgdRun.target

    let planForRun (parameters: parameterSet) (basis: runDataOutput) reporting sgd =
        let controls = requireControls sgd
        let final = SgdRun.target controls
        let snapshots = SgdReporting.samples $"Constant({controls.SnapshotInterval})" final |> Seq.toArray
        let summarySchedule = reporting |> Option.map _.SummarySchedule |> Option.defaultValue "Constant(1)"
        let summaries = SgdReporting.samples summarySchedule final |> Seq.toArray
        let output dataType bindings =
            let query = bindings |> List.fold (fun (p: parameterSet) binding -> p.WithBinding binding) parameters
            RunDataOutput.create dataType (query.ParamMap |> Map.toList |> List.map fst) query basis.Dependencies
        // The target is deliberately absent from identities so a run can be extended.
        let snapshotOutputs = snapshots |> Array.map (fun generation ->
            generation, output (sortopiaDataType.SorterPoolSet "") [ParameterBinding.create ParamKeys.generationCurrentKey generation])
        let snapshotSet, summarySet = Set.ofArray snapshots, Set.ofArray summaries
        let batches = ResizeArray<int array * runDataOutput>()
        let pending = ResizeArray<int>()
        for generation in Set.union snapshotSet summarySet do
            if summarySet.Contains generation then pending.Add generation
            if pending.Count > 0 && (snapshotSet.Contains generation || pending.Count >= 256) then
                let generations = pending.ToArray()
                let bindings = [
                    ParameterBinding.create ParamKeys.generationFirstKey generations[0]
                    ParameterBinding.create ParamKeys.generationCurrentKey generations[generations.Length - 1]
                    ParameterBinding.fromText (UMX.tag "SummaryGenerations") parameterValueType.Text (String.Join(",", generations)) ]
                batches.Add(generations, output (sortopiaDataType.SorterPoolSummarySet "") bindings)
                pending.Clear()
        { Snapshots = snapshotOutputs; Summaries = batches.ToArray() }

    let checkSnapshot parameters generation (snapshot: sorterPoolSet) =
        if snapshot.Generation <> generation || snapshot.Pools.Length <> FunctionParameters.required ParamKeys.sorterPoolCountKey parameters then
            invalidArg "snapshot" "Snapshot does not match its generation and pool count."

    let checkSummary parameters generations (summary: sorterPoolSummarySet) =
        SorterPoolSummarySet.validate summary |> ignore
        let count = FunctionParameters.required ParamKeys.sorterPoolCountKey parameters
        let expected = [| for generation in generations do for pool in 0 .. count - 1 -> generation, pool |]
        if (summary.Rows |> Array.map (fun row -> row.Generation, row.PoolIndex)) <> expected then
            invalidOp "Pool summary samples are missing or duplicated."

    /// Include domain validation in the same plan used for all result files.
    let outputs parameters (plan: evolutionSamplingPlan) = [
        for generation, output in plan.Snapshots do
            yield output, (function sortopiaStorageData.SorterPoolSet snapshot -> checkSnapshot parameters generation snapshot
                                    | _ -> invalidOp "Expected a sampled pool set.")
        for generations, output in plan.Summaries do
            yield output, (function sortopiaStorageData.SorterPoolSummarySet summary -> checkSummary parameters generations summary
                                    | _ -> invalidOp "Expected a pool summary set.") ]

/// Observes evolution and builds scheduled payloads; all persistence goes through the database.
type EvolutionSampler(parameters: parameterSet, plan: evolutionSamplingPlan, database: IRunDatabase, token: CancellationToken, ?resumeGeneration: int) =
    let final = plan.Snapshots |> Array.map fst |> Array.max
    let measure = FunctionParameters.required ParamKeys.sorterEvalMeasureKey parameters |> SorterEvalFunctions.fromCompactString
    let snapshots = plan.Snapshots |> Map.ofArray
    let summarySet = plan.Summaries |> Array.collect fst |> Set.ofArray
    let batches = plan.Summaries |> Array.map (fun (generations, output) -> Array.last generations, (generations, output)) |> Map.ofArray
    let pending = ResizeArray<sgdPoolStatistics>()
    let start = defaultArg resumeGeneration -1
    let mutable lastGeneration = if start < 0 then -1 else start - 1
    do
        if start >= 0 then
            let reuse output = database.Save(output, fun () -> invalidOp "A checkpoint output disappeared during continuation.")
            for generation, output in plan.Snapshots do if generation <= start then reuse output
            for generations, output in plan.Summaries do if Array.last generations <= start then reuse output

    member _.Observe(snapshot: sorterPoolSet) =
        token.ThrowIfCancellationRequested()
        if snapshot.Generation <> lastGeneration + 1 || snapshot.Generation > final then invalidOp "Evolution observations must arrive in generation order."
        lastGeneration <- snapshot.Generation
        if snapshot.Generation > start then
            if summarySet.Contains snapshot.Generation then pending.AddRange(EvolutionReports.summarize measure snapshot)
            // Publish statistics before the checkpoint that makes this interval resumable.
            match batches |> Map.tryFind snapshot.Generation with
            | Some (_, output) ->
                database.Save(output, sortopiaStorageData.SorterPoolSummarySet { TestsId = UMX.untag snapshot.TestsId; Rows = pending.ToArray() })
                pending.Clear()
            | None -> ()
        match snapshots |> Map.tryFind snapshot.Generation with
        | Some output -> database.Save(output, sortopiaStorageData.SorterPoolSet snapshot)
        | None -> ()

    member _.Complete(result: sorterPoolSet) =
        if lastGeneration <> final || pending.Count <> 0 then invalidOp "Evolution reporting did not reach the final generation."
        // The final snapshot is the ordinary result. Save verifies the returned
        // population against that observation without publishing or counting it twice.
        database.Save(snapshots[final], sortopiaStorageData.SorterPoolSet result)
