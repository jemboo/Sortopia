namespace Sortopia.Functions

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Catalog
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Models.Sorting.Simple.Si
open Sortopia.Models.Sorting.Simple.Rs
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.Models.Sorting.Simple.Uf6
open FunctionParameters

type internal modelInputSource = PrefixInput | ChannelCountInput

module internal ModelGenerators =
    let private commonKeys source = [
        ParamKey.describe ParamKeys.sorterModelTypeKey
        (match source with PrefixInput -> ParamKey.describe ParamKeys.prefixLibIdKey | ChannelCountInput -> ParamKey.describe ParamKeys.channelCountKey)
        ParamKey.describe ParamKeys.aMaxKey; ParamKey.describe ParamKeys.activationProbabilityKey
        ParamKey.describe ParamKeys.rngTypeKey; ParamKey.describe ParamKeys.stageLengthKey
        ParamKey.describe ParamKeys.sorterCountKey; ParamKey.describe ParamKeys.replKey ]
    let msceKeys source = commonKeys source @ [ParamKey.describe ParamKeys.excludeSelfCeKey]
    let mssiKeys source = commonKeys source
    let msrsKeys source = commonKeys source @ [ParamKey.describe ParamKeys.stageWeightsKey]
    let msuf4Keys source = commonKeys source @ [ParamKey.describe ParamKeys.seed4WeightsKey; ParamKey.describe ParamKeys.unfoldingWeightsKey]
    let msuf6Keys source = commonKeys source @ [ParamKey.describe ParamKeys.seed6WeightsKey; ParamKey.describe ParamKeys.unfoldingWeightsKey]

    // Check every participating value, including partial defaults, without expanding unrelated spans.
    let inputError source modelType (defaults: parameterBinding list) (spans: parameterSpan list) =
        let values key = inputValues key defaults spans
        try
            for family in values ParamKeys.sorterModelTypeKey do
                if SorterModelType.fromCaseName family modelType = sorterModelType.Unknown then
                    invalidArg "SorterModelType" "Unknown cannot generate models. Choose Simple, SimpleR, or SimpleS."
            let widthKey = match source with PrefixInput -> "PrefixLibId" | ChannelCountInput -> "ChannelCount"
            let widths =
                match source with
                | PrefixInput ->
                    values ParamKeys.prefixLibIdKey |> List.map (fun prefix ->
                        if PrefixLib.tryGet prefix |> Option.isNone then
                            invalidArg "PrefixLibId" $"PrefixLibId '{PrefixLibId.toString prefix}' is not available in the prefix catalog."
                        int prefix.ChannelCount)
                | ChannelCountInput -> values ParamKeys.channelCountKey
            for width in widths do
                if not (SimpleSorterModelType.supportsChannelCount width modelType) then
                    invalidArg widthKey $"The {SimpleSorterModelType.toString modelType} function is incompatible with {widthKey} ({width} channels): it requires {SimpleSorterModelType.channelCountRequirement modelType}."
            values ParamKeys.rngTypeKey |> List.iter (RngType.fromString >> ignore)
            values ParamKeys.excludeSelfCeKey |> ignore
            values ParamKeys.stageWeightsKey |> List.iter GenerationWeights.validateOps
            values ParamKeys.seed4WeightsKey |> List.iter GenerationWeights.validateOps
            values ParamKeys.seed6WeightsKey |> List.iter GenerationWeights.validateSeed6
            for weights in values ParamKeys.unfoldingWeightsKey do
                GenerationWeights.validateUnfolding weights
                for width in widths do
                    let levels =
                        match modelType with
                        | simpleSorterModelType.Msuf4 -> Msuf4RandGen.unfoldingWidths width
                        | simpleSorterModelType.Msuf6 -> Msuf6RandGen.unfoldingWidths width
                        | _ -> []
                    if weights.Levels.Length <> levels.Length then
                        invalidArg "UnfoldingWeights" $"{widthKey} has {width} channels: {SimpleSorterModelType.toString modelType} needs {levels.Length} unfolding levels, but UnfoldingWeights has {weights.Levels.Length}. These levels repeat in every stage. All width and weight span combinations must match."
            if values ParamKeys.aMaxKey |> List.exists (fun value -> value < 0) then
                invalidArg "AMax" "AMax must be nonnegative; it bounds SimpleR activators."
            if values ParamKeys.activationProbabilityKey |> List.exists (fun value -> not (Double.IsFinite value) || value < 0. || value > 1.) then
                invalidArg "ActivationProbability" "ActivationProbability must be finite and between 0 and 1; it controls SimpleS activators."
            let stages = values ParamKeys.stageLengthKey
            let counts = values ParamKeys.sorterCountKey
            let replications = values ParamKeys.replKey
            if stages |> List.exists (fun value -> value < 1) then invalidArg "StageLength" "StageLength must be at least 1. For prefixed models this is the random suffix length; the fixed prefix is additional."
            if modelType = simpleSorterModelType.Msce && List.contains 1 widths then
                if values ParamKeys.excludeSelfCeKey |> List.contains true then
                    invalidArg "ExcludeSelfCe" "Excluding self comparisons requires at least two channels."
                if List.contains 1 stages then
                    invalidArg "StageLength" "A one-channel CE model needs at least two stages to provide one comparator."
            if counts |> List.exists (fun value -> value < 1 || value > Array.MaxLength) then invalidArg "SorterCount" "SorterCount must be positive and fit in a model array."
            if replications |> List.exists (fun value -> value < 0) then invalidArg "Repl" "Repl must be nonnegative."
            if not counts.IsEmpty && not replications.IsEmpty then
                let count = int64 (List.max counts)
                if int64 (List.max replications) * count + count - 1L > int64 Int32.MaxValue then
                    invalidArg "parameters" "Repl and SorterCount produce sorter indices beyond the supported range."
            if not stages.IsEmpty && not widths.IsEmpty then
                let width = widths |> List.map int64 |> List.max
                if int64 (List.max stages) * width > int64 Int32.MaxValue then
                    invalidArg "StageLength" $"StageLength and {widthKey}'s channel count exceed the supported suffix-size range."
            ""
        with error -> error.Message

    let private configuration source parameters =
        let width, prefix =
            match source with
            | PrefixInput ->
                let library = required ParamKeys.prefixLibIdKey parameters
                let prefix = SorterDataParse.getCeArrayFromPrefixLib library |> Option.defaultWith (fun () -> invalidArg "PrefixLibId" "The prefix is unavailable.")
                library.ChannelCount, prefix
            | ChannelCountInput -> required ParamKeys.channelCountKey parameters |> UMX.tag<channelCount>, [||]
        let rng = required ParamKeys.rngTypeKey parameters |> RngType.fromString |> RngFactory.create
        let stages = required ParamKeys.stageLengthKey parameters |> UMX.tag<stageLength>
        rng, width, prefix, stages
    let private ops (value: opsWeights) = OpsGenerationProbabilities.fromWeights value.Ortho value.Para value.SelfReflection
    let private unfolding parameters = (required ParamKeys.unfoldingWeightsKey parameters).Levels |> List.map ops
    let private models modelType baseGenerator parameters =
        let generator =
            match SorterModelType.fromCaseName (required ParamKeys.sorterModelTypeKey parameters) modelType with
            | sorterModelType.Simple _ -> sorterModelGen.Simple baseGenerator
            | sorterModelType.SimpleR _ -> simpleSorterModelRGen.create baseGenerator (required ParamKeys.aMaxKey parameters) |> sorterModelGen.SimpleR
            | sorterModelType.SimpleS _ -> simpleSorterModelSGen.create baseGenerator (required ParamKeys.activationProbabilityKey parameters) |> sorterModelGen.SimpleS
            | sorterModelType.Unknown -> invalidArg "SorterModelType" "Unknown cannot generate models."
        let count = required ParamKeys.sorterCountKey parameters
        let first = int64 (required ParamKeys.replKey parameters) * int64 count |> int
        SorterModelGen.makeSorterModelSetFromIndexSpan (Guid.NewGuid() |> UMX.tag) (UMX.tag first) (UMX.tag count) generator |> functionData.SorterModelSet

    let msce source parameters =
        let rng, width, prefix, stages = configuration source parameters
        let generator = MsceRandGen.create rng width prefix stages (required ParamKeys.excludeSelfCeKey parameters |> UMX.tag) |> SmmMsceRandGen
        models simpleSorterModelType.Msce generator parameters
    let mssi source parameters =
        let rng, width, prefix, stages = configuration source parameters
        let generator = MssiRandGen.create rng width prefix stages |> SmmMssiRandGen
        models simpleSorterModelType.Mssi generator parameters
    let msrs source parameters =
        let rng, width, prefix, stages = configuration source parameters
        let generator = MsrsRandGen.create rng width prefix stages (required ParamKeys.stageWeightsKey parameters |> ops) |> SmmMsrsRandGen
        models simpleSorterModelType.Msrs generator parameters
    let msuf4 source parameters =
        let rng, width, prefix, stages = configuration source parameters
        let generator = Msuf4RandGen.create rng width prefix stages (required ParamKeys.seed4WeightsKey parameters |> ops) (unfolding parameters) |> SmmMsuf4RandGen
        models simpleSorterModelType.Msuf4 generator parameters
    let msuf6 source parameters =
        let rng, width, prefix, stages = configuration source parameters
        let seed = required ParamKeys.seed6WeightsKey parameters
        let probabilities = Seed6GenerationProbabilities.fromWeights seed.Ortho1 seed.Ortho2 seed.Para1 seed.Para2 seed.Para3 seed.Para4 seed.SelfReflection
        let generator = Msuf6RandGen.create rng width prefix stages probabilities (unfolding parameters) |> SmmMsuf6RandGen
        models simpleSorterModelType.Msuf6 generator parameters
