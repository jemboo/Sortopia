namespace Sortopia.Functions

open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Core
open Sortopia.Runs
open Sortopia.Sorting.Inputs
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Evaluation
open Sortopia.Evaluation.Sgd
open FunctionParameters

module SgdFunctions =
    let private modelsType = sortopiaDataType.SorterModelSet ""
    let private testsType = sortopiaDataType.SortableTests ""
    let private poolType = sortopiaDataType.SorterPoolSet ""

    let private readSettings (host: runHost) : standardSgdSettings =
        let parameters = host.Parameters
        { PoolCount = required ParamKeys.sorterPoolCountKey parameters
          SortersPerPool = required ParamKeys.sorterCountPerPoolKey parameters
          InitialPoolSeed = required ParamKeys.initialPoolSeedKey parameters
          InitialPoolSelection = required ParamKeys.initialPoolSelectionKey parameters |> InitialPoolSelection.fromString
          RetainedParentsPerPool = required ParamKeys.selectedSorterCountPerPoolKey parameters
          ChildrenPerParent = required ParamKeys.sorterChildCountKey parameters
          Generations = EvolutionSampling.target host.Sgd
          MutationMod = required ParamKeys.mutationModKey parameters
          AdjustCeLengthFrequency = required ParamKeys.adjustCeLengthFrequencyKey parameters
          SortedFraction = required ParamKeys.sortedFractionKey parameters
          PrioritizeNewMutants = required ParamKeys.prioritizeNewMutantsKey parameters
          DistinctSorterHashes = required ParamKeys.distinctSorterHashesKey parameters
          SelectionMeasure = required ParamKeys.sorterEvalMeasureKey parameters |> SorterEvalFunctions.fromCompactString }

    let private loadTests (host: runHost) =
        match host.Database.Load(host.Database.Find testsType) with
        | sortopiaStorageData.SortableTests tests -> tests
        | _ -> invalidOp "Expected setup sortable tests."

    let private initialPopulation (host: runHost) settings tests =
        let prepare () =
            let models =
                match host.Database.Load(host.Database.Find modelsType) with
                | sortopiaStorageData.SorterModelSet value -> value
                | _ -> invalidOp "Expected setup sorter models."
            EvaluationInputs.validateCopiedModels (SortableTests.getChannelCount tests) models
            if models.SorterModels |> Array.exists (function sorterModel.Simple (simpleSorterModel.Msuf4 _) -> false | _ -> true) then
                invalidArg "models" "sorter-sgd.standard.msuf4.simple requires Simple MSUF4 source models."
            StandardSgd.prepareInitialPopulation
                (ParallelOptions(MaxDegreeOfParallelism = host.EvaluationParallelism, CancellationToken = host.CancellationToken)) settings models tests
        match settings.InitialPoolSelection, host.PreparationCache with
        | initialPoolSelection.Best, Some cache ->
            // Ranking depends on the sources and pool settings, not on mutation parameters.
            // Keep this key stable so a batch shares preparation across mutation combinations.
            let references = [host.Database.Find modelsType; host.Database.Find testsType]
            let key = GuidUtils.guidFromObjs [box "StandardSgd/best-preparation/v1"; box (sprintf "%A" references);
                                             box settings.PoolCount; box settings.SortersPerPool; box settings.DistinctSorterHashes;
                                             box (SorterEvalFunctions.toCompactString settings.SelectionMeasure)] |> string
            cache.GetOrAdd(key, fun () -> box (prepare ())) :?> preparedSgdPopulation
        | _ -> prepare ()

    let private prepare (host: runHost) =
        let configuration = readSettings host
        if host.ResumeSnapshot.IsNone && configuration.InitialPoolSelection = initialPoolSelection.Best then
            let tests = loadTests host
            initialPopulation host configuration tests |> ignore

    let private createMutator channels parameters =
        SimpleSorterModelMutator.getMsuf4ModelMutator channels
            (required ParamKeys.rngTypeKey parameters |> RngType.fromString |> RngFactory.create) (UMX.tag true)
            (required ParamKeys.seedModificationRateKey parameters |> UMX.tag)
            (required ParamKeys.modificationRateKey parameters |> UMX.tag)
            (required ParamKeys.orthoRateKey parameters |> UMX.tag)
            (required ParamKeys.paraRateKey parameters |> UMX.tag)
            (required ParamKeys.selfSymRateKey parameters |> UMX.tag)
            (required ParamKeys.mutatorVariantKey parameters |> MutatorVariant.fromString)
        |> sorterModelMutator.Simple

    let private evolve (host: runHost) (sampler: EvolutionSampler) (plan: evolutionSamplingPlan) =
        let tests = loadTests host
        let configuration = readSettings host
        let mutator = createMutator (SortableTests.getChannelCount tests) host.Parameters
        let snapshotGenerations = plan.Snapshots |> Array.map fst |> Set.ofArray
        let observe (snapshot: sorterPoolSet) =
            sampler.Observe snapshot
            // Publish checkpoints before notifying callers, which may cancel at a boundary.
            if Set.contains snapshot.Generation snapshotGenerations then
                host.GenerationProgress |> Option.iter (fun report -> report snapshot.Generation)
        let options = ParallelOptions(MaxDegreeOfParallelism = host.EvaluationParallelism, CancellationToken = host.CancellationToken)
        match host.ResumeSnapshot with
        | Some snapshot -> StandardSgd.resumeObserved observe options configuration mutator snapshot tests
        | None -> StandardSgd.runObservedPrepared observe options configuration mutator (initialPopulation host configuration tests) tests

    let private execute (host: runHost) =
        let basis = host.Outputs.Planned |> List.find (fun output -> output.DataType = poolType)
        let plan = EvolutionSampling.planForRun host.Parameters basis host.Reporting host.Sgd
        let sampler = EvolutionSampler(host.Parameters, plan, host.Database, host.CancellationToken,
                                       ?resumeGeneration = (host.ResumeSnapshot |> Option.map _.Generation))
        evolve host sampler plan |> sampler.Complete

    // This persisted contract version participates in output identities.
    let standardMsuf4Simple =
        FunctionDefinition.results "7" SgdParameterContract.requiredParameters
            [{ DataType = poolType; ParameterKeys = SgdParameterContract.requiredParameters |> List.map _.Name; Dependencies = [modelsType; testsType] }] SgdParameterContract.support execute
        |> fun definition -> { definition with Prepare = prepare }
        |> FunctionDefinition.describe (String.concat " " [
            "Run GeneSort's fixed-pool Standard SGD on copied Simple MSUF4 models and sortable tests."
            "InitialPoolSelection chooses Random (uniform sampling without replacement using InitialPoolSeed) or Best (rank the source using SorterEvalMeasure and DistinctSorterHashes; ties use model ID)."
            "Best ignores InitialPoolSeed."
            "Source ranking is shared across mutation combinations."
            "Pools use disjoint initial models."
            "Mutate, evaluate, adjust CE limits, and select survivors."
            "Channel count comes from the tests."
            "Fixed prefixes are retained in metrics with -1 CE usage markers."
            "SnapshotInterval and SnapshotCount are run properties: the target is their product, with generation zero saved separately."
            "SnapshotInterval is fixed after parameter generation; increase SnapshotCount to continue from the latest complete checkpoint."
            "Pool summaries use an independent schedule." ])
