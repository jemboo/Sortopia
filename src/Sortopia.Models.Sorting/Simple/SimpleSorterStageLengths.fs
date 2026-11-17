namespace Sortopia.Models.Sorting.Simple

open System
open Sortopia.Models.Sorting
open Sortopia.Sorting

/// Provisional suffix budgets for the probability that a generated network sorts EVERY input.
/// Assumes uniform Simple generators, all entries active, and non-self CEs for Msce.
/// A prefix's depth alone cannot determine its effectiveness. See docs/stage-lengths.md.
module SimpleSorterStageLengths =
    type recommendation = {
        Stages: int<stageLength>
        /// True when interpolated, extrapolated, or provisionally adjusted.
        /// False means a direct reference row, not a guaranteed success probability.
        IsEstimate: bool
        /// False means a planning value only: the generator cannot use this width.
        IsSupported: bool
        Source: string
    }

    // Rows are (channels, 50%, 90%, 99%). Historical values: GeneSort 79fbce09,
    // GeneSort.Project/Params/StageLength.fs, comment tables (NOT its placeholder functions).
    // Copyright (c) 2025 Tom Patterson. GeneSort's MIT license is retained in this repository.
    // Four-channel rows replace inconsistent historical values with 10,000 current-generator
    // samples checked against all 16 binary inputs. Reproduce with tools/CheckStageLengths.fsx.
    let private fullSort = Map.ofList [
        Msce,  [4, 7, 12, 18; 6, 8, 14, 20; 8, 17, 26, 40; 12, 37, 53, 71; 16, 60, 83, 112; 24, 111, 149, 187]
        Mssi,  [4, 5, 9, 15; 6, 12, 17, 19; 8, 22, 33, 49; 12, 43, 62, 75; 16, 67, 93, 133; 24, 120, 162, 215]
        Msrs,  [4, 5, 9, 14; 6, 12, 18, 19; 8, 21, 35, 51; 12, 46, 68, 77; 16, 77, 113, 149; 24, 145, 211, 291]
        Msuf4, [4, 5, 9, 15; 8, 21, 35, 51; 16, 88, 136, 180]
        Msuf6, [6, 9, 16, 19; 12, 52, 73, 79; 24, 231, 332, 388]
    ]

    // A separate problem: two already-sorted halves. These are NOT full-sort measurements.
    // Retained for existing callers and as lower bounds on wide unprefixed full-sort budgets.
    let private halfMerge = Map.ofList [
        Msce, [32, 146, 199, 290; 48, 254, 354, 445; 64, 359, 488, 645; 96, 599, 769, 933
               128, 866, 1119, 1404; 192, 1376, 1715, 2220; 256, 1873, 2407, 2700; 384, 3069, 3628, 4793]
        Mssi, [32, 153, 214, 289; 48, 260, 357, 467; 64, 375, 497, 714; 96, 618, 773, 981
               128, 869, 1091, 1404; 192, 1375, 1742, 2132; 256, 1925, 2456, 3304; 384, 3142, 3712, 4884]
        Msrs, [32, 186, 268, 351; 48, 325, 469, 615; 64, 471, 648, 853; 96, 819, 1125, 1482
               128, 1134, 1521, 1824; 192, 1907, 2402, 2976; 256, 2567, 3208, 3717; 384, 4156, 5068, 5873]
        Msuf4, [32, 272, 368, 398; 64, 1034, 1458, 1901; 128, 3845, 5259, 6818; 256, 12815, 16778, 21553]
        Msuf6, [48, 649, 879, 984; 96, 2722, 3707, 4600; 192, 9585, 12563, 16480; 384, 33700, 40929, 46606]
    ]

    let private quantile percent (_, p50, p90, p99) =
        match percent with
        | 50 -> p50 | 90 -> p90 | 99 -> p99
        | _ -> invalidArg "percent" "Choose 50, 90, or 99."

    let private width (cc, _, _, _) = cc
    let private reference table percent (cc: int<channelCount>) smt =
        table |> Map.find smt |> List.tryFind (fun row -> width row = int cc)
        |> Option.map (fun row -> quantile percent row * 1<stageLength>)

    /// Reference rows only; historical observations have not been recalibrated for current RNGs.
    let tryFullSort99 smt cc = reference fullSort 99 cc smt
    let tryHalfMerge99 smt cc = reference halfMerge 99 cc smt

    let private validate (cc: int<channelCount>) (prefixStageLength: int<stageLength>) =
        if cc < 2<channelCount> then invalidArg "cc" "At least two channels are required."
        if prefixStageLength < 0<stageLength> || prefixStageLength > 5<stageLength> then
            invalidArg "prefixStageLength" "Provisional prefix estimates support zero through five stages."

    let private roundUp value =
        if not (Double.IsFinite value) || value > float Int32.MaxValue then
            invalidArg "cc" "The estimated stage length exceeds the supported integer range."
        max 1 (int (ceil value)) * 1<stageLength>

    // Linear interpolation within the references. Above them, extend the last log-log slope:
    // unfolding costs grow faster than linearly, so a constant last-segment slope understates them.
    let private curve table percent cc smt =
        let rows = Map.find smt table
        let points = rows |> List.map (fun row -> width row, quantile percent row)
        match points |> List.tryFind (fun (w, _) -> w = int cc) with
        | Some (_, stages) -> stages * 1<stageLength>, false, "reference"
        | None ->
            let w = float cc
            let segments = List.pairwise points
            let (w1, s1), (w2, s2) =
                segments |> List.tryFind (fun (_, (upper, _)) -> int cc <= upper)
                |> Option.defaultWith (fun () -> List.last segments)
            let above = int cc > fst (List.last points)
            let value =
                if above then
                    let exponent = max 1.0 (log (float s2 / float s1) / log (float w2 / float w1))
                    float s2 * (w / float w2) ** exponent
                else float s1 + (w - float w1) * float (s2 - s1) / float (w2 - w1)
            roundUp value, true, (if above || int cc < fst points.Head then "extrapolated" else "interpolated")

    let private fullEstimate percent cc smt =
        let baseStages, estimated, basis = curve fullSort percent cc smt
        let mergeFloor =
            if int cc >= width (List.head halfMerge[smt]) then
                let stages, _, _ = curve halfMerge percent cc smt
                stages
            else 1<stageLength>
        let fullStages = max baseStages mergeFloor
        let source =
            if not estimated then
                if cc = 4<channelCount> then "current four-channel sample" else "GeneSort legacy reference"
            elif fullStages > baseStages then basis + "; half-merge lower bound"
            else basis
        fullStages, estimated || fullStages <> baseStages, source

    /// Return the provisional suffix length and its provenance. All requested widths have values,
    /// including unsupported model/width pairs; inspect IsSupported before constructing a generator.
    /// No prefix identity or weights are supplied, so a requested percentage is a target, not a guarantee.
    let recommend percent (cc: int<channelCount>) (prefixStageLength: int<stageLength>) (smt: simpleSorterModelType) =
        validate cc prefixStageLength
        let baseStages, estimated, basis = fullEstimate percent cc smt
        // Independently extrapolated percentile curves can cross far outside the observations.
        let fullStages =
            [50; 90] |> List.filter (fun lower -> lower < percent)
            |> List.fold (fun longest lower ->
                let stages, _, _ = fullEstimate lower cc smt
                max longest stages) baseStages
        let source = if fullStages > baseStages then basis + "; percentile ordering" else basis
        // Deliberately modest credit: no unmeasured exponential prefix speedup is assumed.
        let suffix = max 1<stageLength> (fullStages - prefixStageLength)
        // Observed MSUF4 branch runs fell short even at three times the original budget.
        // Use five times the original suffix at every target and width.
        // Keep the original measurements and the separate half-merge task intact.
        let adjusted = smt = simpleSorterModelType.Msuf4
        if adjusted && int suffix > Int32.MaxValue / 5 then
            invalidArg "cc" "The adjusted stage length exceeds the supported integer range."
        let source = if adjusted then source + "; provisional MSUF4 5x suffix adjustment" else source
        { Stages = if adjusted then suffix * 5 else suffix
          IsEstimate = adjusted || estimated || fullStages <> baseStages || prefixStageLength > 0<stageLength>
          IsSupported = SimpleSorterModelType.supportsChannelCount (int cc) smt
          Source = if prefixStageLength = 0<stageLength> then source else source + "; provisional prefix credit" }

    /// Additional suffix stages targeting 50% fully successful generated networks.
    let stageLength50 (cc: int<channelCount>) (prefixStageLength: int<stageLength>) (smt: simpleSorterModelType) : int<stageLength> =
        (recommend 50 cc prefixStageLength smt).Stages

    /// Additional suffix stages targeting 90% fully successful generated networks.
    let stageLength90 (cc: int<channelCount>) (prefixStageLength: int<stageLength>) (smt: simpleSorterModelType) : int<stageLength> =
        (recommend 90 cc prefixStageLength smt).Stages

    /// Additional suffix stages targeting 99% fully successful generated networks.
    let stageLength99 (cc: int<channelCount>) (prefixStageLength: int<stageLength>) (smt: simpleSorterModelType) : int<stageLength> =
        (recommend 99 cc prefixStageLength smt).Stages

    /// Typed success target used by model-generation parameters and their previews.
    let forSuccessFraction fraction cc prefixStageLength smt =
        recommend (SorterSuccessFraction.percent fraction) cc prefixStageLength smt

    /// Kept separate from arbitrary-input sorting, including the prefix heuristic above.
    let recommendHalfMerge99 (smt: simpleSorterModelType) (cc: int<channelCount>) =
        validate cc 0<stageLength>
        let stages, estimated, basis = curve halfMerge 99 cc smt
        { Stages = stages; IsEstimate = estimated
          IsSupported = SimpleSorterModelType.supportsChannelCount (int cc) smt
          Source = if estimated then basis + " half-merge" else "GeneSort legacy half-merge reference" }
