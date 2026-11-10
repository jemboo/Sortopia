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
    let standardKeys = [ParamKey.describe ParamKeys.channelCountKey; ParamKey.describe ParamKeys.sortableDataFormatKey]
    let mergeKeys = [ParamKey.describe ParamKeys.sortableDataFormatKey; ParamKey.describe ParamKeys.mergeLibIdKey]
    let prefixKeys = [ParamKey.describe ParamKeys.sortableDataFormatKey; ParamKey.describe ParamKeys.prefixLibIdKey]
    let modelKeys = [
        ParamKey.describe ParamKeys.channelCountKey; ParamKey.describe ParamKeys.simpleSorterModelTypeKey
        ParamKey.describe ParamKeys.rngTypeKey; ParamKey.describe ParamKeys.excludeSelfCeKey
        ParamKey.describe ParamKeys.stageLengthKey; ParamKey.describe ParamKeys.sorterCountKey; ParamKey.describe ParamKeys.replKey ]

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

    let attempt factory = try Ok (factory ()) with error -> Error error.Message

    let sourceTests parameters inputs =
        match inputs |> List.choose (function functionData.SortableTests (tests, prefix) -> Some (tests, prefix) | _ -> None) with
        | [tests, prefix] ->
            let channels = required ParamKeys.channelCountKey parameters
            if int (Sortopia.Sorting.Inputs.SortableTests.getChannelCount tests) <> channels then
                invalidArg "ChannelCount" "The source tests and generated models must have the same ChannelCount."
            functionData.SortableTests (tests, prefix)
        | _ -> invalidArg "inputs" "Expected exactly one source SortableTests output."
