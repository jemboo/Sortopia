namespace Sortopia.Functions

open System
open Sortopia.Runs
open Sortopia.Models.Sorting
open Sortopia.Evaluation
open Sortopia.Evaluation.Sgd
open FunctionParameters

/// Required inputs, defaults and validation for Standard MSUF4 SGD.
module internal SgdParameterContract =
    let requiredParameters = [
        // Initial pool construction and parent selection.
        ParamKey.describe ParamKeys.sorterPoolCountKey
        ParamKey.describe ParamKeys.sorterCountPerPoolKey
        ParamKey.describe ParamKeys.initialPoolSeedKey
        ParamKey.describe ParamKeys.initialPoolSelectionKey
        ParamKey.describe ParamKeys.selectedSorterCountPerPoolKey
        ParamKey.describe ParamKeys.sorterChildCountKey
        // Evolution and evaluation policy.
        ParamKey.describe ParamKeys.mutationModKey
        ParamKey.describe ParamKeys.adjustCeLengthFrequencyKey
        ParamKey.describe ParamKeys.sortedFractionKey
        ParamKey.describe ParamKeys.prioritizeNewMutantsKey
        ParamKey.describe ParamKeys.distinctSorterHashesKey
        ParamKey.describe ParamKeys.sorterEvalMeasureKey
        // Mutation probabilities and destination weights.
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

    let private defaults context =
        ParameterDefaults.common context @ [
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

    let private editorRules = Map.ofList [
        for key in [ParamKeys.sorterPoolCountKey; ParamKeys.sorterCountPerPoolKey; ParamKeys.sorterChildCountKey; ParamKeys.adjustCeLengthFrequencyKey] do
            ParameterRules.minimum key 1
        for key in [ParamKeys.selectedSorterCountPerPoolKey; ParamKeys.initialPoolSeedKey] do
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
                "Finite nonnegative relative mutation weight. OrthoRate, ParaRate, and SelfSymRate are normalized together to choose the destination mode; at least one must be positive. SeedModificationRate and ModificationRate control the attempt probability. Selecting the current mode leaves it unchanged."
                (fun value -> Double.IsFinite value && value >= 0.)
        ]

    let support =
        { ParameterDefaults.support with
            Defaults = defaults; EditorRules = ParameterRules.combine [ParameterRules.common; editorRules]
            CombinationError = combinationError }
