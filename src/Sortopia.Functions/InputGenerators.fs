namespace Sortopia.Functions

open System
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Catalog
open Sortopia.Models.Inputs
open FunctionParameters

// Adapted from GeneSort's SortableTestsMakers, SortableTestsExecution and
// SorterModelSetMakers. Copyright (c) 2025 Tom Patterson. See GENESORT-LICENSE.txt.
module internal InputGenerators =
    let queryKeys (keys: paramKeyDescriptor list) = keys |> List.map (fun key -> key.Name)
    let standardKeys = [ParamKey.describe ParamKeys.channelCountKey; ParamKey.describe ParamKeys.sortableDataFormatKey]
    let mergeKeys = [ParamKey.describe ParamKeys.sortableDataFormatKey; ParamKey.describe ParamKeys.mergeLibIdKey]
    let prefixKeys = [ParamKey.describe ParamKeys.sortableDataFormatKey; ParamKey.describe ParamKeys.prefixLibIdKey]

    let evaluationInputError testsKeys modelType defaults spans =
        let values key = inputValues key defaults spans
        let modelError = ModelGenerators.inputError ChannelCountInput modelType defaults spans
        if modelError <> "" then modelError
        else
            try
                values ParamKeys.sortableDataFormatKey |> List.iter (SortableDataFormat.fromString >> ignore)
                let widths = values ParamKeys.channelCountKey
                let has key = testsKeys |> List.exists (fun (descriptor: paramKeyDescriptor) -> descriptor.Name = key)
                let checkWidth name width =
                    if widths |> List.exists ((<>) width) then
                        invalidArg "ChannelCount" $"ChannelCount must match {name}'s {width} channels for every parameter combination."
                if has ParamKeys.mergeLibIdKey.Name then
                    values ParamKeys.mergeLibIdKey |> List.iter (fun library -> checkWidth "MergeLibId" (int library.ChannelCount))
                if has ParamKeys.prefixLibIdKey.Name then
                    values ParamKeys.prefixLibIdKey |> List.iter (fun library -> checkWidth "PrefixLibId" (int library.ChannelCount))
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

    let sourceTests parameters inputs =
        match inputs |> List.choose (function functionData.SortableTests (tests, prefix) -> Some (tests, prefix) | _ -> None) with
        | [tests, prefix] ->
            let channels = required ParamKeys.channelCountKey parameters
            if int (Sortopia.Sorting.Inputs.SortableTests.getChannelCount tests) <> channels then
                invalidArg "ChannelCount" "The source tests and generated models must have the same ChannelCount."
            functionData.SortableTests (tests, prefix)
        | _ -> invalidArg "inputs" "Expected exactly one source SortableTests output."
