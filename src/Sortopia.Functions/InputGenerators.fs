namespace Sortopia.Functions

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Catalog
open Sortopia.Models.Inputs
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open FunctionParameters

// Adapted from GeneSort's SortableTestsMakers, SortableTestsExecution and
// SorterModelSetMakers. Copyright (c) 2025 Tom Patterson. See GENESORT-LICENSE.txt.
module internal InputGenerators =
    let queryKeys (keys: paramKeyDescriptor list) = keys |> List.map (fun key -> key.Name)
    let standardKeys = [ParamKey.describe ParamKeys.channelCountKey; ParamKey.describe ParamKeys.sortableDataFormatKey]
    let mergeKeys = [ParamKey.describe ParamKeys.sortableDataFormatKey; ParamKey.describe ParamKeys.mergeLibIdKey]
    let prefixKeys = [ParamKey.describe ParamKeys.sortableDataFormatKey; ParamKey.describe ParamKeys.prefixLibIdKey]
    let modelKeys = [
        ParamKey.describe ParamKeys.channelCountKey; ParamKey.describe ParamKeys.simpleSorterModelTypeKey
        ParamKey.describe ParamKeys.rngTypeKey; ParamKey.describe ParamKeys.excludeSelfCeKey
        ParamKey.describe ParamKeys.stageLengthKey; ParamKey.describe ParamKeys.sorterCountKey; ParamKey.describe ParamKeys.replKey ]
    let prefixModelKeys = [
        ParamKey.describe ParamKeys.sorterModelTypeKey
        ParamKey.describe ParamKeys.simpleSorterModelTypeKey; ParamKey.describe ParamKeys.prefixLibIdKey
        ParamKey.describe ParamKeys.aMaxKey; ParamKey.describe ParamKeys.activationProbabilityKey
        ParamKey.describe ParamKeys.rngTypeKey; ParamKey.describe ParamKeys.excludeSelfCeKey
        ParamKey.describe ParamKeys.stageLengthKey; ParamKey.describe ParamKeys.sorterCountKey; ParamKey.describe ParamKeys.replKey ]

    // Check distinct values per participating key, not the full run's Cartesian product.
    // This also handles partially entered module/project defaults.
    let prefixModelInputError (defaults: parameterBinding list) (spans: parameterSpan list) =
        let values (key: paramKey<'value>) =
            let read value = value |> Option.defaultWith (fun () -> invalidArg "parameters" $"Invalid value for {key.Name}.")
            [ for binding in defaults do
                  if binding.KeyName = key.Name then yield ParameterValue.tryGet<'value> binding.Value |> read
              for span in spans do
                  if span.KeyName = key.Name then
                      for text in span.Values do yield key.TryParse text |> read ] |> List.distinct
        try
            for family in values ParamKeys.sorterModelTypeKey do
                if SorterModelType.fromCaseName family simpleSorterModelType.Msce = sorterModelType.Unknown then
                    invalidArg "SorterModelType" "Unknown cannot generate models. Choose Simple, SimpleR, or SimpleS."
            let modelTypes = values ParamKeys.simpleSorterModelTypeKey |> List.map SimpleSorterModelType.fromString |> List.distinct
            let prefixes = values ParamKeys.prefixLibIdKey
            for prefix in prefixes do
                if PrefixLib.tryGet prefix |> Option.isNone then
                    invalidArg "PrefixLibId" $"PrefixLibId '{PrefixLibId.toString prefix}' is not available in the prefix catalog."
                for modelType in modelTypes do
                    if not (SimpleSorterModelType.supportsChannelCount (int prefix.ChannelCount) modelType) then
                        invalidArg "parameters" $"SimpleSorterModelType '{SimpleSorterModelType.toString modelType}' is incompatible with {PrefixLibId.toString prefix}: it requires {SimpleSorterModelType.channelCountRequirement modelType}."
            values ParamKeys.rngTypeKey |> List.iter (RngType.fromString >> ignore)
            values ParamKeys.excludeSelfCeKey |> ignore
            if values ParamKeys.aMaxKey |> List.exists (fun value -> value < 0) then
                invalidArg "AMax" "AMax must be nonnegative; it bounds SimpleR activators."
            if values ParamKeys.activationProbabilityKey |> List.exists (fun value -> not (Double.IsFinite value) || value < 0.0 || value > 1.0) then
                invalidArg "ActivationProbability" "ActivationProbability must be finite and between 0 and 1; it controls SimpleS activators."
            let stages = values ParamKeys.stageLengthKey
            let counts = values ParamKeys.sorterCountKey
            let replications = values ParamKeys.replKey
            if stages |> List.exists (fun value -> value < 1) then invalidArg "StageLength" "StageLength is the random suffix length and must be at least 1; the fixed prefix is additional."
            if counts |> List.exists (fun value -> value < 1 || value > Array.MaxLength) then invalidArg "SorterCount" "SorterCount must be positive and fit in a model array."
            if replications |> List.exists (fun value -> value < 0) then invalidArg "Repl" "Repl must be nonnegative."
            if not counts.IsEmpty && not replications.IsEmpty then
                let count = int64 (List.max counts)
                if int64 (List.max replications) * count + count - 1L > int64 Int32.MaxValue then
                    invalidArg "parameters" "Repl and SorterCount produce sorter indices beyond the supported range."
            if not stages.IsEmpty && not prefixes.IsEmpty then
                let maxWidth = prefixes |> List.map (fun prefix -> int64 prefix.ChannelCount) |> List.max
                if int64 (List.max stages) * maxWidth > int64 Int32.MaxValue then
                    invalidArg "StageLength" "StageLength and PrefixLibId's channel count exceed the supported suffix-size range."
            ""
        with error -> error.Message

    let private tests model prefix parameters =
        let format = required ParamKeys.sortableDataFormatKey parameters |> SortableDataFormat.fromString
        let tests = SortableTestsModel.makeSortableTests (Guid.NewGuid() |> UMX.tag) model format
        functionData.SortableTests (tests, prefix)

    let standardTests parameters =
        let channels = required ParamKeys.channelCountKey parameters
        if channels < 1 then invalidArg "ChannelCount" "ChannelCount must be positive."
        tests (msasF.create (UMX.tag channels) |> sortableTestsModel.MsasF) [||] parameters

    let mergeTests parameters =
        let library = required ParamKeys.mergeLibIdKey parameters
        let model = msasM.create library.ChannelCount library.MergeDimension mergeSuffixType.NoSuffix library.SorterLibVariant
        let prefix = model.CeArray |> Option.defaultWith (fun () -> invalidArg "MergeLibId" "The merge prefix is unavailable.")
        tests (sortableTestsModel.MsasMi model) prefix parameters

    let prefixTests parameters =
        let library = required ParamKeys.prefixLibIdKey parameters
        let prefix = SorterDataParse.getCeArrayFromPrefixLib library |> Option.defaultWith (fun () -> invalidArg "PrefixLibId" "The prefix is unavailable.")
        tests (msasPfx.create library |> sortableTestsModel.MsasPfx) prefix parameters

    let uniformModels parameters =
        let channels = required ParamKeys.channelCountKey parameters
        let modelType = required ParamKeys.simpleSorterModelTypeKey parameters |> SimpleSorterModelType.fromString
        let rng = required ParamKeys.rngTypeKey parameters |> RngType.fromString |> RngFactory.create
        let excludeSelf = required ParamKeys.excludeSelfCeKey parameters |> UMX.tag<excludeSelfCe>
        let stages = required ParamKeys.stageLengthKey parameters
        let count = required ParamKeys.sorterCountKey parameters
        let repl = required ParamKeys.replKey parameters
        if channels < 1 || stages < 0 || count <= 0 || repl < 0 then
            invalidArg "parameters" "ChannelCount and SorterCount must be positive; StageLength and Repl must be nonnegative."
        let first = int64 repl * int64 count
        if first + int64 count - 1L > int64 Int32.MaxValue then invalidArg "parameters" "The replication's sorter indices exceed the supported range."
        let generator = SimpleSorterModelGen.makeUniform rng (UMX.tag channels) [||] (UMX.tag stages) modelType excludeSelf |> sorterModelGen.Simple
        SorterModelGen.makeSorterModelSetFromIndexSpan (Guid.NewGuid() |> UMX.tag) (UMX.tag (int first)) (UMX.tag count) generator |> functionData.SorterModelSet

    let prefixModels parameters =
        let library = required ParamKeys.prefixLibIdKey parameters
        let prefix = SorterDataParse.getCeArrayFromPrefixLib library |> Option.defaultWith (fun () -> invalidArg "PrefixLibId" "The prefix is unavailable.")
        let modelType = required ParamKeys.simpleSorterModelTypeKey parameters |> SimpleSorterModelType.fromString
        let rng = required ParamKeys.rngTypeKey parameters |> RngType.fromString |> RngFactory.create
        let excludeSelf = required ParamKeys.excludeSelfCeKey parameters |> UMX.tag<excludeSelfCe>
        let stages = required ParamKeys.stageLengthKey parameters |> UMX.tag<stageLength>
        let count = required ParamKeys.sorterCountKey parameters
        let first = int64 (required ParamKeys.replKey parameters) * int64 count |> int
        let baseGenerator = SimpleSorterModelGen.makeUniform rng library.ChannelCount prefix stages modelType excludeSelf
        let generator =
            match SorterModelType.fromCaseName (required ParamKeys.sorterModelTypeKey parameters) modelType with
            | sorterModelType.Simple _ -> sorterModelGen.Simple baseGenerator
            | sorterModelType.SimpleR _ -> simpleSorterModelRGen.create baseGenerator (required ParamKeys.aMaxKey parameters) |> sorterModelGen.SimpleR
            | sorterModelType.SimpleS _ -> simpleSorterModelSGen.create baseGenerator (required ParamKeys.activationProbabilityKey parameters) |> sorterModelGen.SimpleS
            | sorterModelType.Unknown -> invalidArg "SorterModelType" "Unknown cannot generate models. Choose Simple, SimpleR, or SimpleS."
        SorterModelGen.makeSorterModelSetFromIndexSpan (Guid.NewGuid() |> UMX.tag) (UMX.tag first) (UMX.tag count) generator |> functionData.SorterModelSet

    let sourceTests parameters inputs =
        match inputs |> List.choose (function functionData.SortableTests (tests, prefix) -> Some (tests, prefix) | _ -> None) with
        | [tests, prefix] ->
            let channels = required ParamKeys.channelCountKey parameters
            if int (Sortopia.Sorting.Inputs.SortableTests.getChannelCount tests) <> channels then
                invalidArg "ChannelCount" "The source tests and generated models must have the same ChannelCount."
            functionData.SortableTests (tests, prefix)
        | _ -> invalidArg "inputs" "Expected exactly one source SortableTests output."
