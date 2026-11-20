namespace Sortopia.Evaluation.Sgd

open System
open System.Threading.Tasks
open System.Runtime.Intrinsics
open FSharp.UMX
open Sortopia.Core
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Evaluation

[<RequireQualifiedAccess>]
type initialPoolSelection = Random | Best

module InitialPoolSelection =
    let toString = function initialPoolSelection.Random -> "Random" | initialPoolSelection.Best -> "Best"
    let fromString = function
        | "Random" -> initialPoolSelection.Random
        | "Best" -> initialPoolSelection.Best
        | _ -> invalidArg "InitialPoolSelection" "Choose Random or Best."

type standardSgdSettings = {
    PoolCount: int
    SortersPerPool: int
    RetainedParentsPerPool: int
    ChildrenPerParent: int
    InitialPoolSeed: int
    InitialPoolSelection: initialPoolSelection
    Generations: int
    MutationMod: int
    AdjustCeLengthFrequency: int
    SortedFraction: float
    PrioritizeNewMutants: bool
    DistinctSorterHashes: bool
    SelectionMeasure: sorterEvalMeasure
}

/// Read-only seed models and evaluations, reusable across mutation combinations.
/// Only the selected models are retained; the full source can be released.
type preparedSgdPopulation = private {
    Configuration: Guid
    SourceId: Guid
    TestsId: Guid
    RawCeLength: int
    Seeds: (sorterModel * sorterEval) array array
}

