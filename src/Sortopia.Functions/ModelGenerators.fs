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

module internal ModelGenerators =
    // Stable catalog tokens live alongside their concrete constructors; no model-family dispatch is needed.
    let constructors = [
        "simple",
        { Name = "Simple"
          Parameters = []
          Description = "All generated suffix entries are enabled."
          Guidance = "The function fixes the algorithm and Simple model type. All suffix entries are enabled; no activator parameters are required. "
          Validate = fun _ _ -> ()
          Defaults = fun _ -> []
          Create = fun _ -> sorterModelGen.Simple }
        "simple-r",
        { Name = "SimpleR"
          Parameters = [ParamKey.describe ParamKeys.aMaxKey; ParamKey.describe ParamKeys.aInitialKey]
          Description = "Every suffix activator starts at AInitial, within [-AMax, AMax]; only positive activators enable entries."
          Guidance = "The function fixes the algorithm and SimpleR model type. AInitial sets every suffix activator's starting value and must be within [-AMax, AMax]. Only positive activators enable entries. "
          Validate = fun defaults spans ->
              let values key = inputValues key defaults spans
              if values ParamKeys.aMaxKey |> List.exists (fun value -> value < 0) then
                  invalidArg "AMax" "AMax must be nonnegative; it bounds SimpleR activators."
              for initial in values ParamKeys.aInitialKey do
                  for maximum in values ParamKeys.aMaxKey do
                      if int64 initial < -int64 maximum || int64 initial > int64 maximum then
                          invalidArg "AInitial" "AInitial must be within [-AMax, AMax] for every parameter combination."
          Defaults = fun _ -> [ParameterBinding.create ParamKeys.aMaxKey 1; ParameterBinding.create ParamKeys.aInitialKey 0]
          Create = fun parameters generator ->
              simpleSorterModelRGen.create generator (required ParamKeys.aMaxKey parameters) (required ParamKeys.aInitialKey parameters)
              |> sorterModelGen.SimpleR }
        "simple-s",
        { Name = "SimpleS"
          Parameters = [ParamKey.describe ParamKeys.activationProbabilityKey]
          Description = "ActivationProbability controls Boolean suffix activators."
          Guidance = "The function fixes the algorithm and SimpleS model type. ActivationProbability controls Boolean suffix activators. "
          Validate = fun defaults spans ->
              if inputValues ParamKeys.activationProbabilityKey defaults spans
                 |> List.exists (fun value -> not (Double.IsFinite value) || value < 0. || value > 1.) then
                  invalidArg "ActivationProbability" "ActivationProbability must be finite and between 0 and 1; it controls SimpleS activators."
          Defaults = fun _ -> [ParameterBinding.create ParamKeys.activationProbabilityKey 0.5]
          Create = fun parameters generator ->
              simpleSorterModelSGen.create generator (required ParamKeys.activationProbabilityKey parameters)
              |> sorterModelGen.SimpleS } ]

    let keys source (algorithm: simpleModelAlgorithm) (constructor: simpleModelConstructor) = [
        (match source with modelPrefixMode.CatalogPrefix -> ParamKey.describe ParamKeys.prefixLibIdKey | modelPrefixMode.NoPrefix -> ParamKey.describe ParamKeys.channelCountKey)
        yield! constructor.Parameters
        ParamKey.describe ParamKeys.rngTypeKey; ParamKey.describe ParamKeys.sorterSuccessFractionKey
        ParamKey.describe ParamKeys.sorterCountKey; ParamKey.describe ParamKeys.replKey
        yield! algorithm.Parameters ]

    let private description algorithm details source (constructor: simpleModelConstructor) =
        let prefix = match source with
                     | modelPrefixMode.CatalogPrefix -> "with a fixed library prefix and a generated suffix"
                     | modelPrefixMode.NoPrefix -> "with no fixed prefix"
        $"Generate {constructor.Name} {algorithm} sorter models {prefix}. SorterSuccessFraction selects a provisional 50%%, 90%%, or 99%% whole-network success target; channel count, prefix depth, and algorithm determine the suffix stage budget. Custom weights and inactive entries can change the actual success rate. {constructor.Description}{details} Repl selects a reproducible batch."

    let private configuration source parameters =
        let width, prefix =
            match source with
            | modelPrefixMode.CatalogPrefix ->
                let library = required ParamKeys.prefixLibIdKey parameters
                let prefix = SorterDataParse.getCeArrayFromPrefixLib library |> Option.defaultWith (fun () -> invalidArg "PrefixLibId" "The prefix is unavailable.")
                library.ChannelCount, prefix
            | modelPrefixMode.NoPrefix -> required ParamKeys.channelCountKey parameters |> UMX.tag<channelCount>, [||]
        let rng = required ParamKeys.rngTypeKey parameters |> RngType.fromString |> RngFactory.create
        rng, width, prefix
    let private ops (value: opsWeights) = OpsGenerationProbabilities.fromWeights value.Ortho value.Para value.SelfReflection
    let private unfolding parameters = (required ParamKeys.unfoldingWeightsKey parameters).Levels |> List.map ops
    let models source (algorithm: simpleModelAlgorithm) (constructor: simpleModelConstructor) parameters =
        let rng, width, prefix = configuration source parameters
        let stages = algorithm.SuffixStages source parameters
        let generator = algorithm.Create rng width prefix stages parameters |> constructor.Create parameters
        let count = required ParamKeys.sorterCountKey parameters
        let first = int64 (required ParamKeys.replKey parameters) * int64 count |> int
        SorterModelGen.makeSorterModelSetFromIndexSpan (Guid.NewGuid() |> UMX.tag) (UMX.tag first) (UMX.tag count) generator |> sortopiaStorageData.SorterModelSet

    let private equalOps : opsWeights = { Ortho = 1.; Para = 1.; SelfReflection = 1. }
    let private weightGuidance = "Mode weights are shared by every stage; UF6 has separate seed and unfolding-level weights. Every width and unfolding span combination must be compatible."

    // Names and versions are persisted identities. Each entry owns its validation and presentation policy.
    let algorithms : simpleModelAlgorithm list = [
        let modelType = simpleSorterModelType.Msce
        { Name = "msce"
          Parameters = [ParamKey.describe ParamKeys.excludeSelfCeKey]
          Version = "2"
          Description = description "compare-exchange (MSCE)" ""
          InputError = ModelGenerationSupport.inputError modelType (fun _ _ defaults spans ->
              inputValues ParamKeys.excludeSelfCeKey defaults spans |> ignore)
          ParameterSupport = ModelGenerationSupport.create modelType 8
              (fun _ -> [ParameterBinding.create ParamKeys.excludeSelfCeKey true]) weightGuidance
          SuffixStages = ModelGenerationSupport.suffixStages modelType
          Create = fun rng width prefix stages parameters ->
              MsceRandGen.create rng width prefix stages (required ParamKeys.excludeSelfCeKey parameters |> UMX.tag)
              |> SmmMsceRandGen }
        let modelType = simpleSorterModelType.Mssi
        { Name = "mssi"
          Parameters = []
          Version = "2"
          Description = description "self-inverse (MSSI)" ""
          InputError = ModelGenerationSupport.inputError modelType (fun _ _ _ _ -> ())
          ParameterSupport = ModelGenerationSupport.create modelType 8 (fun _ -> []) weightGuidance
          SuffixStages = ModelGenerationSupport.suffixStages modelType
          Create = fun rng width prefix stages _ ->
              MssiRandGen.create rng width prefix stages |> SmmMssiRandGen }
        let modelType = simpleSorterModelType.Msrs
        { Name = "msrs"
          Parameters = [ParamKey.describe ParamKeys.stageWeightsKey]
          Version = "2"
          Description = description "reflection-symmetric (MSRS)" ""
          InputError = ModelGenerationSupport.inputError modelType (fun _ _ defaults spans ->
              inputValues ParamKeys.stageWeightsKey defaults spans |> List.iter GenerationWeights.validateOps)
          ParameterSupport = ModelGenerationSupport.create modelType 8
              (fun _ -> [ParameterBinding.create ParamKeys.stageWeightsKey equalOps]) weightGuidance
          SuffixStages = ModelGenerationSupport.suffixStages modelType
          Create = fun rng width prefix stages parameters ->
              MsrsRandGen.create rng width prefix stages (required ParamKeys.stageWeightsKey parameters |> ops)
              |> SmmMsrsRandGen }
        let modelType = simpleSorterModelType.Msuf4
        { Name = "msuf4"
          Parameters = [ParamKey.describe ParamKeys.orthoRateKey; ParamKey.describe ParamKeys.paraRateKey; ParamKey.describe ParamKeys.selfSymRateKey]
          // v6 uses 5x the original suffix capacity; v5 cached models use 3x.
          Version = "6"
          Description = description "4-channel seed unfolding (MSUF4)"
              " OrthoRate, ParaRate, and SelfSymRate are normalized together and used for the seed and every unfolding level. The channel count determines the number of levels."
          InputError = ModelGenerationSupport.inputError modelType (fun _ _ defaults spans ->
              let values key = inputValues key defaults spans
              let rateKeys = [ParamKeys.orthoRateKey; ParamKeys.paraRateKey; ParamKeys.selfSymRateKey]
              for key in rateKeys do
                  if values key |> List.exists (fun value -> not (Double.IsFinite value) || value < 0.) then
                      invalidArg (string key.Name) $"{key.Name} must be finite and nonnegative. MSUF4 rates are normalized together."
              // A Cartesian product contains an all-zero choice exactly when each input contains zero.
              if rateKeys |> List.forall (fun key -> values key |> List.contains 0.) then
                  invalidArg "rates" "At least one of OrthoRate, ParaRate, and SelfSymRate must be positive for every parameter combination.")
          ParameterSupport = ModelGenerationSupport.create modelType 8
              (fun _ -> [ParameterBinding.create ParamKeys.orthoRateKey 1.; ParameterBinding.create ParamKeys.paraRateKey 1.; ParameterBinding.create ParamKeys.selfSymRateKey 1.])
              "OrthoRate, ParaRate, and SelfSymRate are normalized together and reused for the seed and every unfolding level. At least one rate must be positive. The channel count determines the unfolding levels: 4 channels need zero, 8 need one, 16 need two, and so on."
          SuffixStages = ModelGenerationSupport.suffixStages modelType
          Create = fun rng width prefix stages parameters ->
              let rates = OpsGenerationProbabilities.fromWeights (required ParamKeys.orthoRateKey parameters)
                              (required ParamKeys.paraRateKey parameters) (required ParamKeys.selfSymRateKey parameters)
              let levels = Msuf4RandGen.unfoldingWidths (int width) |> List.map (fun _ -> rates)
              Msuf4RandGen.create rng width prefix stages rates levels |> SmmMsuf4RandGen }
        let modelType = simpleSorterModelType.Msuf6
        { Name = "msuf6"
          Parameters = [ParamKey.describe ParamKeys.seed6WeightsKey; ParamKey.describe ParamKeys.unfoldingWeightsKey]
          Version = "2"
          Description = description "6-channel seed unfolding (MSUF6)" ""
          InputError = ModelGenerationSupport.inputError modelType (fun widthKey widths defaults spans ->
              let values key = inputValues key defaults spans
              values ParamKeys.seed6WeightsKey |> List.iter GenerationWeights.validateSeed6
              for weights in values ParamKeys.unfoldingWeightsKey do
                  GenerationWeights.validateUnfolding weights
                  for width in widths do
                      let levels = Msuf6RandGen.unfoldingWidths width
                      if weights.Levels.Length <> levels.Length then
                          invalidArg "UnfoldingWeights" $"{widthKey} has {width} channels: {SimpleSorterModelType.toString modelType} needs {levels.Length} unfolding levels, but UnfoldingWeights has {weights.Levels.Length}. These levels repeat in every stage. All width and weight span combinations must match.")
          ParameterSupport = ModelGenerationSupport.create modelType 12
              (fun width ->
                  // Partial defaults can contain an incompatible width; leave validation to InputError.
                  let rec levels size = if size >= int64 width then 0 else 1 + levels (size * 2L)
                  [ ParameterBinding.create ParamKeys.seed6WeightsKey
                        { Ortho1 = 1.; Ortho2 = 1.; Para1 = 1.; Para2 = 1.; Para3 = 1.; Para4 = 1.; SelfReflection = 1. }
                    ParameterBinding.create ParamKeys.unfoldingWeightsKey { Levels = List.replicate (levels 6L) equalOps } ]) weightGuidance
          SuffixStages = ModelGenerationSupport.suffixStages modelType
          Create = fun rng width prefix stages parameters ->
              let seed = required ParamKeys.seed6WeightsKey parameters
              let probabilities = Seed6GenerationProbabilities.fromWeights seed.Ortho1 seed.Ortho2 seed.Para1 seed.Para2 seed.Para3 seed.Para4 seed.SelfReflection
              Msuf6RandGen.create rng width prefix stages probabilities (unfolding parameters) |> SmmMsuf6RandGen } ]

    /// A copy query has parameter descriptors, not a model selection. Ask each owner only for its keys.
    let sourceDefaults (context: parameterDefaultContext) =
        let fromOwner (keys: paramKeyDescriptor list) provider =
            let names = keys |> List.map _.Name |> Set.ofList
            let missing = context.Missing |> List.filter (fun key -> Set.contains key.Name names)
            if missing.IsEmpty then [] else provider { context with Missing = missing }
        [ for algorithm in algorithms do
              let support = algorithm.ParameterSupport false modelPrefixMode.NoPrefix ""
              yield! fromOwner algorithm.Parameters support.Defaults
          for _, constructor in constructors do
              yield! fromOwner constructor.Parameters constructor.Defaults ]
        |> ParameterDefaults.select context
