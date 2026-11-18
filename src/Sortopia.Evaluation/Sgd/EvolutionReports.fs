namespace Sortopia.Evaluation.Sgd

open System
open FSharp.UMX
open Sortopia.Evaluation
open Sortopia.Reporting

/// Lightweight population statistics: one row per pool and sampled generation.
[<CLIMutable>]
type sgdPoolStatistics = {
    Generation: int; PoolIndex: int; PoolId: Guid
    MemberCount: int; SuccessfulCount: int; SuccessFraction: float
    RawCeLength: int
    MinimumCeLength: int; MeanCeLength: float; StdDevCeLength: float
    MinimumStageLength: int; MeanStageLength: float; StdDevStageLength: float
    MeanStageCrossings: float; MeanReflectiveFraction: float option
    MeanUnsortedCount: float; MeanScore: float; MinimumScore: float
}

/// Sampled population statistics stored as a normal typed database result.
[<CLIMutable>]
type sorterPoolSummarySet = { TestsId: Guid; Rows: sgdPoolStatistics array }

module SorterPoolSummarySet =
    let validate (value: sorterPoolSummarySet) =
        if isNull (box value) || value.TestsId = Guid.Empty || isNull value.Rows || value.Rows.Length = 0 then
            invalidArg "summary" "A pool summary set needs a test identity and sampled rows."
        let mutable previous = -1, -1
        for row in value.Rows do
            if isNull (box row) || row.PoolId = Guid.Empty || row.Generation < 0 || row.PoolIndex < 0
               || row.MemberCount < 1 || row.SuccessfulCount < 0 || row.SuccessfulCount > row.MemberCount
               || (row.Generation, row.PoolIndex) <= previous then invalidArg "summary" "Invalid or unordered pool summary."
            if [row.SuccessFraction; row.MeanCeLength; row.StdDevCeLength; row.MeanStageLength; row.StdDevStageLength;
                row.MeanStageCrossings; row.MeanUnsortedCount; row.MeanScore; row.MinimumScore]
               |> List.exists (Double.IsFinite >> not) then invalidArg "summary" "Nonfinite pool statistic."
            if row.MeanReflectiveFraction |> Option.exists (Double.IsFinite >> not) then invalidArg "summary" "Nonfinite reflective fraction."
            previous <- row.Generation, row.PoolIndex
        value

module EvolutionReports =
    let summarize measure (snapshot: sorterPoolSet) =
        snapshot.Pools |> Array.mapi (fun index pool ->
            let evaluations = pool.Members |> Array.map _.Evaluation
            let stats get =
                let values = evaluations |> Array.map get
                let mean = Array.average values
                Array.min values, mean, sqrt (values |> Array.averageBy (fun value -> (value - mean) ** 2.0))
            let minCe, meanCe, sdCe = stats (SorterEval.getCeLength >> float)
            let minStage, meanStage, sdStage = stats (SorterEval.getStageLength >> float)
            let sorted = evaluations |> Array.filter SorterEval.getIsSorted |> Array.length
            let ratios = evaluations |> Array.choose (fun value ->
                let ces = int (SorterEval.getCeLength value)
                if ces = 0 then None else Some (float (SorterEval.getReflectiveCount value) / float ces))
            let scores = evaluations |> Array.map (SorterEvalFunctions.getFunctionForMeasure measure >> float)
            { Generation = snapshot.Generation; PoolIndex = index; PoolId = pool.Id
              MemberCount = evaluations.Length; SuccessfulCount = sorted; SuccessFraction = float sorted / float evaluations.Length
              RawCeLength = pool.RawCeLength; MinimumCeLength = int minCe; MeanCeLength = meanCe; StdDevCeLength = sdCe
              MinimumStageLength = int minStage; MeanStageLength = meanStage; StdDevStageLength = sdStage
              MeanStageCrossings = evaluations |> Array.averageBy (SorterEval.getStageCrossingsCount >> float)
              MeanReflectiveFraction = if ratios.Length <> evaluations.Length then None else Some (Array.average ratios)
              MeanUnsortedCount = evaluations |> Array.averageBy (SorterEval.getUnsortedCount >> float)
              MeanScore = Array.average scores; MinimumScore = Array.min scores })

    let columns = [|
        for name in ["Generation"; "PoolIndex"; "MemberCount"; "SuccessfulCount"; "RawCeLength"; "MinimumCeLength"; "MinimumStageLength"] do
            { Name = name; ValueType = reportValueType.Integer }
        { Name = "PoolId"; ValueType = reportValueType.Identifier }
        for name in ["SuccessFraction"; "MeanCeLength"; "StdDevCeLength"; "MeanStageLength"; "StdDevStageLength"; "MeanStageCrossings"; "MeanReflectiveFraction"; "MeanUnsortedCount"; "MeanScore"; "MinimumScore"] do
            { Name = name; ValueType = reportValueType.Number } |]

    let row (value: sgdPoolStatistics) = ReportRow.create [
        for name, number in ["Generation", value.Generation; "PoolIndex", value.PoolIndex; "MemberCount", value.MemberCount; "SuccessfulCount", value.SuccessfulCount
                             "RawCeLength", value.RawCeLength; "MinimumCeLength", value.MinimumCeLength; "MinimumStageLength", value.MinimumStageLength] do
            yield name, reportValue.Integer (int64 number)
        yield "PoolId", reportValue.Identifier value.PoolId
        for name, number in ["SuccessFraction", value.SuccessFraction; "MeanCeLength", value.MeanCeLength; "StdDevCeLength", value.StdDevCeLength
                             "MeanStageLength", value.MeanStageLength; "StdDevStageLength", value.StdDevStageLength; "MeanStageCrossings", value.MeanStageCrossings
                             "MeanUnsortedCount", value.MeanUnsortedCount; "MeanScore", value.MeanScore; "MinimumScore", value.MinimumScore] do
            yield name, reportValue.Number number
        yield "MeanReflectiveFraction", value.MeanReflectiveFraction |> Option.map reportValue.Number |> Option.defaultValue reportValue.Missing ]