// Adapted from GeneSort.Eval.V1/Sgd/SgdStandard/{SpMember,SorterPool,Pipeline}_Standard.
// Copyright (c) 2025 Tom Patterson. See GENESORT-LICENSE.txt.
// This is the fixed-pool Standard pipeline; scheduling, storage and UI stay outside it.
module StandardSgd =
    let validateSettings settings =
        if settings.PoolCount < 1 || settings.SortersPerPool < 1 || settings.ChildrenPerParent < 1 then
            invalidArg "settings" "Pool count, sorters per pool, and children per parent must be positive."
        if settings.RetainedParentsPerPool < 0 || settings.RetainedParentsPerPool > settings.SortersPerPool then
            invalidArg "settings" "Retained parents must be between zero and SorterCountPerPool."
        if settings.Generations < 0 || settings.AdjustCeLengthFrequency < 1 then
            invalidArg "settings" "GenerationCount must be nonnegative and AdjustCeLengthFrequency positive."
        if settings.InitialPoolSeed < 0 then
            invalidArg "settings" "InitialPoolSeed must be nonnegative."
        if not (Double.IsFinite settings.SortedFraction) || settings.SortedFraction <= 0.0 || settings.SortedFraction > 1.0 then
            invalidArg "settings" "SortedFraction must be greater than zero and at most one."
        if int64 settings.PoolCount * int64 settings.SortersPerPool > int64 Int32.MaxValue
           || int64 settings.ChildrenPerParent * int64 settings.SortersPerPool + int64 settings.SortersPerPool > int64 Int32.MaxValue
           || int64 settings.Generations * int64 settings.ChildrenPerParent > int64 Int32.MaxValue then
            invalidArg "settings" "The requested population or mutation cursor exceeds the supported size."

    let private identity values = GuidUtils.guidFromObjs values
    let private score settings (memberValue: sorterPoolMember) =
        SorterEvalFunctions.getFunctionForMeasure settings.SelectionMeasure memberValue.Evaluation |> float

    /// GeneSort retains the oldest representative of a duplicate network before ranking survivors.
    let prune settings (pool: sorterPool) =
        let eligible =
            if %(SorterEvalFunctions.getFilterUnsortedFlag settings.SelectionMeasure) then
                // Adaptive RawCeLength is an inclusive last-CE index. The general
                // getIsShortEnough helper instead expects an exclusive length bound.
                pool.Members |> Array.filter (fun m ->
                    SorterEval.getIsSorted m.Evaluation
                    && int (SorterEval.getLastCeIndex m.Evaluation) <= pool.RawCeLength)
            else pool.Members
        let recovering = Array.isEmpty eligible
        let candidates = (if recovering then pool.Members else eligible) |> Array.sortBy (fun m -> m.Birthday, m.Id)
        let candidates = if settings.DistinctSorterHashes then candidates |> Array.distinctBy (fun m -> SorterEval.getSequenceHash m.Evaluation) else candidates
        let members = candidates |> Array.sortBy (fun m ->
            let recoveryRank = if recovering then int (SorterEval.getUnsortedCount m.Evaluation) else 0
            let cursor = if settings.PrioritizeNewMutants then m.MutationIndex else -m.MutationIndex
            recoveryRank, score settings m, cursor, m.Id) |> Array.truncate settings.SortersPerPool
        if Array.isEmpty members then invalidOp "SGD cannot continue with an empty pool."
        { pool with Members = members }

    let adjustCeLength sortedFraction (pool: sorterPool) =
        let sorted = pool.Members |> Array.filter (fun m -> SorterEval.getIsSorted m.Evaluation)
                                |> Array.sortBy (fun m -> SorterEval.getLastCeIndex m.Evaluation)
        if Array.isEmpty sorted then pool
        else
            let index = max 0 (min (sorted.Length - 1) (int (Math.Ceiling(float sorted.Length * sortedFraction)) - 1))
            // Preserve GeneSort's raw cutoff convention (last CE index, not used CE count).
            let cutoff = int (SorterEval.getLastCeIndex sorted[index].Evaluation) |> max 0
            { pool with RawCeLength = cutoff
                        Members = pool.Members |> Array.filter (fun m -> int (SorterEval.getLastCeIndex m.Evaluation) <= cutoff) }

    let private evaluate (options: ParallelOptions) tests rawCeLength (models: sorterModel array) =
        options.CancellationToken.ThrowIfCancellationRequested()
        let padded = min (float Int32.MaxValue) (float rawCeLength * 1.1) |> int |> UMX.tag<ceLength> |> Some
        SorterSetEval.makeSorterModelEvalsWithOptions options models padded tests sorterEvalType.V2 (UMX.tag false)

    let private initialConfiguration settings =
        identity [box (InitialPoolSelection.toString settings.InitialPoolSelection); box settings.PoolCount; box settings.SortersPerPool
                  box (if settings.InitialPoolSelection = initialPoolSelection.Random then settings.InitialPoolSeed else 0)
                  box (if settings.InitialPoolSelection = initialPoolSelection.Best then SorterEvalFunctions.toCompactString settings.SelectionMeasure else "")
                  box (settings.InitialPoolSelection = initialPoolSelection.Best && settings.DistinctSorterHashes)]

    let private preparedTests tests =
        match tests with
        | sortableTests.Bools values when int values.SortableCount >= 512 && Vector256.IsHardwareAccelerated ->
            SortableBitv512Tests.fromBoolArrays values.Id values.ChannelCount values.SortableBinaryArrays |> sortableTests.Bitv512
        | _ -> tests

    let prepareInitialPopulation (options: ParallelOptions) settings (models: sorterModelSet) tests =
        validateSettings settings
        options.CancellationToken.ThrowIfCancellationRequested()
        let channels = SortableTests.getChannelCount tests
        // Prepare once for the repeated count-only evaluations. Keep the source ID,
        // input order and duplicate lanes; saved setup data and cache keys stay unchanged.
        let evaluationTests = preparedTests tests
        options.CancellationToken.ThrowIfCancellationRequested()
        let allModels = models.SorterModels
        for model in allModels do
            if SorterModel.getChannelCount model <> channels then invalidArg "models" "The models and tests must have the same channel count."
        let required = settings.PoolCount * settings.SortersPerPool
        if allModels.Length < required then invalidArg "models" $"SGD needs {required} starting models; the source contains {allModels.Length}."
        let seeds =
            match settings.InitialPoolSelection with
            | initialPoolSelection.Random ->
                // Partial Fisher-Yates with unbiased bounded draws; source order is canonical.
                let samplingRng = randomSplitMix64(UMX.tag (uint64 settings.InitialPoolSeed)) :> IRando
                for index in 0 .. required - 1 do
                    let selected = index + samplingRng.NextIndex(allModels.Length - index)
                    let temp = allModels[index]
                    allModels[index] <- allModels[selected]
                    allModels[selected] <- temp
                allModels |> Array.take required |> Array.chunkBySize settings.SortersPerPool |> Array.map (fun selected ->
                    Array.zip selected (evaluate options evaluationTests (int models.RawCeLength) selected))
            | initialPoolSelection.Best ->
                let evaluations = evaluate options evaluationTests (int models.RawCeLength) allModels
                let eligible = SorterEvalFunctions.filterEvaluations settings.SelectionMeasure evaluations |> Seq.map SorterEval.getSorterId |> Set.ofSeq
                let measure = SorterEvalFunctions.getFunctionForMeasure settings.SelectionMeasure
                let ranked = Array.zip allModels evaluations
                             |> Array.filter (fun (_, evaluation) -> Set.contains (SorterEval.getSorterId evaluation) eligible)
                             |> Array.sortBy (fun (model, evaluation) -> float (measure evaluation), SorterModel.getId model)
                let ranked = if settings.DistinctSorterHashes then ranked |> Array.distinctBy (snd >> SorterEval.getSequenceHash) else ranked
                if ranked.Length < required then
                    invalidArg "models" $"Best selection needs {required} eligible models; only {ranked.Length} remain after the evaluation and duplicate filters."
                ranked |> Array.take required |> Array.chunkBySize settings.SortersPerPool
        options.CancellationToken.ThrowIfCancellationRequested()
        { Configuration = initialConfiguration settings; SourceId = %models.Id; TestsId = %(SortableTests.getId tests)
          RawCeLength = int models.RawCeLength; Seeds = seeds }

    let runObservedPrepared (observe: sorterPoolSet -> unit) (options: ParallelOptions) settings mutator (initial: preparedSgdPopulation) tests =
        validateSettings settings
        options.CancellationToken.ThrowIfCancellationRequested()
        if initial.Configuration <> initialConfiguration settings || initial.TestsId <> %(SortableTests.getId tests) then
            invalidArg "initial" "Prepared starting pools do not match the selection settings or tests."
        let evaluationTests = preparedTests tests
        let mutable pools = initial.Seeds |> Array.mapi (fun poolIndex seeds ->
            let poolId =
                match settings.InitialPoolSelection with
                | initialPoolSelection.Random -> identity [box "Sortopia.StandardSgd/pool/v2"; box initial.SourceId; box settings.InitialPoolSeed; box poolIndex]
                | initialPoolSelection.Best -> identity [box "Sortopia.StandardSgd/best-pool/v1"; box initial.SourceId; box initial.TestsId; box initial.Configuration; box poolIndex]
            let members = seeds |> Array.map (fun (model, evaluation) -> {
                Id = identity [box poolId; box (SorterModel.getId model)]
                Model = model; MutationIndex = 0; MutationMod = settings.MutationMod; Birthday = 0; ParentId = None; Evaluation = evaluation })
            { Id = poolId; RawCeLength = initial.RawCeLength; Members = members })
        let report generation =
            options.CancellationToken.ThrowIfCancellationRequested()
            observe { Generation = generation; TestsId = SortableTests.getId tests; Pools = pools }
        report 0
        for generation in 0 .. settings.Generations - 1 do
            options.CancellationToken.ThrowIfCancellationRequested()
            let adjust = generation % settings.AdjustCeLengthFrequency = 0
            pools <- pools |> Array.map (fun pool ->
                let parents = pool.Members |> Array.sortBy _.Id
                let children = parents |> EvaluationParallel.map options (fun parent ->
                    Array.init settings.ChildrenPerParent (fun index ->
                        options.CancellationToken.ThrowIfCancellationRequested()
                        let mutationIndex = parent.MutationIndex + index
                        let model = SorterModelMutator.makeMutantSorterModelFromIndexAndMod mutator parent.Model (UMX.tag mutationIndex) (UMX.tag parent.MutationMod)
                        parent, mutationIndex, model)) |> Array.concat
                let childEvals = children |> Array.map (fun (_, _, model) -> model) |> evaluate options evaluationTests pool.RawCeLength
                let childMembers = Array.map2 (fun ((parent: sorterPoolMember), cursor, model) evaluation -> {
                    Id = identity [box parent.Id; box cursor; box (SorterModelMutator.getId mutator)]
                    Model = model; MutationIndex = 0; MutationMod = parent.MutationMod; Birthday = generation
                    ParentId = Some parent.Id; Evaluation = evaluation }) children childEvals
                let retained = parents |> Array.sortBy (fun m -> score settings m, m.Id) |> Array.truncate settings.RetainedParentsPerPool
                                       |> Array.map (fun m -> { m with MutationIndex = m.MutationIndex + settings.ChildrenPerParent })
                let retained =
                    if adjust then
                        let evaluations = retained |> Array.map _.Model |> evaluate options evaluationTests pool.RawCeLength
                        Array.map2 (fun memberValue evaluation -> { memberValue with Evaluation = evaluation }) retained evaluations
                    else retained
                { pool with Members = Array.append retained childMembers }
                |> (if adjust then adjustCeLength settings.SortedFraction else id)
                |> prune settings)
            report (generation + 1)
        options.CancellationToken.ThrowIfCancellationRequested()
        { Generation = settings.Generations; TestsId = SortableTests.getId tests; Pools = pools }
        |> SorterPoolSet.validate

    let runObserved observe options settings mutator models tests =
        let initial = prepareInitialPopulation options settings models tests
        runObservedPrepared observe options settings mutator initial tests

    let run options settings mutator models tests = runObserved ignore options settings mutator models tests
