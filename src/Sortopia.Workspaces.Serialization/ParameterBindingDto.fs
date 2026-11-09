namespace Sortopia.Workspaces.Serialization

open System
open FSharp.UMX
open Sortopia.Runs

/// A typed parameter binding; its containing property determines its role.
[<CLIMutable>]
type parameterBindingDto = { keyName: string; valueType: int; value: string }

module ParameterBindingDto =
    let fromDomain (value: parameterBinding) : parameterBindingDto =
        ParameterBinding.validate value
        { keyName = UMX.untag value.KeyName; valueType = int value.ValueType; value = value.FormattedValue }
    let toDomain (value: parameterBindingDto) =
        if isNull (box value) then nullArg "binding"
        if not (Enum.IsDefined(typeof<parameterValueType>, value.valueType)) then invalidArg "valueType" "Unknown parameter value type."
        ParameterBinding.fromText (UMX.tag value.keyName) (enum<parameterValueType> value.valueType) value.value
