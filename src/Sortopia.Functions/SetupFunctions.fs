namespace Sortopia.Functions

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Models.Inputs
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open FunctionParameters

// Adapted from GeneSort.Project.V1/Catalogs/Setup/SorterEval:
// SortableTestsMakers.makeStandardTests and SorterModelSetMakers.makeUniformSorterModelSet.
// Copyright (c) 2025 Tom Patterson. See GENESORT-LICENSE.txt.
module SetupFunctions =
    let makeStandardTests : setupFunction = fun parameters _ ->
        try
            let channels = required ParamKeys.channelCountKey parameters |> UMX.tag<channelCount>
            let format = required ParamKeys.sortableDataFormatKey parameters |> SortableDataFormat.fromString
            let model = msasF.create channels |> sortableTestsModel.MsasF
            let tests = SortableTestsModel.makeSortableTests (Guid.NewGuid() |> UMX.tag) model format
            Ok [functionData.SortableTests (tests, [||])]
        with error -> Error error.Message

    let makeUniformSorterModels : setupFunction = fun parameters _ ->
        try
            let channels = required ParamKeys.channelCountKey parameters |> UMX.tag<channelCount>
            let modelType = required ParamKeys.simpleSorterModelTypeKey parameters |> SimpleSorterModelType.fromString
            let rng = required ParamKeys.rngTypeKey parameters |> RngType.fromString |> RngFactory.create
            let excludeSelf = required ParamKeys.excludeSelfCeKey parameters |> UMX.tag<excludeSelfCe>
            let stages = required ParamKeys.stageLengthKey parameters
            let count = required ParamKeys.sorterCountKey parameters
            let repl = required ParamKeys.replKey parameters
            if stages < 0 || count <= 0 || repl < 0 then
                invalidArg "parameters" "StageLength and Repl must be nonnegative; SorterCount must be positive."
            let first = int64 repl * int64 count
            if first + int64 count - 1L > int64 Int32.MaxValue then
                invalidArg "parameters" "The replication's sorter indices exceed the supported range."
            let generator = SimpleSorterModelGen.makeUniform rng channels [||] (UMX.tag stages) modelType excludeSelf |> sorterModelGen.Simple
            let models = SorterModelGen.makeSorterModelSetFromIndexSpan (Guid.NewGuid() |> UMX.tag) (UMX.tag (int first)) (UMX.tag count) generator
            Ok [functionData.SorterModelSet models]
        with error -> Error error.Message
