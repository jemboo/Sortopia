namespace Sortopia.Runs

open System
open FSharp.UMX
open Sortopia.Core

module ParameterSets =
    let valueType (bindings: parameterBinding list) name =
        bindings |> List.tryFind (fun binding -> binding.KeyName = name) |> Option.map (fun binding -> binding.ValueType)
        |> Option.defaultWith (fun () -> ParamKeys.valueType name)
    let validateSpans (bindings: parameterBinding list) (spans: parameterSpan list) =
        ParameterBinding.validateUnique bindings
        if isNull (box spans) then nullArg "spans"
        let names = spans |> List.map (fun span -> span.KeyName)
        if (Set.ofList names).Count <> names.Length then invalidArg "spans" "Parameter spans must have unique key names."
        for span in spans do
            if String.IsNullOrWhiteSpace (UMX.untag span.KeyName) || span.Values.IsEmpty then
                invalidArg "spans" "Each parameter span needs a key and at least one value."
            let kind = valueType bindings span.KeyName
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
                let kind = valueType bindings span.KeyName
                span.KeyName, span.Values |> List.map (ParameterBinding.fromText span.KeyName kind))
            |> Combinatorics.cartesianProductMaps
            |> Seq.map (fun values -> values |> Map.fold (fun (combined: parameterSet) _ binding -> combined.WithBinding(binding)) initial)

    /// Spans replace bindings of the same name. Other bindings occur in every combination.
    let create (bindings: parameterBinding list) (spans: parameterSpan list) : parameterSet array =
        combinations bindings spans |> Seq.toArray

    /// Validate every input, then materialize only the first maxCount combinations, in execution order.
    let preview maxCount (bindings: parameterBinding list) (spans: parameterSpan list) : parameterSet array =
        if maxCount <= 0 then invalidArg "maxCount" "The preview limit must be positive."
        combinations bindings spans |> Seq.truncate maxCount |> Seq.toArray
