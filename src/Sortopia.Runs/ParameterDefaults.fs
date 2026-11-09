namespace Sortopia.Runs

open System
open FSharp.UMX
open Sortopia.Core

type parameterDefault =
    private { keyName: string<paramKeyName>; value: parameterValue }
    member this.KeyName = this.keyName
    member this.Value = this.value
    member this.ValueType = ParameterValue.valueType this.value
    member this.FormattedValue = ParameterValue.format this.value

module ParameterDefault =
    let fromText (name: string<paramKeyName>) kind text =
        if String.IsNullOrWhiteSpace (UMX.untag name) then invalidArg "name" "Enter a parameter key."
        match ParamKeys.tryFind name with
        | Some key when key.ValueType <> kind -> invalidArg "kind" $"Parameter '{name}' requires type {ParameterValue.label key.ValueType}."
        | _ -> ()
        { keyName = name; value = ParameterValue.parse kind text }
    let create (key: paramKey<'value>) (value: 'value) =
        fromText key.Name (ParameterValue.typeFor typeof<'value>) (key.Format value)
    let validate (defaults: parameterDefault list) =
        if isNull (box defaults) then nullArg "defaults"
        let names = defaults |> List.map (fun d -> d.KeyName)
        if (Set.ofList names).Count <> names.Length then invalidArg "defaults" "Default parameter keys must be unique."

module ParameterSets =
    let valueType (defaults: parameterDefault list) name =
        defaults |> List.tryFind (fun d -> d.KeyName = name) |> Option.map (fun d -> d.ValueType)
        |> Option.defaultWith (fun () -> ParamKeys.valueType name)
    let validateSpans defaults (spans: parameterSpan list) =
        ParameterDefault.validate defaults
        if isNull (box spans) then nullArg "spans"
        let names = spans |> List.map (fun span -> span.KeyName)
        if (Set.ofList names).Count <> names.Length then invalidArg "spans" "Parameter spans must have unique key names."
        for span in spans do
            if String.IsNullOrWhiteSpace (UMX.untag span.KeyName) || span.Values.IsEmpty then
                invalidArg "spans" "Each parameter span needs a key and at least one value."
            let kind = valueType defaults span.KeyName
            for value in span.Values do
                let error = ParameterValue.validationError kind value
                if error <> "" then invalidArg "spans" $"Parameter '{span.KeyName}': {error}"
    /// Spans override defaults of the same name. Other defaults occur in every combination.
    let create (defaults: parameterDefault list) (spans: parameterSpan list) : parameterSet array =
        validateSpans defaults spans
        let initial = defaults |> List.map (fun d -> d.KeyName, d.FormattedValue) |> Map.ofList
        let count = spans |> List.fold (fun count span -> count * bigint span.Values.Length) 1I
        if count > bigint Array.MaxLength then invalidArg "spans" "The parameter spans produce too many combinations for an array."
        match spans with
        | [] -> [| parameterSet.create initial |]
        | _ ->
            spans |> List.map (fun span ->
                let kind = valueType defaults span.KeyName
                span.KeyName, span.Values |> List.map (ParameterValue.parse kind >> ParameterValue.format))
            |> Combinatorics.cartesianProductMaps
            |> Seq.map (fun values -> values |> Map.fold (fun combined key value -> Map.add key value combined) initial |> parameterSet.create)
            |> Seq.toArray
