namespace Sortopia.Functions

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Catalog
open Sortopia.Sorting.Networks
open Sortopia.Models.Inputs
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Models.Sorting.Simple.Si
open Sortopia.Models.Sorting.Simple.Rs
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.Models.Sorting.Simple.Uf6
open FunctionParameters

/// Sizing shared by execution and saved run-file display metadata.
module ModelGenerationStageLengths =
    let suffixStages withPrefix modelType parameters =
        let width, prefixStages =
            if withPrefix then
                let prefix = required ParamKeys.prefixLibIdKey parameters
                prefix.ChannelCount, prefix.StageLength
            else required ParamKeys.channelCountKey parameters * 1<channelCount>, 0<stageLength>
        (SimpleSorterStageLengths.forSuccessFraction (required ParamKeys.sorterSuccessFractionKey parameters) width prefixStages modelType).Stages

    let forMetadata (metadata: modelGenerationMetadata) parameters =
        suffixStages (metadata.PrefixMode = modelPrefixMode.CatalogPrefix) metadata.Algorithm parameters

    let tryForDefinition (definition: functionDefinition<'executor>) parameters =
        definition.ModelGeneration |> Option.map (fun metadata -> forMetadata metadata parameters)

module internal ModelGenerators =
    let version = function simpleSorterModelType.Msuf4 -> "3" | _ -> "2"
    let algorithmDescription = function
        | simpleSorterModelType.Msce -> "compare-exchange (MSCE)"
        | simpleSorterModelType.Mssi -> "self-inverse (MSSI)"
        | simpleSorterModelType.Msrs -> "reflection-symmetric (MSRS)"
        | simpleSorterModelType.Msuf4 -> "4-channel seed unfolding (MSUF4)"
        | simpleSorterModelType.Msuf6 -> "6-channel seed unfolding (MSUF6)"

    let private commonKeys source = [
        (match source with modelPrefixMode.CatalogPrefix -> ParamKey.describe ParamKeys.prefixLibIdKey | modelPrefixMode.NoPrefix -> ParamKey.describe ParamKeys.channelCountKey)
        ParamKey.describe ParamKeys.aMaxKey; ParamKey.describe ParamKeys.aInitialKey; ParamKey.describe ParamKeys.activationProbabilityKey
        ParamKey.describe ParamKeys.rngTypeKey; ParamKey.describe ParamKeys.sorterSuccessFractionKey
        ParamKey.describe ParamKeys.sorterCountKey; ParamKey.describe ParamKeys.replKey ]
    let msceKeys source = commonKeys source @ [ParamKey.describe ParamKeys.excludeSelfCeKey]
    let mssiKeys source = commonKeys source
    let msrsKeys source = commonKeys source @ [ParamKey.describe ParamKeys.stageWeightsKey]
    let msuf4Keys source = commonKeys source @ [ParamKey.describe ParamKeys.orthoRateKey; ParamKey.describe ParamKeys.paraRateKey; ParamKey.describe ParamKeys.selfSymRateKey]
    let msuf6Keys source = commonKeys source @ [ParamKey.describe ParamKeys.seed6WeightsKey; ParamKey.describe ParamKeys.unfoldingWeightsKey]

    /// Model-generation functions fix the outer model family as part of their catalog identity.
    let familyKeys family keys =
        keys |> List.filter (fun (key: paramKeyDescriptor) ->
            match string key.Name with
            | "AMax" | "AInitial" -> family = modelFamily.SimpleR
            | "ActivationProbability" -> family = modelFamily.SimpleS
            | _ -> true)

    let familyDescription source modelType family =
        let prefix = match source with
                     | modelPrefixMode.CatalogPrefix -> "with a fixed library prefix and a generated suffix"
                     | modelPrefixMode.NoPrefix -> "with no fixed prefix"
        let activators =
            match family with
            | modelFamily.SimpleR -> "Every suffix activator starts at AInitial, within [-AMax, AMax]; only positive activators enable entries."
            | modelFamily.SimpleS -> "ActivationProbability controls Boolean suffix activators."
            | modelFamily.Simple -> "All generated suffix entries are enabled."
        let rates = if modelType = simpleSorterModelType.Msuf4 then
                        " OrthoRate, ParaRate, and SelfSymRate are normalized together and used for the seed and every unfolding level. The channel count determines the number of levels."
                    else ""
        $"Generate {family} {algorithmDescription modelType} sorter models {prefix}. SorterSuccessFraction selects a provisional 50%%, 90%%, or 99%% whole-network success target; channel count, prefix depth, and algorithm determine the suffix stage budget. Custom weights and inactive entries can change the actual success rate. {activators}{rates} Repl selects a reproducible batch."

    // Check every participating value, including partial defaults, without expanding unrelated spans.
    let inputError source modelType (defaults: parameterBinding list) (spans: parameterSpan list) =
        let values key = inputValues key defaults spans
        try
            let widthKey = match source with modelPrefixMode.CatalogPrefix -> "PrefixLibId" | modelPrefixMode.NoPrefix -> "ChannelCount"
            let dimensions =
                match source with
                | modelPrefixMode.CatalogPrefix ->
                    values ParamKeys.prefixLibIdKey |> List.map (fun prefix ->
                        if PrefixLib.tryGet prefix |> Option.isNone then
                            invalidArg "PrefixLibId" $"PrefixLibId '{PrefixLibId.toString prefix}' is not available in the prefix catalog."
                        int prefix.ChannelCount, int prefix.StageLength)
                | modelPrefixMode.NoPrefix -> values ParamKeys.channelCountKey |> List.map (fun width -> width, 0)
            let widths = dimensions |> List.map fst
            for width in widths do
                if not (SimpleSorterModelType.supportsChannelCount width modelType) then
                    invalidArg widthKey $"The {SimpleSorterModelType.toString modelType} function is incompatible with {widthKey} ({width} channels): it requires {SimpleSorterModelType.channelCountRequirement modelType}."
            values ParamKeys.rngTypeKey |> List.iter (RngType.fromString >> ignore)
            values ParamKeys.excludeSelfCeKey |> ignore
            values ParamKeys.stageWeightsKey |> List.iter GenerationWeights.validateOps
            if modelType = simpleSorterModelType.Msuf4 then
                let rateKeys = [ParamKeys.orthoRateKey; ParamKeys.paraRateKey; ParamKeys.selfSymRateKey]
                for key in rateKeys do
                    if values key |> List.exists (fun value -> not (Double.IsFinite value) || value < 0.) then
                        invalidArg (string key.Name) $"{key.Name} must be finite and nonnegative. MSUF4 rates are normalized together."
                // A Cartesian product contains an all-zero choice exactly when each span/default contains zero.
                if rateKeys |> List.forall (fun key -> values key |> List.contains 0.) then
                    invalidArg "rates" "At least one of OrthoRate, ParaRate, and SelfSymRate must be positive for every parameter combination."
            values ParamKeys.seed6WeightsKey |> List.iter GenerationWeights.validateSeed6
            for weights in values ParamKeys.unfoldingWeightsKey do
                GenerationWeights.validateUnfolding weights
                for width in widths do
                    let levels =
                        match modelType with
                        | simpleSorterModelType.Msuf6 -> Msuf6RandGen.unfoldingWidths width
                        | _ -> []
                    if weights.Levels.Length <> levels.Length then
                        invalidArg "UnfoldingWeights" $"{widthKey} has {width} channels: {SimpleSorterModelType.toString modelType} needs {levels.Length} unfolding levels, but UnfoldingWeights has {weights.Levels.Length}. These levels repeat in every stage. All width and weight span combinations must match."
            if values ParamKeys.aMaxKey |> List.exists (fun value -> value < 0) then
                invalidArg "AMax" "AMax must be nonnegative; it bounds SimpleR activators."
            for initial in values ParamKeys.aInitialKey do
                for maximum in values ParamKeys.aMaxKey do
                    if int64 initial < -int64 maximum || int64 initial > int64 maximum then
                        invalidArg "AInitial" "AInitial must be within [-AMax, AMax] for every parameter combination."
            if values ParamKeys.activationProbabilityKey |> List.exists (fun value -> not (Double.IsFinite value) || value < 0. || value > 1.) then
                invalidArg "ActivationProbability" "ActivationProbability must be finite and between 0 and 1; it controls SimpleS activators."
            let budgets = [
                for width, prefix in dimensions do
                    // Validate dimensions even when the target is still missing from partial defaults.
                    SimpleSorterStageLengths.forSuccessFraction sorterSuccessFraction.pct50 (width * 1<channelCount>) (prefix * 1<stageLength>) modelType |> ignore
                    for fraction in values ParamKeys.sorterSuccessFractionKey do
                        let recommendation = SimpleSorterStageLengths.forSuccessFraction fraction (width * 1<channelCount>) (prefix * 1<stageLength>) modelType
                        yield width, int recommendation.Stages ]
            let counts = values ParamKeys.sorterCountKey
            let replications = values ParamKeys.replKey
            if counts |> List.exists (fun value -> value < 1 || value > Array.MaxLength) then invalidArg "SorterCount" "SorterCount must be positive and fit in a model array."
            if replications |> List.exists (fun value -> value < 0) then invalidArg "Repl" "Repl must be nonnegative."
            if not counts.IsEmpty && not replications.IsEmpty then
                let count = int64 (List.max counts)
                if int64 (List.max replications) * count + count - 1L > int64 Int32.MaxValue then
                    invalidArg "parameters" "Repl and SorterCount produce sorter indices beyond the supported range."
            if budgets |> List.exists (fun (width, stages) -> int64 stages * int64 width > int64 Int32.MaxValue) then
                invalidArg "SorterSuccessFraction" $"The suggested stage length and {widthKey}'s channel count exceed the supported suffix-size range."
            ""
        with error -> error.Message

    let private configuration source modelType parameters =
        let width, prefix =
            match source with
            | modelPrefixMode.CatalogPrefix ->
                let library = required ParamKeys.prefixLibIdKey parameters
                let prefix = SorterDataParse.getCeArrayFromPrefixLib library |> Option.defaultWith (fun () -> invalidArg "PrefixLibId" "The prefix is unavailable.")
                library.ChannelCount, prefix
            | modelPrefixMode.NoPrefix -> required ParamKeys.channelCountKey parameters |> UMX.tag<channelCount>, [||]
        let rng = required ParamKeys.rngTypeKey parameters |> RngType.fromString |> RngFactory.create
        let stages = ModelGenerationStageLengths.suffixStages (source = modelPrefixMode.CatalogPrefix) modelType parameters
        rng, width, prefix, stages
    let private ops (value: opsWeights) = OpsGenerationProbabilities.fromWeights value.Ortho value.Para value.SelfReflection
    let private unfolding parameters = (required ParamKeys.unfoldingWeightsKey parameters).Levels |> List.map ops
    let private models family baseGenerator parameters =
        let generator =
            match family with
            | modelFamily.Simple -> sorterModelGen.Simple baseGenerator
            | modelFamily.SimpleR -> simpleSorterModelRGen.create baseGenerator (required ParamKeys.aMaxKey parameters) (required ParamKeys.aInitialKey parameters) |> sorterModelGen.SimpleR
            | modelFamily.SimpleS -> simpleSorterModelSGen.create baseGenerator (required ParamKeys.activationProbabilityKey parameters) |> sorterModelGen.SimpleS
        let count = required ParamKeys.sorterCountKey parameters
        let first = int64 (required ParamKeys.replKey parameters) * int64 count |> int
        SorterModelGen.makeSorterModelSetFromIndexSpan (Guid.NewGuid() |> UMX.tag) (UMX.tag first) (UMX.tag count) generator |> functionData.SorterModelSet

    let msce source family parameters =
        let rng, width, prefix, stages = configuration source simpleSorterModelType.Msce parameters
        let generator = MsceRandGen.create rng width prefix stages (required ParamKeys.excludeSelfCeKey parameters |> UMX.tag) |> SmmMsceRandGen
        models family generator parameters
    let mssi source family parameters =
        let rng, width, prefix, stages = configuration source simpleSorterModelType.Mssi parameters
        let generator = MssiRandGen.create rng width prefix stages |> SmmMssiRandGen
        models family generator parameters
    let msrs source family parameters =
        let rng, width, prefix, stages = configuration source simpleSorterModelType.Msrs parameters
        let generator = MsrsRandGen.create rng width prefix stages (required ParamKeys.stageWeightsKey parameters |> ops) |> SmmMsrsRandGen
        models family generator parameters
    let msuf4 source family parameters =
        let rng, width, prefix, stages = configuration source simpleSorterModelType.Msuf4 parameters
        let rates = OpsGenerationProbabilities.fromWeights (required ParamKeys.orthoRateKey parameters)
                        (required ParamKeys.paraRateKey parameters) (required ParamKeys.selfSymRateKey parameters)
        let levels = Msuf4RandGen.unfoldingWidths (int width) |> List.map (fun _ -> rates)
        let generator = Msuf4RandGen.create rng width prefix stages rates levels |> SmmMsuf4RandGen
        models family generator parameters
    let msuf6 source family parameters =
        let rng, width, prefix, stages = configuration source simpleSorterModelType.Msuf6 parameters
        let seed = required ParamKeys.seed6WeightsKey parameters
        let probabilities = Seed6GenerationProbabilities.fromWeights seed.Ortho1 seed.Ortho2 seed.Para1 seed.Para2 seed.Para3 seed.Para4 seed.SelfReflection
        let generator = Msuf6RandGen.create rng width prefix stages probabilities (unfolding parameters) |> SmmMsuf6RandGen
        models family generator parameters
