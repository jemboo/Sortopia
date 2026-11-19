namespace Sortopia.Functions

open Sortopia.Core
open Sortopia.Evaluation
open Sortopia.Runs
open Sortopia.Sorting

/// Reusable rule builders and common input policies. Model policies belong to their registrations.
module internal ParameterRules =
    let create (key: paramKey<'value>) choices hint validate =
        key.Name, { Choices = choices; Hint = hint; IsValid = key.TryParse >> Option.exists validate }

    let minimum key minimum =
        create key [] $"Enter a whole number of {minimum} or greater." (fun value -> value >= minimum)

    let choice key values format parse =
        create key (values |> List.map format) "Choose a value from the list." (fun value -> parse value |> ignore; true)

    /// Later providers own duplicate keys.
    let combine rules = rules |> Seq.collect Map.toSeq |> Map.ofSeq

    let forKeys (keys: paramKeyDescriptor list) rules =
        let names = keys |> List.map _.Name |> Set.ofList
        rules |> Map.filter (fun name _ -> Set.contains name names)

    let withRules rules (support: functionParameterSupport) =
        { support with EditorRules = combine [support.EditorRules; rules] }

    let common = Map.ofList [
        minimum ParamKeys.channelCountKey 1
        minimum ParamKeys.stageLengthKey 0
        minimum ParamKeys.sorterCountKey 1
        minimum ParamKeys.replKey 0
        choice ParamKeys.rngTypeKey [rngType.Lcg; rngType.Net; rngType.Smx] RngType.toString RngType.fromString
        choice ParamKeys.sortableDataFormatKey
            [sortableDataFormat.BoolArray; sortableDataFormat.IntArray; sortableDataFormat.PackedIntArray
             sortableDataFormat.BitVector256; sortableDataFormat.BitVector512; sortableDataFormat.Int8Vector256; sortableDataFormat.Int8Vector512]
            SortableDataFormat.toString SortableDataFormat.fromString
        choice ParamKeys.sorterEvalTypeKey [sorterEvalType.V1; sorterEvalType.V2; sorterEvalType.V3] SorterEvalType.toString SorterEvalType.fromString
        choice ParamKeys.mergeSuffixTypeKey [mergeSuffixType.NoSuffix; mergeSuffixType.VV_1] MergeSuffixType.toString MergeSuffixType.fromString
        ]
