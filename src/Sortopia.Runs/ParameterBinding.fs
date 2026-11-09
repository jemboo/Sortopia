namespace Sortopia.Runs

open System
open FSharp.UMX

/// A parameter key paired with a typed value, independent of how the binding is used.
type parameterBinding =
    private { keyName: string<paramKeyName>; value: parameterValue }
    member this.KeyName = this.keyName
    member this.Value = this.value
    member this.ValueType = ParameterValue.valueType this.value
    member this.FormattedValue = ParameterValue.format this.value

module ParameterBinding =
    let private validateFields (name: string<paramKeyName>) value =
        if String.IsNullOrWhiteSpace (UMX.untag name) then invalidArg "name" "Enter a parameter key."
        ParameterValue.validate value
        let kind = ParameterValue.valueType value
        match ParamKeys.tryFind name with
        | Some key when key.ValueType <> kind -> invalidArg "value" $"Parameter '{name}' requires type {ParameterValue.label key.ValueType}."
        | _ -> ()

    /// Bind an already typed value, validating the key's declared type.
    let fromValue name value =
        validateFields name value
        { keyName = name; value = value }

    /// Parse an editor or persisted value without assigning it a default/override role.
    let fromText name kind text = fromValue name (ParameterValue.parse kind text)

    let create (key: paramKey<'value>) (value: 'value) =
        if isNull (box key) then nullArg "key"
        fromValue key.Name (ParameterValue.fromTyped value)

    let validate (binding: parameterBinding) =
        if isNull (box binding) then nullArg "binding"
        validateFields binding.KeyName binding.Value

    /// A parameter collection supplies at most one value for each key.
    let validateUnique (bindings: parameterBinding seq) =
        if isNull (box bindings) then nullArg "bindings"
        let names = Collections.Generic.HashSet<string<paramKeyName>>()
        for binding in bindings do
            validate binding
            if not (names.Add binding.KeyName) then invalidArg "bindings" "Parameter keys must be unique."
