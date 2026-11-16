namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

/// Resolved query metadata only; this is neither a saved configuration nor a cache key.
/// Unknown keys remain visible so incomplete drafts can be shown without throwing.
type sourceQueryContract = {
    QueryKeys: string<paramKeyName> list
    RequiredParameters: paramKeyDescriptor list
    UnknownKeys: string<paramKeyName> list
}

module SourceContracts =
    let resolve (queryKeys: seq<string<paramKeyName>>) =
        let keys = Seq.toList queryKeys
        { QueryKeys = keys
          RequiredParameters = keys |> List.distinct |> List.choose ParamKeys.tryFind
          UnknownKeys = keys |> List.filter (ParamKeys.tryFind >> Option.isNone) }

    /// Keep first-occurrence ordering, matching defaults, previews, and run validation.
    let combine (contracts: seq<sourceQueryContract>) =
        contracts |> Seq.collect _.QueryKeys |> Seq.distinct |> resolve

    let missingQueryKeys (localKeys: seq<string<paramKeyName>>) (source: sourceQueryContract) =
        localKeys |> Seq.filter (fun key -> not (List.contains key source.QueryKeys)) |> Seq.toList

    let unacceptedQueryKeys (requiredKeys: seq<string<paramKeyName>>) (source: sourceQueryContract) =
        let required = Set.ofSeq requiredKeys
        source.QueryKeys |> List.filter (fun key -> not (Set.contains key required)) |> List.distinct

    /// Execution requires registered, typed keys; draft editors can inspect UnknownKeys.
    let requireParameters (source: sourceQueryContract) =
        match source.UnknownKeys with
        | name :: _ -> invalidArg "parameters" $"Unknown source query parameter '{name}'."
        | [] -> source.RequiredParameters
