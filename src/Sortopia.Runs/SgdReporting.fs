namespace Sortopia.Runs

open System
open Sortopia.Core

/// Sampling policy frozen with execution parameters; sampled results are shared by parameter identity.
[<CLIMutable>]
type sgdReportingConfig = { SnapshotSchedule: string; SummarySchedule: string }

module SgdReporting =
    let defaults = { SnapshotSchedule = "Constant(100)"; SummarySchedule = "Constant(1)" }

    let parse text =
        if String.IsNullOrWhiteSpace text || text.Length > 2048 then invalidArg "schedule" "Choose a reporting schedule."
        let value = IntSampleMethod.fromString text
        let rec validate depth = function
            | _ when depth > 16 -> invalidArg "schedule" "Reporting schedules are nested too deeply."
            | Constant step when step > 0 -> ()
            | Exponential (growth, first) when first > 0 && Double.IsFinite growth && growth > 0.0 -> ()
            | Cyclic (inner, count) when count > 0 -> validate (depth + 1) inner
            | Prefixed (count, prefix, main) when count >= 0 -> validate (depth + 1) prefix; validate (depth + 1) main
            | _ -> invalidArg "schedule" "Intervals must be positive; exponential growth must be finite and positive."
        validate 0 value
        value

    let validate value =
        if isNull (box value) then nullArg "reporting"
        parse value.SnapshotSchedule |> ignore
        parse value.SummarySchedule |> ignore
        value

    /// Overflow-safe, bounded sampling. Only scheduled points are included.
    let samples text finalGeneration =
        if finalGeneration < 0 then invalidArg "finalGeneration" "Generation count must be nonnegative."
        let bound = int64 finalGeneration
        let rec generate methodValue start = seq {
            match methodValue with
            | Constant step ->
                let mutable current = start
                while current <= bound do
                    yield int current
                    current <- current + int64 step
            | Exponential (growth, first) ->
                let mutable current, delta = float start, float first
                while Double.IsFinite current && Math.Round current <= float bound do
                    yield int (Math.Round current)
                    current <- current + delta
                    delta <- delta * (1.0 + growth)
            | Cyclic (inner, count) ->
                let mutable current = start
                while current <= bound do
                    let mutable last, produced = current, 0
                    for sample in generate inner current |> Seq.truncate count do
                        yield sample
                        last <- int64 sample
                        produced <- produced + 1
                    current <- if produced < count then bound + 1L else last + 1L
            | Prefixed (count, prefix, main) ->
                let mutable next, produced = start, 0
                for sample in generate prefix start |> Seq.truncate count do
                    yield sample
                    next <- int64 sample + 1L
                    produced <- produced + 1
                if produced = count then yield! generate main next }
        generate (parse text) 0L |> Seq.distinct

    let validateGenerationCount reporting generationCount =
        validate reporting |> ignore
        if generationCount < 0 then invalidArg "generationCount" "GenerationCount must be nonnegative."
        for label, schedule in ["full snapshot", reporting.SnapshotSchedule; "summary", reporting.SummarySchedule] do
            let scheduled =
                match parse schedule with
                | Constant step -> generationCount % step = 0
                | _ -> samples schedule generationCount |> Seq.contains generationCount
            if not scheduled then
                invalidArg "generationCount" $"GenerationCount {generationCount} is not on the {label} schedule {schedule}. Choose a scheduled generation or change the {label} schedule."

    /// Check just this input, rather than expanding the complete parameter sweep.
    let validateGenerationCounts reporting (values: string seq) =
        let values = values |> Seq.distinct |> Seq.toArray
        if values.Length = 0 then invalidArg "generationCount" "Provide GenerationCount to validate the snapshot and summary schedules."
        for text in values do
            let count = ParamKeys.generationCountKey.TryParse text
                        |> Option.defaultWith (fun () -> invalidArg "generationCount" "GenerationCount must be an integer.")
            validateGenerationCount reporting count
