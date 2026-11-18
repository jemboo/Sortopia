namespace Sortopia.FileDb

open System
open System.Threading
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Evaluation
open Sortopia.Evaluation.Sgd

type evolutionOutputPlan = { Snapshots: (int * dataKey) array; Summaries: (int array * dataKey) array }
type evolutionSamples = { Generation: int; Snapshots: int array; SummaryGenerations: int array; PoolCount: int }

/// Normal database results: their keys depend on parameters, inputs and generations,
/// never on run IDs, work-item IDs or export locations. No manifest is needed.
module EvolutionReportingStore =
    let validate (item: runParameters) =
        item.Reporting |> Option.iter (fun reporting ->
            SgdReporting.validate reporting |> ignore
            if not (ResultsFunctionCatalog.require item.Results.FunctionName).SupportsEvolutionReporting then
                invalidArg "reporting" "The selected results function does not support evolution reporting."
            if FunctionParameters.required ParamKeys.generationCountKey item.Parameters < 0 then
                invalidArg "reporting" "Generation count must be nonnegative.")

    let plan (item: runParameters) =
        // Derive saved identities without needing an installed catalog executor,
        // so inspection and cleanup can still address older saved combinations.
        item.Reporting |> Option.iter (SgdReporting.validate >> ignore)
        match item.Reporting with
        | None -> { Snapshots = [||]; Summaries = [||] }
        | Some reporting ->
            let final = FunctionParameters.required ParamKeys.generationCountKey item.Parameters
            let snapshots = SgdReporting.samples reporting.SnapshotSchedule final |> Seq.toArray
            let summaries = SgdReporting.samples reporting.SummarySchedule final |> Seq.toArray
            let key dataType bindings =
                let parameters = bindings |> List.fold (fun (p: parameterSet) b -> p.WithBinding b) item.Parameters
                DataKey.create item.ProjectId (string item.Results.DatabaseName) false (string item.Results.FunctionName) item.Results.FunctionVersion
                    dataType (parameters.ParamMap |> Map.toList |> List.map fst) parameters item.SetupInputs
            let snapshotKeys = snapshots |> Array.map (fun generation ->
                generation, key (sortopiaDataType.SorterPoolSet "snapshot") [ParameterBinding.create ParamKeys.generationCurrentKey generation])
            let snapshotSet, summarySet = Set.ofArray snapshots, Set.ofArray summaries
            let batches = ResizeArray<int array * dataKey>()
            let pending = ResizeArray<int>()
            for generation in Set.union snapshotSet summarySet do
                if summarySet.Contains generation then pending.Add generation
                if pending.Count > 0 && (snapshotSet.Contains generation || pending.Count >= 256) then
                    let generations = pending.ToArray()
                    // Actual sampled generations make equivalent schedules share a batch.
                    let bindings = [
                        ParameterBinding.create ParamKeys.generationFirstKey generations[0]
                        ParameterBinding.create ParamKeys.generationCurrentKey generations[generations.Length - 1]
                        ParameterBinding.fromText (UMX.tag "SummaryGenerations") parameterValueType.Text (String.Join(",", generations)) ]
                    batches.Add(generations, key (sortopiaDataType.SorterPoolSummarySet "") bindings)
                    pending.Clear()
            { Snapshots = snapshotKeys; Summaries = batches.ToArray() }

    let keys item =
        let outputs = plan item
        Array.append (outputs.Snapshots |> Array.map snd) (outputs.Summaries |> Array.map snd)

    let private store folder (item: runParameters) = SortopiaFileDb(folder, item.ProjectId)
    let private read folder item (key: dataKey) token =
        match ((store folder item) :> ISortopiaDb).TryLoad(key, token) |> HostStorage.require with
        | Some saved -> saved.Value
        | None -> invalidOp $"Sampled result {key.Reference.DataType} {key.Id} is missing. Execute this parameter combination to generate it."

    let private publish folder item key value token =
        let saved, reused = ((store folder item) :> ISortopiaDb).GetOrCreate(key, (fun () -> Ok value), token) |> HostStorage.require
        if reused && DataCodec.encode saved.Value <> DataCodec.encode value then
            invalidOp $"Saved evolution sample differs from replay: {key.Reference.Id}"
        saved.Reference

    let private snapshotKey item generation =
        (plan item).Snapshots |> Array.tryFind (fst >> (=) generation) |> Option.map snd
        |> Option.defaultWith (fun () -> invalidArg "generation" "This generation is not a configured snapshot.")

    let snapshotPath folder item generation = (store folder item).GetPath (snapshotKey item generation).Reference

    let private checkSnapshot (item: runParameters) generation (snapshot: sorterPoolSet) =
        if snapshot.Generation <> generation || snapshot.Pools.Length <> FunctionParameters.required ParamKeys.sorterPoolCountKey item.Parameters then
            invalidArg "snapshot" "Snapshot does not match its generation and pool count."

    let saveSnapshot folder item generation (snapshot: sorterPoolSet) token =
        checkSnapshot item generation snapshot
        publish folder item (snapshotKey item generation) (functionData.SorterPoolSet snapshot) token

    let loadSnapshot folder item generation token =
        match read folder item (snapshotKey item generation) token with
        | functionData.SorterPoolSet snapshot -> checkSnapshot item generation snapshot; snapshot
        | _ -> invalidOp "Invalid sampled pool result."

    let private checkSummary (item: runParameters) generations (summary: sorterPoolSummarySet) =
        SorterPoolSummarySet.validate summary |> ignore
        let count = FunctionParameters.required ParamKeys.sorterPoolCountKey item.Parameters
        let expected = [| for generation in generations do for pool in 0 .. count - 1 -> generation, pool |]
        if (summary.Rows |> Array.map (fun row -> row.Generation, row.PoolIndex)) <> expected then
            invalidOp "Pool summary samples are missing or duplicated."

    let saveSummary folder item (summary: sorterPoolSummarySet) token =
        let generations = summary.Rows |> Array.map _.Generation |> Array.distinct
        checkSummary item generations summary
        let key = (plan item).Summaries |> Array.tryFind (fst >> (=) generations) |> Option.map snd
                  |> Option.defaultWith (fun () -> invalidArg "summary" "Summary batch does not match the configured sampling schedule.")
        publish folder item key (functionData.SorterPoolSummarySet summary) token

    let load folder (item: runParameters) token =
        if item.Reporting.IsNone then invalidOp "This parameter combination has no evolution reporting schedule."
        let outputs = plan item
        let rows = outputs.Summaries |> Array.collect (fun (generations, key) ->
            match read folder item key token with
            | functionData.SorterPoolSummarySet summary -> checkSummary item generations summary; summary.Rows
            | _ -> invalidOp "Expected a pool summary set.")
        { Generation = FunctionParameters.required ParamKeys.generationCountKey item.Parameters
          Snapshots = outputs.Snapshots |> Array.map fst
          SummaryGenerations = outputs.Summaries |> Array.collect fst
          PoolCount = FunctionParameters.required ParamKeys.sorterPoolCountKey item.Parameters }, rows

    let inspectOutputs folder item token =
        let available, errors = ResizeArray<storedOutput>(), ResizeArray<string>()
        let outputPlan = plan item
        let outputs = [|
            for generation, key in outputPlan.Snapshots do
                yield key, (function functionData.SorterPoolSet snapshot -> checkSnapshot item generation snapshot | _ -> invalidOp "Expected a sampled pool set.")
            for generations, key in outputPlan.Summaries do
                yield key, (function functionData.SorterPoolSummarySet summary -> checkSummary item generations summary | _ -> invalidOp "Expected a pool summary set.") |]
        for key, check in outputs do
            match ((store folder item) :> ISortopiaDb).TryLoad(key, token) with
            | Ok (Some saved) ->
                try check saved.Value; available.Add { Reference = saved.Reference; Reused = true }
                with error -> errors.Add error.Message
            | Ok None -> ()
            | Error error -> errors.Add error
        { State = if errors.Count > 0 then outputState.Invalid elif available.Count = outputs.Length then outputState.Complete
                  elif available.Count = 0 then outputState.Pending else outputState.Partial
          Available = List.ofSeq available; ExpectedCount = outputs.Length; Error = String.Join(Environment.NewLine, errors) }

    let inspect folder item token =
        let value = inspectOutputs folder item token
        value.State, value.Error

    let includeInspection folder item token (outputs: outputInspection) =
        let samples = inspectOutputs folder item token
        let available = outputs.Available @ samples.Available
        let expected = outputs.ExpectedCount + samples.ExpectedCount
        { State = if outputs.State = outputState.Invalid || samples.State = outputState.Invalid then outputState.Invalid
                  elif available.Length = expected then outputState.Complete elif available.IsEmpty then outputState.Pending else outputState.Partial
          Available = available; ExpectedCount = expected
          Error = String.Join(Environment.NewLine, [outputs.Error; samples.Error] |> List.filter (String.IsNullOrWhiteSpace >> not)) }

    let create folder (item: runParameters) (token: CancellationToken) =
        let outputs = plan item
        let final = FunctionParameters.required ParamKeys.generationCountKey item.Parameters
        let measure = FunctionParameters.required ParamKeys.sorterEvalMeasureKey item.Parameters |> SorterEvalFunctions.fromCompactString
        let snapshotKeys = outputs.Snapshots |> Map.ofArray
        let summarySet = outputs.Summaries |> Array.collect fst |> Set.ofArray
        let batches = outputs.Summaries |> Array.map (fun (generations, key) -> Array.last generations, (generations, key)) |> Map.ofArray
        let pending = ResizeArray<sgdPoolStatistics>()
        let mutable lastGeneration = -1
        { new IEvolutionReporting with
            member _.Observe snapshot =
                token.ThrowIfCancellationRequested()
                if snapshot.Generation <> lastGeneration + 1 || snapshot.Generation > final then invalidOp "Evolution observations must arrive in generation order."
                lastGeneration <- snapshot.Generation
                if summarySet.Contains snapshot.Generation then pending.AddRange(EvolutionReports.summarize measure snapshot)
                if snapshotKeys.ContainsKey snapshot.Generation then
                    checkSnapshot item snapshot.Generation snapshot
                    publish folder item snapshotKeys[snapshot.Generation] (functionData.SorterPoolSet snapshot) token |> ignore
                if batches.ContainsKey snapshot.Generation then
                    let generations, key = batches[snapshot.Generation]
                    let summary = { TestsId = UMX.untag snapshot.TestsId; Rows = pending.ToArray() }
                    checkSummary item generations summary
                    publish folder item key (functionData.SorterPoolSummarySet summary) token |> ignore
                    pending.Clear()
            member _.Complete() =
                if lastGeneration <> final || pending.Count <> 0 then invalidOp "Evolution reporting did not reach the final generation." }
