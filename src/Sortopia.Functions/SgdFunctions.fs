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
    let private evalType = sortopiaDataType.SorterSetEval ""

    let private keys = [
        ParamKey.describe ParamKeys.generationCountKey
        ParamKey.describe ParamKeys.sorterPoolCountKey
        ParamKey.describe ParamKeys.sorterCountPerPoolKey
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

    let private inputError defaults spans =
        try
            let ints key = inputValues key defaults spans
            let floats key = inputValues key defaults spans
            for key in [ParamKeys.sorterPoolCountKey; ParamKeys.sorterCountPerPoolKey; ParamKeys.sorterChildCountKey; ParamKeys.adjustCeLengthFrequencyKey] do
                if ints key |> List.exists (fun n -> n < 1) then invalidArg (string key.Name) "Must be positive."
            for key in [ParamKeys.generationCountKey; ParamKeys.selectedSorterCountPerPoolKey] do
                if ints key |> List.exists (fun n -> n < 0) then invalidArg (string key.Name) "Must be nonnegative."
            for retained in ints ParamKeys.selectedSorterCountPerPoolKey do
                for size in ints ParamKeys.sorterCountPerPoolKey do
                    if retained > size then invalidArg "SelectedSorterCountPerPool" "Cannot exceed SorterCountPerPool."
            for key in [ParamKeys.seedModificationRateKey; ParamKeys.modificationRateKey; ParamKeys.sortedFractionKey] do
                if floats key |> List.exists (fun n -> not (Double.IsFinite n) || n < 0.0 || n > 1.0 || (key.Name = ParamKeys.sortedFractionKey.Name && n = 0.0)) then
                    invalidArg (string key.Name) "Must be finite and between 0 and 1; SortedFraction must be positive."
            let weights = [ParamKeys.orthoRateKey; ParamKeys.paraRateKey; ParamKeys.selfSymRateKey] |> List.map floats
            for values in weights do
                if values |> List.exists (fun n -> not (Double.IsFinite n) || n < 0.0) then invalidArg "rates" "Mutation weights must be finite and nonnegative."
            if weights |> List.forall (List.contains 0.0) then invalidArg "rates" "At least one mutation weight must be positive."
            for value in inputValues ParamKeys.rngTypeKey defaults spans do RngType.fromString value |> ignore
            for value in inputValues ParamKeys.mutatorVariantKey defaults spans do
                if value <> "V1" && value <> "V2" then invalidArg "MutatorVariantKey" "MSUF4 supports V1 and V2."
            for value in inputValues ParamKeys.sorterEvalMeasureKey defaults spans do SorterEvalFunctions.fromCompactString value |> ignore
            ""
        with error -> error.Message

    let private execute (host: runHost) =
        let inputs = [host.SetupDb.Reference modelsType; host.SetupDb.Reference testsType]
        let mutable evolved = false
        let evolve () =
            evolved <- true
            let models = match host.SetupDb.Load modelsType with functionData.SorterModelSet value -> value | _ -> invalidOp "Expected setup sorter models."
            let tests, prefix = match host.SetupDb.Load testsType with functionData.SortableTests (tests, prefix) -> tests, prefix | _ -> invalidOp "Expected setup sortable tests."
            let channels = SortableTests.getChannelCount tests
            EvaluationInputs.validateCopiedModels channels prefix models
            if models.SorterModels |> Array.exists (function sorterModel.Simple (simpleSorterModel.Msuf4 _) -> false | _ -> true) then
                invalidArg "models" "sorter-sgd.standard.msuf4.simple requires Simple MSUF4 source models."
            let p = host.Parameters
            let mutator = SimpleSorterModelMutator.getMsuf4ModelMutator channels
                              (required ParamKeys.rngTypeKey p |> RngType.fromString |> RngFactory.create) (UMX.tag true)
                              (required ParamKeys.seedModificationRateKey p |> UMX.tag) (required ParamKeys.modificationRateKey p |> UMX.tag)
                              (required ParamKeys.orthoRateKey p |> UMX.tag) (required ParamKeys.paraRateKey p |> UMX.tag) (required ParamKeys.selfSymRateKey p |> UMX.tag)
                              (required ParamKeys.mutatorVariantKey p |> MutatorVariant.fromString)
                          |> sorterModelMutator.Simple
            let settings = {
                PoolCount = required ParamKeys.sorterPoolCountKey p; SortersPerPool = required ParamKeys.sorterCountPerPoolKey p
                RetainedParentsPerPool = required ParamKeys.selectedSorterCountPerPoolKey p; ChildrenPerParent = required ParamKeys.sorterChildCountKey p
                Generations = required ParamKeys.generationCountKey p; MutationMod = required ParamKeys.mutationModKey p
                AdjustCeLengthFrequency = required ParamKeys.adjustCeLengthFrequencyKey p; SortedFraction = required ParamKeys.sortedFractionKey p
                PrioritizeNewMutants = required ParamKeys.prioritizeNewMutantsKey p; DistinctSorterHashes = required ParamKeys.distinctSorterHashesKey p
                SelectionMeasure = required ParamKeys.sorterEvalMeasureKey p |> SorterEvalFunctions.fromCompactString }
            let observe snapshot = host.EvolutionReporting |> Option.iter (fun reporting -> reporting.Observe snapshot)
            StandardSgd.runObserved observe (ParallelOptions(MaxDegreeOfParallelism = host.EvaluationParallelism, CancellationToken = host.CancellationToken)) settings mutator models tests prefix
            |> functionData.SorterPoolSet
        host.ResultsDb.SaveIfMissing(poolType, inputs, evolve)
        // A different reporting schedule may need fresh observations even when
        // the final population is already cached. Sampling never changes evolution.
        if host.EvolutionReporting.IsSome && not evolved then evolve () |> ignore
        let pools () = match host.ResultsDb.Load poolType with functionData.SorterPoolSet value -> value | _ -> invalidOp "Expected saved SGD pools."
        host.ResultsDb.SaveIfMissing(modelsType, inputs, fun () ->
            let value = pools ()
            let models = value.Pools |> Array.collect _.Members |> Array.map _.Model |> Array.distinctBy SorterModel.getId
            sorterModelSet.create (UMX.tag (Guid.NewGuid())) models (value.Pools |> Array.map _.RawCeLength |> Array.max |> UMX.tag) |> functionData.SorterModelSet)
        host.ResultsDb.SaveIfMissing(evalType, inputs, fun () ->
            let value = pools ()
            let evaluations = value.Pools |> Array.collect _.Members |> Array.map _.Evaluation |> Array.distinctBy SorterEval.getSorterId
            sorterSetEval.create (UMX.tag (Guid.NewGuid())) (UMX.tag (Guid.NewGuid())) value.TestsId evaluations |> functionData.SorterSetEval)
        host.EvolutionReporting |> Option.iter (fun reporting -> reporting.Complete())

    let standardMsuf4Simple =
        let outputs = [poolType; modelsType; evalType]
        FunctionDefinition.resultsWithInputsValidated "1" keys (outputs |> List.map (fun output -> output, [modelsType; testsType]))
            (outputs |> List.map (fun output -> output, keys |> List.map _.Name)) inputError execute
        |> FunctionDefinition.describe "Run GeneSort's fixed-pool Standard SGD on copied Simple MSUF4 models and sortable tests. Seed pools in model-ID order; mutate every member, evaluate on the full test set, adjust CE limits, and select survivors with duplicate/recovery handling. Save final pools, models, and evaluations. Optional run reporting records full population snapshots and lightweight pool statistics on independent schedules. Channel count and prefix come from setup inputs. GenerationCount counts evolution steps after the initial evaluation. Cancellation restarts unfinished evolution and verifies previously saved reporting samples during replay."
