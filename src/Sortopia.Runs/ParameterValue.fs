namespace Sortopia.Runs

open System
open System.Globalization
open Sortopia.Sorting.Catalog

type parameterValueType =
    | Text = 0
    | Integer = 1
    | Unsigned64 = 2
    | Floating = 3
    | Boolean = 4
    | Guid = 5
    | SorterLibId = 6
    | PrefixLibId = 7
    | MergeLibId = 8
    | OpsWeights = 9
    | Seed6Weights = 10
    | UnfoldingWeights = 11

/// A typed parameter value, independent of its key or role in a run.
type parameterValue =
    | TextValue of string
    | IntegerValue of int
    | Unsigned64Value of uint64
    | FloatingValue of float
    | BooleanValue of bool
    | GuidValue of Guid
    | SorterLibIdValue of sorterLibId
    | PrefixLibIdValue of prefixLibId
    | MergeLibIdValue of mergeLibId
    | OpsWeightsValue of opsWeights
    | Seed6WeightsValue of seed6Weights
    | UnfoldingWeightsValue of unfoldingWeights

module ParameterValue =
    let private invariant = CultureInfo.InvariantCulture
    let valueType = function
        | TextValue _ -> parameterValueType.Text
        | IntegerValue _ -> parameterValueType.Integer
        | Unsigned64Value _ -> parameterValueType.Unsigned64
        | FloatingValue _ -> parameterValueType.Floating
        | BooleanValue _ -> parameterValueType.Boolean
        | GuidValue _ -> parameterValueType.Guid
        | SorterLibIdValue _ -> parameterValueType.SorterLibId
        | PrefixLibIdValue _ -> parameterValueType.PrefixLibId
        | MergeLibIdValue _ -> parameterValueType.MergeLibId
        | OpsWeightsValue _ -> parameterValueType.OpsWeights
        | Seed6WeightsValue _ -> parameterValueType.Seed6Weights
        | UnfoldingWeightsValue _ -> parameterValueType.UnfoldingWeights
    let typeFor (value: Type) =
        if value = typeof<string> then parameterValueType.Text
        elif value = typeof<int> then parameterValueType.Integer
        elif value = typeof<uint64> then parameterValueType.Unsigned64
        elif value = typeof<float> then parameterValueType.Floating
        elif value = typeof<bool> then parameterValueType.Boolean
        elif value = typeof<Guid> then parameterValueType.Guid
        elif value = typeof<sorterLibId> then parameterValueType.SorterLibId
        elif value = typeof<prefixLibId> then parameterValueType.PrefixLibId
        elif value = typeof<mergeLibId> then parameterValueType.MergeLibId
        elif value = typeof<opsWeights> then parameterValueType.OpsWeights
        elif value = typeof<seed6Weights> then parameterValueType.Seed6Weights
        elif value = typeof<unfoldingWeights> then parameterValueType.UnfoldingWeights
        else invalidArg "value" $"Unsupported parameter type: {value.Name}."
    let label = function
        | parameterValueType.Text -> "Text"
        | parameterValueType.Integer -> "Integer"
        | parameterValueType.Unsigned64 -> "Unsigned 64-bit integer"
        | parameterValueType.Floating -> "Floating point"
        | parameterValueType.Boolean -> "Boolean"
        | parameterValueType.Guid -> "GUID"
        | parameterValueType.SorterLibId -> "Sorter library ID"
        | parameterValueType.PrefixLibId -> "Prefix library ID"
        | parameterValueType.MergeLibId -> "Merge library ID"
        | parameterValueType.OpsWeights -> "OPS mode weights"
        | parameterValueType.Seed6Weights -> "Six-channel seed weights"
        | parameterValueType.UnfoldingWeights -> "Unfolding-level weights"
        | _ -> "Unknown type"
    let hint = function
        | parameterValueType.Text -> "Text; spaces, line breaks, and blank values are preserved."
        | parameterValueType.Integer -> "Whole number from -2147483648 to 2147483647."
        | parameterValueType.Unsigned64 -> "Whole number from 0 to 18446744073709551615."
        | parameterValueType.Floating -> "Finite number using a decimal point; scientific notation is allowed."
        | parameterValueType.Boolean -> "Choose True or False."
        | parameterValueType.Guid -> "GUID, for example 01234567-89ab-cdef-0123-456789abcdef."
        | parameterValueType.SorterLibId -> "Choose a sorter catalog entry, or specify a positive channel count and sorter variant."
        | parameterValueType.PrefixLibId -> "Choose a prefix catalog entry, or specify positive channel and stage counts and a prefix variant."
        | parameterValueType.MergeLibId -> "Specify a positive channel count, a positive merge dimension that divides it, and a sorter variant."
        | parameterValueType.OpsWeights -> "Set finite nonnegative Ortho, Para, and SelfReflection weights, with at least one positive weight."
        | parameterValueType.Seed6Weights -> "Set finite nonnegative Ortho1–2, Para1–4, and SelfReflection weights, with at least one positive weight."
        | parameterValueType.UnfoldingWeights -> "Set OPS weights for each unfolding level, from the seed outwards. Each level needs a positive weight. The same levels repeat in every stage."
        | _ -> "Choose a supported parameter type."

    /// Validate the typed value without formatting or reparsing it.
    let validate value =
        if isNull (box value) then nullArg "value"
        match value with
        | OpsWeightsValue value -> GenerationWeights.validateOps value
        | Seed6WeightsValue value -> GenerationWeights.validateSeed6 value
        | UnfoldingWeightsValue value -> GenerationWeights.validateUnfolding value
        | TextValue text when isNull text -> invalidArg "value" (hint parameterValueType.Text)
        | FloatingValue number when not (Double.IsFinite number) -> invalidArg "value" (hint parameterValueType.Floating)
        | SorterLibIdValue id ->
            if isNull (box id) then nullArg "value"
            sorterLibId.create id.ChannelCount id.SorterVariant |> ignore
        | PrefixLibIdValue id ->
            if isNull (box id) then nullArg "value"
            prefixLibId.create id.ChannelCount id.StageLength id.PrefixLibVariant |> ignore
        | MergeLibIdValue id ->
            if isNull (box id) then nullArg "value"
            mergeLibId.create id.ChannelCount id.MergeDimension id.SorterLibVariant |> ignore
        | _ -> ()

    /// Preserve a supported CLR value directly in the corresponding union case.
    let fromTyped (value: 'value) =
        let boxed = box value
        if isNull boxed then nullArg "value"
        let result =
            match typeFor typeof<'value> with
            | parameterValueType.Text -> TextValue (unbox<string> boxed)
            | parameterValueType.Integer -> IntegerValue (unbox<int> boxed)
            | parameterValueType.Unsigned64 -> Unsigned64Value (unbox<uint64> boxed)
            | parameterValueType.Floating -> FloatingValue (unbox<float> boxed)
            | parameterValueType.Boolean -> BooleanValue (unbox<bool> boxed)
            | parameterValueType.Guid -> GuidValue (unbox<Guid> boxed)
            | parameterValueType.SorterLibId -> SorterLibIdValue (unbox<sorterLibId> boxed)
            | parameterValueType.PrefixLibId -> PrefixLibIdValue (unbox<prefixLibId> boxed)
            | parameterValueType.MergeLibId -> MergeLibIdValue (unbox<mergeLibId> boxed)
            | parameterValueType.OpsWeights -> OpsWeightsValue (unbox<opsWeights> boxed)
            | parameterValueType.Seed6Weights -> Seed6WeightsValue (unbox<seed6Weights> boxed)
            | parameterValueType.UnfoldingWeights -> UnfoldingWeightsValue (unbox<unfoldingWeights> boxed)
            | _ -> invalidArg "value" "Unsupported parameter type."
        validate result
        result

    /// Read the stored CLR value only when its type matches exactly; no conversion is attempted.
    let tryGet<'value> value : 'value option =
        let boxed =
            match value with
            | TextValue value -> box value
            | IntegerValue value -> box value
            | Unsigned64Value value -> box value
            | FloatingValue value -> box value
            | BooleanValue value -> box value
            | GuidValue value -> box value
            | SorterLibIdValue value -> box value
            | PrefixLibIdValue value -> box value
            | MergeLibIdValue value -> box value
            | OpsWeightsValue value -> box value
            | Seed6WeightsValue value -> box value
            | UnfoldingWeightsValue value -> box value
        if not (isNull boxed) && boxed.GetType() = typeof<'value> then Some (unbox<'value> boxed) else None

    let private parsed wrap (success, value) = if success then Some (wrap value) else None
    let private catalog parse wrap text =
        try Some (wrap (parse text))
        with
        | :? ArgumentException | :? FormatException | :? OverflowException -> None
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
            | parameterValueType.SorterLibId -> catalog SorterLibId.fromString SorterLibIdValue text
            | parameterValueType.PrefixLibId -> catalog PrefixLibId.fromString PrefixLibIdValue text
            | parameterValueType.MergeLibId -> catalog MergeLibId.fromString MergeLibIdValue text
            | parameterValueType.OpsWeights -> catalog GenerationWeights.parseOps OpsWeightsValue text
            | parameterValueType.Seed6Weights -> catalog GenerationWeights.parseSeed6 Seed6WeightsValue text
            | parameterValueType.UnfoldingWeights -> catalog GenerationWeights.parseUnfolding UnfoldingWeightsValue text
            | _ -> None
    let format = function
        | TextValue value -> value
        | IntegerValue value -> value.ToString(invariant)
        | Unsigned64Value value -> value.ToString(invariant)
        | FloatingValue value -> value.ToString("R", invariant)
        | BooleanValue value -> string value
        | GuidValue value -> value.ToString("D")
        | SorterLibIdValue value -> SorterLibId.toString value
        | PrefixLibIdValue value -> PrefixLibId.toString value
        | MergeLibIdValue value -> MergeLibId.toString value
        | OpsWeightsValue value -> GenerationWeights.formatOps value
        | Seed6WeightsValue value -> GenerationWeights.formatSeed6 value
        | UnfoldingWeightsValue value -> GenerationWeights.formatUnfolding value
    let validationError kind text =
        if tryParse kind text |> Option.isSome then "" else hint kind
    let parse kind text =
        tryParse kind text |> Option.defaultWith (fun () -> invalidArg "value" (hint kind))

