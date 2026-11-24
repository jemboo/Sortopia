namespace Sortopia.Runs

/// Run-wide controls, independent of the evolutionary parameter sweep.
[<CLIMutable>]
type sgdRunConfig = { SnapshotInterval: int; SnapshotCount: int }

module SgdRun =
    let validate (value: sgdRunConfig) =
        if value.SnapshotInterval < 1 then invalidArg "SnapshotInterval" "SnapshotInterval must be positive."
        if value.SnapshotCount < 0 then invalidArg "SnapshotCount" "SnapshotCount cannot be negative."
        if int64 value.SnapshotInterval * int64 value.SnapshotCount > int64 System.Int32.MaxValue then
            invalidArg "SnapshotCount" "SnapshotInterval × SnapshotCount exceeds the supported generation limit."
        value
    let target value = let value = validate value in value.SnapshotInterval * value.SnapshotCount
    let defaults = { SnapshotInterval = 100; SnapshotCount = 1 }
    let validateExtension before after =
        validate after |> ignore
        if before.SnapshotInterval <> after.SnapshotInterval then
            invalidOp "SnapshotInterval is locked after runParameters are created."
        if after.SnapshotCount < before.SnapshotCount then
            invalidOp "SnapshotCount can only increase after runParameters are created."

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
