namespace Sortopia.Runs

open System
open System.Globalization

/// Relative mode weights. Each distribution is normalized by the model initializer.
type opsWeights = { Ortho: float; Para: float; SelfReflection: float }
type seed6Weights = {
    Ortho1: float; Ortho2: float
    Para1: float; Para2: float; Para3: float; Para4: float
    SelfReflection: float
}
/// Levels run from the seed outwards within a stage; the whole list repeats in every stage.
type unfoldingWeights = { Levels: opsWeights list }

module GenerationWeights =
    let private validate values =
        if values |> List.exists (fun value -> not (Double.IsFinite value) || value < 0.) then
            invalidArg "weights" "Mode weights must be finite and nonnegative."
        if not (values |> List.exists (fun value -> value > 0.)) then
            invalidArg "weights" "Set at least one positive mode weight in each distribution."
    let validateOps (value: opsWeights) =
        if isNull (box value) then nullArg "weights"
        validate [value.Ortho; value.Para; value.SelfReflection]
    let validateSeed6 (value: seed6Weights) =
        if isNull (box value) then nullArg "weights"
        validate [value.Ortho1; value.Ortho2; value.Para1; value.Para2; value.Para3; value.Para4; value.SelfReflection]
    let validateUnfolding (value: unfoldingWeights) =
        if isNull (box value) || isNull (box value.Levels) then nullArg "weights"
        value.Levels |> List.iter validateOps
    let private number (value: string) =
        Double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)
    let private parts (text: string) = text.Split(',') |> Array.map number |> Array.toList
    let parseOps text =
        match parts text with
        | [ortho; para; self] ->
            let value = { Ortho = ortho; Para = para; SelfReflection = self }
            validateOps value
            value
        | _ -> invalidArg "weights" "Enter Ortho, Para, and SelfReflection weights."
    let parseSeed6 text =
        match parts text with
        | [ortho1; ortho2; para1; para2; para3; para4; self] ->
            let value = { Ortho1 = ortho1; Ortho2 = ortho2; Para1 = para1; Para2 = para2; Para3 = para3; Para4 = para4; SelfReflection = self }
            validateSeed6 value
            value
        | _ -> invalidArg "weights" "Enter the seven seed mode weights: Ortho1, Ortho2, Para1–4, SelfReflection."
    let parseUnfolding (text: string) =
        { Levels = if String.IsNullOrWhiteSpace text then [] else text.Split(';') |> Array.map parseOps |> Array.toList }
    let private format values =
        values |> List.map (fun (value: float) -> (if value = 0. then 0. else value).ToString("R", CultureInfo.InvariantCulture)) |> String.concat ","
    let formatOps (value: opsWeights) = format [value.Ortho; value.Para; value.SelfReflection]
    let formatSeed6 (value: seed6Weights) = format [value.Ortho1; value.Ortho2; value.Para1; value.Para2; value.Para3; value.Para4; value.SelfReflection]
    let formatUnfolding (value: unfoldingWeights) = value.Levels |> List.map formatOps |> String.concat ";"
