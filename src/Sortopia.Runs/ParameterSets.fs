namespace Sortopia.Runs

open System
open FSharp.UMX
open Sortopia.Core

module ParameterSets =
    /// A parameter has exactly one source in a phase.
    let overlapError (defaultKeys: string<paramKeyName> list) (spanKeys: string<paramKeyName> list) =
        let overlap = Set.intersect (Set.ofList defaultKeys) (Set.ofList spanKeys) |> Set.toList
        if overlap.IsEmpty then ""
        else "Parameters cannot have both a default and a span: " + System.String.Join(", ", overlap)
             + ". Remove the span, or remove/exclude the default on the project before using a span."
    /// Query parameters must be explicitly present in the run, even if a default exists.
    let querySpansError (queryKeys: string<paramKeyName> list) (spanKeys: string<paramKeyName> list) =
        let missing = queryKeys |> List.distinct |> List.filter (fun key -> not (List.contains key spanKeys))
        if missing.IsEmpty then ""
        else "Query keys require explicit parameter spans: " + System.String.Join(", ", missing)
             + ". Add a span for each key, using one value if it is fixed. Defaults do not satisfy query keys."
    let validateQuerySpans queryKeys (spans: parameterSpan list) =
        let error = querySpansError queryKeys (spans |> List.map (fun span -> span.KeyName))
        if error <> "" then invalidArg "parameterSpans" error

    /// Shared by the run editor and execution validation; never inserts spans.
    let inputKeysError (requiredKeys: string<paramKeyName> list) queryKeys (defaultKeys: string<paramKeyName> list) spanKeys =
        let provided = Set.ofList (defaultKeys @ spanKeys)
        let required = Set.ofList requiredKeys
        let extra = Set.difference provided required |> Set.toList
        let missing = Set.difference required provided |> Set.toList
        [ if not extra.IsEmpty then
              yield "Parameters not required by the selected function: " + System.String.Join(", ", extra) + "."
          let overlap = overlapError defaultKeys spanKeys
          if overlap <> "" then yield overlap
          let queryError = querySpansError queryKeys spanKeys
          if queryError <> "" then yield queryError
          if not missing.IsEmpty then
              yield "Missing values for required parameters: " + System.String.Join(", ", missing) + ". Supply a default or a parameter span." ]
        |> String.concat " "

    let validateSpans (bindings: parameterBinding list) (spans: parameterSpan list) =
        ParameterBinding.validateUnique bindings
        if isNull (box spans) then nullArg "spans"
        let names = spans |> List.map (fun span -> span.KeyName)
        let overlap = overlapError (bindings |> List.map (fun binding -> binding.KeyName)) names
        if overlap <> "" then invalidArg "spans" overlap
        if (Set.ofList names).Count <> names.Length then invalidArg "spans" "Parameter spans must have unique key names."
        for span in spans do
            if String.IsNullOrWhiteSpace (UMX.untag span.KeyName) || span.Values.IsEmpty then
                invalidArg "spans" "Each parameter span needs a key and at least one value."
            let kind = span.ValueType
            for value in span.Values do
                let error = ParameterValue.validationError kind value
                if error <> "" then invalidArg "spans" $"Parameter '{span.KeyName}': {error}"
    let private combinations (bindings: parameterBinding list) (spans: parameterSpan list) =
        validateSpans bindings spans
        let initial = parameterSet.fromBindings bindings
        let count = spans |> List.fold (fun count span -> count * bigint span.Values.Length) 1I
        if count > bigint Array.MaxLength then invalidArg "spans" "The parameter spans produce too many combinations for an array."
        match spans with
        | [] -> Seq.singleton initial
        | _ ->
            spans |> List.map (fun span ->
                let kind = span.ValueType
                span.KeyName, span.Values |> List.map (ParameterBinding.fromText span.KeyName kind))
            |> Combinatorics.cartesianProductMaps
            |> Seq.map (fun values -> values |> Map.fold (fun (combined: parameterSet) _ binding -> combined.WithBinding(binding)) initial)

    /// Disjoint defaults stay fixed while span values form a Cartesian product.
    let create (bindings: parameterBinding list) (spans: parameterSpan list) : parameterSet array =
        combinations bindings spans |> Seq.toArray

    /// Validate every input, then materialize only the first maxCount combinations, in execution order.
    let preview maxCount (bindings: parameterBinding list) (spans: parameterSpan list) : parameterSet array =
        if maxCount <= 0 then invalidArg "maxCount" "The preview limit must be positive."
        combinations bindings spans |> Seq.truncate maxCount |> Seq.toArray
