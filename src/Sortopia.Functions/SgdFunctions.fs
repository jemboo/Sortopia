namespace Sortopia.Functions

open System
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

    let private keys = [
        ParamKey.describe ParamKeys.generationCountKey
        ParamKey.describe ParamKeys.sorterPoolCountKey
        ParamKey.describe ParamKeys.sorterCountPerPoolKey
        ParamKey.describe ParamKeys.initialPoolSeedKey
        ParamKey.describe ParamKeys.initialPoolSelectionKey
        ParamKey.describe ParamKeys.selectedSorterCountPerPoolKey
        ParamKey.describe ParamKeys.sorterChildCountKey
        ParamKey.describe ParamKeys.mutationModKey
        ParamKey.describe ParamKeys.adjustCeLengthFrequencyKey
        ParamKey.describe ParamKeys.sortedFractionKey
        ParamKey.describe ParamKeys.prioritizeNewMutantsKey
        ParamKey.describe ParamKeys.distinctSorterHashesKey
        ParamKey.describe ParamKeys.sorterEvalMeasureKey
        ParamKey.describe ParamKeys.rngTypeKey
        ParamKey.describe ParamKeys.mutatorVariantKey
        ParamKey.describe ParamKeys.seedModificationRateKey
        ParamKey.describe ParamKeys.modificationRateKey
        ParamKey.describe ParamKeys.orthoRateKey
        ParamKey.describe ParamKeys.paraRateKey
        ParamKey.describe ParamKeys.selfSymRateKey ]

    let private combinationError defaults spans =
        try
            let ints key = inputValues key defaults spans
            let floats key = inputValues key defaults spans
            for retained in ints ParamKeys.selectedSorterCountPerPoolKey do
                for size in ints ParamKeys.sorterCountPerPoolKey do
                    if retained > size then invalidArg "SelectedSorterCountPerPool" "Cannot exceed SorterCountPerPool."
            let weights = [ParamKeys.orthoRateKey; ParamKeys.paraRateKey; ParamKeys.selfSymRateKey] |> List.map floats
            if weights |> List.forall (List.contains 0.0) then invalidArg "rates" "At least one mutation weight must be positive."
            ""
        with error -> error.Message

    let private settings p = {
        PoolCount = required ParamKeys.sorterPoolCountKey p; SortersPerPool = required ParamKeys.sorterCountPerPoolKey p
        InitialPoolSeed = required ParamKeys.initialPoolSeedKey p
        InitialPoolSelection = required ParamKeys.initialPoolSelectionKey p |> InitialPoolSelection.fromString
        RetainedParentsPerPool = required ParamKeys.selectedSorterCountPerPoolKey p; ChildrenPerParent = required ParamKeys.sorterChildCountKey p
        Generations = required ParamKeys.generationCountKey p; MutationMod = required ParamKeys.mutationModKey p
        AdjustCeLengthFrequency = required ParamKeys.adjustCeLengthFrequencyKey p; SortedFraction = required ParamKeys.sortedFractionKey p
        PrioritizeNewMutants = required ParamKeys.prioritizeNewMutantsKey p; DistinctSorterHashes = required ParamKeys.distinctSorterHashesKey p
        SelectionMeasure = required ParamKeys.sorterEvalMeasureKey p |> SorterEvalFunctions.fromCompactString }

    let private loadTests (host: runHost) =
        match host.Database.Load(host.Database.Find testsType) with
        | sortopiaStorageData.SortableTests tests -> tests
        | _ -> invalidOp "Expected setup sortable tests."

    let private initialPopulation (host: runHost) settings tests =
        let prepare () =
            let models = match host.Database.Load(host.Database.Find modelsType) with sortopiaStorageData.SorterModelSet value -> value | _ -> invalidOp "Expected setup sorter models."
            EvaluationInputs.validateCopiedModels (SortableTests.getChannelCount tests) models
            if models.SorterModels |> Array.exists (function sorterModel.Simple (simpleSorterModel.Msuf4 _) -> false | _ -> true) then
                invalidArg "models" "sorter-sgd.standard.msuf4.simple requires Simple MSUF4 source models."
            StandardSgd.prepareInitialPopulation
                (ParallelOptions(MaxDegreeOfParallelism = host.EvaluationParallelism, CancellationToken = host.CancellationToken)) settings models tests
        match settings.InitialPoolSelection, host.PreparationCache with
        | initialPoolSelection.Best, Some cache ->
            let references = [host.Database.Find modelsType; host.Database.Find testsType]
            let key = GuidUtils.guidFromObjs [box "StandardSgd/best-preparation/v1"; box (sprintf "%A" references);
                                             box settings.PoolCount; box settings.SortersPerPool; box settings.DistinctSorterHashes;
                                             box (SorterEvalFunctions.toCompactString settings.SelectionMeasure)] |> string
            cache.GetOrAdd(key, fun () -> box (prepare ())) :?> preparedSgdPopulation
        | _ -> prepare ()

    let private prepare (host: runHost) =
        let configuration = settings host.Parameters
        if configuration.InitialPoolSelection = initialPoolSelection.Best then
            let tests = loadTests host
            initialPopulation host configuration tests |> ignore

    let private execute (host: runHost) =
        let inputs = [host.Database.Find modelsType; host.Database.Find testsType]
        let evolve observe =
            let tests = loadTests host
            let channels = SortableTests.getChannelCount tests
            let p = host.Parameters
            let configuration = settings p
            let initial = initialPopulation host configuration tests
            let mutator = SimpleSorterModelMutator.getMsuf4ModelMutator channels
                              (required ParamKeys.rngTypeKey p |> RngType.fromString |> RngFactory.create) (UMX.tag true)
                              (required ParamKeys.seedModificationRateKey p |> UMX.tag) (required ParamKeys.modificationRateKey p |> UMX.tag)
                              (required ParamKeys.orthoRateKey p |> UMX.tag) (required ParamKeys.paraRateKey p |> UMX.tag) (required ParamKeys.selfSymRateKey p |> UMX.tag)
                              (required ParamKeys.mutatorVariantKey p |> MutatorVariant.fromString)
                          |> sorterModelMutator.Simple
            StandardSgd.runObservedPrepared observe (ParallelOptions(MaxDegreeOfParallelism = host.EvaluationParallelism, CancellationToken = host.CancellationToken)) configuration mutator initial tests
        match host.Reporting with
        | None -> host.Database.Save(host.Outputs.Output(poolType, inputs), fun () -> evolve ignore |> sortopiaStorageData.SorterPoolSet)
        | Some _ ->
            // Incomplete sample plans need observations even when the final result exists.
            let plan = EvolutionSampling.plan host.Parameters (host.Outputs.Output poolType) host.Reporting
            let sampler = EvolutionSampler(host.Parameters, plan, host.Database, host.CancellationToken)
            evolve sampler.Observe |> sampler.Complete

    let private defaults context =
        ParameterDefaults.common context @ [
            ParameterBinding.create ParamKeys.generationCountKey 1
            ParameterBinding.create ParamKeys.sorterPoolCountKey 1
            ParameterBinding.create ParamKeys.sorterCountPerPoolKey 1
            ParameterBinding.create ParamKeys.selectedSorterCountPerPoolKey 1
            ParameterBinding.create ParamKeys.sorterChildCountKey 1
            ParameterBinding.create ParamKeys.mutationModKey 1
            ParameterBinding.create ParamKeys.adjustCeLengthFrequencyKey 1
            ParameterBinding.create ParamKeys.sortedFractionKey 0.99
            ParameterBinding.create ParamKeys.prioritizeNewMutantsKey false
            ParameterBinding.create ParamKeys.distinctSorterHashesKey false
            ParameterBinding.create ParamKeys.sorterEvalMeasureKey (SorterEvalMeasure.toCompactString SorterEvalMeasure.stageBiasedFilterUnsorted)
            ParameterBinding.create ParamKeys.mutatorVariantKey "V1"
            ParameterBinding.create ParamKeys.seedModificationRateKey 0.
            ParameterBinding.create ParamKeys.modificationRateKey 0.
            ParameterBinding.create ParamKeys.orthoRateKey 1.
            ParameterBinding.create ParamKeys.paraRateKey 1.
            ParameterBinding.create ParamKeys.selfSymRateKey 1. ]
        |> ParameterDefaults.select context

    let standardMsuf4Simple =
        let editorRules = Map.ofList [
            for key in [ParamKeys.sorterPoolCountKey; ParamKeys.sorterCountPerPoolKey; ParamKeys.sorterChildCountKey; ParamKeys.adjustCeLengthFrequencyKey] do
                ParameterRules.minimum key 1
            for key in [ParamKeys.generationCountKey; ParamKeys.selectedSorterCountPerPoolKey; ParamKeys.initialPoolSeedKey] do
                ParameterRules.minimum key 0
            for key in [ParamKeys.seedModificationRateKey; ParamKeys.modificationRateKey] do
                ParameterRules.create key [] "Must be finite and between 0 and 1." (fun n -> Double.IsFinite n && n >= 0. && n <= 1.)
            ParameterRules.create ParamKeys.sortedFractionKey [] "Must be finite, greater than 0, and at most 1."
                (fun n -> Double.IsFinite n && n > 0. && n <= 1.)
            ParameterRules.create ParamKeys.mutatorVariantKey ["V1"; "V2"] "MSUF4 supports V1 and V2." (fun value -> value = "V1" || value = "V2")
            ParameterRules.create ParamKeys.sorterEvalMeasureKey [] "Enter a valid sorter evaluation measure."
                (fun value -> SorterEvalFunctions.fromCompactString value |> ignore; true)
            ParameterRules.create ParamKeys.initialPoolSelectionKey ["Random"; "Best"]
                "Random samples without replacement using InitialPoolSeed. Best ranks the full source with SorterEvalMeasure and ignores InitialPoolSeed; score ties use model ID."
                (fun value -> InitialPoolSelection.fromString value |> ignore; true)
            for key in [ParamKeys.orthoRateKey; ParamKeys.paraRateKey; ParamKeys.selfSymRateKey] do
                ParameterRules.create key []
                    "Finite nonnegative relative mutation weight. OrthoRate, ParaRate, and SelfSymRate are normalized together; at least one must be positive."
                    (fun value -> Double.IsFinite value && value >= 0.)
            ]
        let support =
            { ParameterDefaults.support with
                Defaults = defaults; EditorRules = ParameterRules.combine [ParameterRules.common; editorRules]
                CombinationError = combinationError }
        FunctionDefinition.results "4" keys [poolType, keys |> List.map _.Name]
            [poolType, [modelsType; testsType]] support execute
        |> fun definition -> { definition with Prepare = prepare }
        |> FunctionDefinition.describe "Run GeneSort's fixed-pool Standard SGD on copied Simple MSUF4 models and sortable tests. InitialPoolSelection chooses Random (uniform sampling without replacement using InitialPoolSeed) or Best (evaluate the entire source and retain the best models using SorterEvalMeasure and DistinctSorterHashes). Best ignores InitialPoolSeed and breaks score ties by model ID. Source ranking is shared across mutation combinations within an execution batch. Multiple pools use disjoint models. Mutate every member, evaluate on the full test set, adjust CE limits, and select survivors with duplicate/recovery handling. Save final pools containing their models and evaluations. CSV reports read these pools directly. Optional run reporting records full population snapshots and lightweight pool statistics on independent schedules. Channel count comes from the tests; each model supplies its own prefix, which is evaluated against those tests. GenerationCount counts evolution steps after the initial evaluation. Cancellation restarts unfinished evolution and verifies previously saved reporting samples during replay."
