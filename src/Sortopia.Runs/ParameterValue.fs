namespace Sortopia.Runs

open System
open System.Globalization

type parameterValueType =
    | Text = 0
    | Integer = 1
    | Unsigned64 = 2
    | Floating = 3
    | Boolean = 4
    | Guid = 5

/// Typed defaults are formatted only at the parameter-set boundary.
type parameterValue =
    | TextValue of string
    | IntegerValue of int
    | Unsigned64Value of uint64
    | FloatingValue of float
    | BooleanValue of bool
    | GuidValue of Guid

module ParameterValue =
    let private invariant = CultureInfo.InvariantCulture
    let valueType = function
        | TextValue _ -> parameterValueType.Text
        | IntegerValue _ -> parameterValueType.Integer
        | Unsigned64Value _ -> parameterValueType.Unsigned64
        | FloatingValue _ -> parameterValueType.Floating
        | BooleanValue _ -> parameterValueType.Boolean
        | GuidValue _ -> parameterValueType.Guid
    let typeFor (value: Type) =
        if value = typeof<string> then parameterValueType.Text
        elif value = typeof<int> then parameterValueType.Integer
        elif value = typeof<uint64> then parameterValueType.Unsigned64
        elif value = typeof<float> then parameterValueType.Floating
        elif value = typeof<bool> then parameterValueType.Boolean
        elif value = typeof<Guid> then parameterValueType.Guid
        else invalidArg "value" $"Unsupported parameter type: {value.Name}."
    let label = function
        | parameterValueType.Text -> "Text"
        | parameterValueType.Integer -> "Integer"
        | parameterValueType.Unsigned64 -> "Unsigned 64-bit integer"
        | parameterValueType.Floating -> "Floating point"
        | parameterValueType.Boolean -> "Boolean"
        | parameterValueType.Guid -> "GUID"
        | _ -> "Unknown type"
    let hint = function
        | parameterValueType.Text -> "Text; spaces, line breaks, and blank values are preserved."
        | parameterValueType.Integer -> "Whole number from -2147483648 to 2147483647."
        | parameterValueType.Unsigned64 -> "Whole number from 0 to 18446744073709551615."
        | parameterValueType.Floating -> "Finite number using a decimal point; scientific notation is allowed."
        | parameterValueType.Boolean -> "Choose True or False."
        | parameterValueType.Guid -> "GUID, for example 01234567-89ab-cdef-0123-456789abcdef."
        | _ -> "Choose a supported parameter type."
    let private parsed wrap (success, value) = if success then Some (wrap value) else None
    let tryParse kind (text: string) =
        if isNull text then None
        else
            match kind with
            | parameterValueType.Text -> Some (TextValue text)
            | parameterValueType.Integer -> Int32.TryParse(text, NumberStyles.Integer, invariant) |> parsed IntegerValue
            | parameterValueType.Unsigned64 -> UInt64.TryParse(text, NumberStyles.Integer, invariant) |> parsed Unsigned64Value
            | parameterValueType.Floating ->
                match Double.TryParse(text, NumberStyles.Float, invariant) with
                | true, value when Double.IsFinite value -> Some (FloatingValue value)
                | _ -> None
            | parameterValueType.Boolean -> Boolean.TryParse(text) |> parsed BooleanValue
            | parameterValueType.Guid -> Guid.TryParse(text) |> parsed GuidValue
            | _ -> None
    let format = function
        | TextValue value -> value
        | IntegerValue value -> value.ToString(invariant)
        | Unsigned64Value value -> value.ToString(invariant)
        | FloatingValue value -> value.ToString("R", invariant)
        | BooleanValue value -> string value
        | GuidValue value -> value.ToString("D")
    let validationError kind text =
        if tryParse kind text |> Option.isSome then "" else hint kind
    let parse kind text =
        tryParse kind text |> Option.defaultWith (fun () -> invalidArg "value" (hint kind))

