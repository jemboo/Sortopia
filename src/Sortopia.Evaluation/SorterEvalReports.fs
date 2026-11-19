namespace Sortopia.Evaluation

open FSharp.UMX
open Sortopia.Reporting

/// Success means zero unsorted inputs in the associated saved test set.
type sorterEvalSummary = {
    ChannelCount: int option
    EvaluatedSorterCount: int
    SuccessfulSorterCount: int
    ObservedSuccessFraction: float option
    SuccessfulMinCeLength: int option
    SuccessfulMedianCeLength: float option
    SuccessfulMinStageLength: int option
    SuccessfulMedianStageLength: float option
}

module SorterEvalReports =
    let private column name kind = { Name = name; ValueType = kind }
    let private integer value = reportValue.Integer (int64 value)
    let private optional make value = value |> Option.map make |> Option.defaultValue reportValue.Missing

    let summarizeEvaluations (evaluations: sorterEval array) =
        let channels = evaluations |> Array.map (SorterEval.getChannelCount >> UMX.untag) |> Array.distinct
        if channels.Length > 1 then invalidArg "source" "An evaluation report cannot mix channel counts within a results file."
        let successful = evaluations |> Array.filter SorterEval.getIsSorted
        let stats measure =
            let sorted = successful |> Array.map measure |> Array.sort
            if sorted.Length = 0 then None, None
            else
                let middle = sorted.Length / 2
                let median = if sorted.Length % 2 = 1 then float sorted[middle] else (float sorted[middle - 1] + float sorted[middle]) / 2.0
                Some sorted[0], Some median
        let minCes, medianCes = stats (SorterEval.getCeLength >> UMX.untag)
        let minStages, medianStages = stats (SorterEval.getStageLength >> UMX.untag)
        { ChannelCount = Array.tryHead channels; EvaluatedSorterCount = evaluations.Length; SuccessfulSorterCount = successful.Length
          ObservedSuccessFraction = if evaluations.Length = 0 then None else Some (float successful.Length / float evaluations.Length)
          SuccessfulMinCeLength = minCes; SuccessfulMedianCeLength = medianCes
          SuccessfulMinStageLength = minStages; SuccessfulMedianStageLength = medianStages }

    let summarize (source: sorterSetEval) = summarizeEvaluations source.SorterEvals

    let summaryColumns = [|
        column "ChannelCount" reportValueType.Integer
        column "EvaluatedSorterCount" reportValueType.Integer
        column "SuccessfulSorterCount" reportValueType.Integer
        column "ObservedSuccessFraction" reportValueType.Number
        column "SuccessfulMinCeLength" reportValueType.Integer
        column "SuccessfulMedianCeLength" reportValueType.Number
        column "SuccessfulMinStageLength" reportValueType.Integer
        column "SuccessfulMedianStageLength" reportValueType.Number |]

    let summaryRow (value: sorterEvalSummary) = ReportRow.create [
        "ChannelCount", optional integer value.ChannelCount
        "EvaluatedSorterCount", integer value.EvaluatedSorterCount
        "SuccessfulSorterCount", integer value.SuccessfulSorterCount
        "ObservedSuccessFraction", optional reportValue.Number value.ObservedSuccessFraction
        "SuccessfulMinCeLength", optional integer value.SuccessfulMinCeLength
        "SuccessfulMedianCeLength", optional reportValue.Number value.SuccessfulMedianCeLength
        "SuccessfulMinStageLength", optional integer value.SuccessfulMinStageLength
        "SuccessfulMedianStageLength", optional reportValue.Number value.SuccessfulMedianStageLength ]

    let detailColumns = [|
        column "SorterId" reportValueType.Identifier
        column "SortableTestsSubsetId" reportValueType.Text
        column "EvaluationType" reportValueType.Text
        column "ChannelCount" reportValueType.Integer
        column "IsSorted" reportValueType.Boolean
        for name in ["UnsortedCount"; "CeLength"; "StageLength"; "SequenceHash"; "LastCeIndex"] do column name reportValueType.Integer
        column "IsReflectionSymmetric" reportValueType.Boolean
        column "StageCrossingsCount" reportValueType.Integer
        column "ReflectiveCount" reportValueType.Integer |]

    let detailRow (value: sorterEval) = ReportRow.create [
        "SorterId", reportValue.Identifier (%(SorterEval.getSorterId value))
        "SortableTestsSubsetId", reportValue.Text (%(SorterEval.getSortableTestsSubsetId value))
        "EvaluationType", reportValue.Text (match value with V1 _ -> "V1" | V2 _ -> "V2" | V3 _ -> "V3")
        "ChannelCount", integer (%(SorterEval.getChannelCount value))
        "IsSorted", reportValue.Boolean (SorterEval.getIsSorted value)
        "UnsortedCount", integer (%(SorterEval.getUnsortedCount value))
        "CeLength", integer (%(SorterEval.getCeLength value))
        "StageLength", integer (%(SorterEval.getStageLength value))
        "SequenceHash", integer (%(SorterEval.getSequenceHash value))
        "LastCeIndex", integer (%(SorterEval.getLastCeIndex value))
        "IsReflectionSymmetric", reportValue.Boolean (%(SorterEval.getIsReflectionSymmetric value))
        "StageCrossingsCount", integer (%(SorterEval.getStageCrossingsCount value))
        "ReflectiveCount", integer (%(SorterEval.getReflectiveCount value)) ]
