namespace Sortopia.Runs

open FSharp.UMX

/// A parameter's candidate values, formatted for parameter-set generation.
type parameterSpan =
    private {
        keyName: string<paramKeyName>
        values: string list
    }

    member this.KeyName = this.keyName
    member this.Values = this.values

module ParameterSpan =
    let create (key: paramKey<'value>) (values: 'value list) : parameterSpan =
        {
            keyName = key.Name
            values = values |> List.map key.Format
        }
