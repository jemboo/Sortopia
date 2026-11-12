namespace Sortopia.Runs

open FSharp.UMX

/// A parameter's candidate values, formatted for parameter-set generation.
type parameterSpan =
    private {
        keyName: string<paramKeyName>
        valueType: parameterValueType
        values: string list
    }

    member this.KeyName = this.keyName
    member this.ValueType = this.valueType
    member this.Values = this.values

module ParameterSpan =
    let fromTexts name kind values : parameterSpan =
        if not (System.Enum.IsDefined kind) then invalidArg "kind" "Unknown parameter value type."
        match ParamKeys.tryFind name with
        | Some key when key.ValueType <> kind -> invalidArg "kind" $"Parameter '{name}' requires type {ParameterValue.label key.ValueType}."
        | _ -> ()
        { keyName = name; valueType = kind; values = values }

    let create (key: paramKey<'value>) (values: 'value list) : parameterSpan =
        if isNull (box key) then nullArg "key"
        let kind = ParamKeys.tryFind key.Name |> Option.map _.ValueType |> Option.defaultWith (fun () -> (ParamKey.describe key).ValueType)
        fromTexts key.Name kind (values |> List.map key.Format)
