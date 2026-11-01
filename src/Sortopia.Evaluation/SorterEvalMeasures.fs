namespace Sortopia.Evaluation

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Reporting
open Sortopia.Sorting


type ceLengthMeasure = private {
    filterUnsorted: bool<filterUnsorted>
    filterReflectionSymmetric: bool<filterReflectionSymmetric>
} with
    static member create (filterUnsorted: bool) (filterReflectionSymmetric: bool) : ceLengthMeasure =
        {
            filterUnsorted = UMX.tag filterUnsorted
            filterReflectionSymmetric = UMX.tag filterReflectionSymmetric
        }

    member this.FilterUnsorted: bool<filterUnsorted> = this.filterUnsorted
    member this.FilterReflectionSymmetric: bool<filterReflectionSymmetric> = this.filterReflectionSymmetric

    member this.ToCompactString() =
        sprintf "CeLen(fUnsorted=%b, fRefl=%b)" (%this.filterUnsorted) (%this.filterReflectionSymmetric)

    static member FromCompactString(s: string) : ceLengthMeasure =
        let fUnsorted = CompactStringParser.parseBool "fUnsorted" s
        let fRefl = CompactStringParser.parseBool "fRefl" s
        ceLengthMeasure.create fUnsorted fRefl


type stageLengthMeasure = private {
    filterUnsorted: bool<filterUnsorted>
    filterReflectionSymmetric: bool<filterReflectionSymmetric>
} with
    static member create (filterUnsorted: bool) (filterReflectionSymmetric: bool) : stageLengthMeasure =
        {
            filterUnsorted = UMX.tag filterUnsorted
            filterReflectionSymmetric = UMX.tag filterReflectionSymmetric
        }

    member this.FilterUnsorted: bool<filterUnsorted> = this.filterUnsorted
    member this.FilterReflectionSymmetric: bool<filterReflectionSymmetric> = this.filterReflectionSymmetric

    member this.ToCompactString() =
        sprintf "StgLen(fUnsorted=%b, fRefl=%b)" (%this.filterUnsorted) (%this.filterReflectionSymmetric)

    static member FromCompactString(s: string) : stageLengthMeasure =
        let fUnsorted = CompactStringParser.parseBool "fUnsorted" s
        let fRefl = CompactStringParser.parseBool "fRefl" s
        stageLengthMeasure.create fUnsorted fRefl


type unsortedCountMeasure = private {
    filterReflectionSymmetric: bool<filterReflectionSymmetric>
} with
    static member create (filterReflectionSymmetric: bool) : unsortedCountMeasure =
        {
            filterReflectionSymmetric = UMX.tag filterReflectionSymmetric
        }

    member this.FilterReflectionSymmetric: bool<filterReflectionSymmetric> = this.filterReflectionSymmetric

    member this.ToCompactString() =
        sprintf "UnsortedCnt(fRefl=%b)" (%this.filterReflectionSymmetric)

    static member FromCompactString(s: string) : unsortedCountMeasure =
        let fRefl = CompactStringParser.parseBool "fRefl" s
        unsortedCountMeasure.create fRefl


type ceStMeasureArgs = {
    StageWeight: float<stageWeight>
    FilterUnsorted: bool<filterUnsorted>
    FilterReflectionSymmetric: bool<filterReflectionSymmetric>
    StageCrossingWeight: float<stageCrossingWeight>
}

type ceStMeasure = private {
    stageWeight: float<stageWeight>
    filterUnsorted: bool<filterUnsorted>
    filterReflectionSymmetric: bool<filterReflectionSymmetric>
    stageCrossingWeight: float<stageCrossingWeight>
} with
    static member create (args: ceStMeasureArgs) : ceStMeasure =
        if not (Double.IsFinite %args.StageWeight && Double.IsFinite %args.StageCrossingWeight) then invalidArg "args" "Weights must be finite."
        {
            stageWeight = args.StageWeight
            filterUnsorted = args.FilterUnsorted
            filterReflectionSymmetric = args.FilterReflectionSymmetric
            stageCrossingWeight = args.StageCrossingWeight
        }

    static member stageBiasedFilterUnsorted () =
            ceStMeasure.create {
                StageWeight = 1.1<stageWeight>
                FilterUnsorted = true |> UMX.tag<filterUnsorted>
                FilterReflectionSymmetric = false |> UMX.tag<filterReflectionSymmetric>
                StageCrossingWeight = 0.0 |> UMX.tag<stageCrossingWeight>
            }

    static member stageBiased () =
            ceStMeasure.create {
                StageWeight = 1.1<stageWeight>
                FilterUnsorted = false |> UMX.tag<filterUnsorted>
                FilterReflectionSymmetric = false |> UMX.tag<filterReflectionSymmetric>
                StageCrossingWeight = 0.0 |> UMX.tag<stageCrossingWeight>
            }

    member this.StageWeight: float<stageWeight> = this.stageWeight
    member this.FilterUnsorted: bool<filterUnsorted> = this.filterUnsorted
    member this.FilterReflectionSymmetric: bool<filterReflectionSymmetric> = this.filterReflectionSymmetric
    member this.StageCrossingWeight: float<stageCrossingWeight> = this.stageCrossingWeight

    member this.ToCompactString() =
        let stWStr = UmxExt.floatToRaw this.stageWeight
        let stcXWStr = UmxExt.floatToRaw this.stageCrossingWeight
        sprintf "CeSt(stW=%s, fUnsorted=%b, fRefl=%b, stcXW=%s)"
            stWStr (%this.filterUnsorted) (%this.filterReflectionSymmetric) stcXWStr

    static member FromCompactString(s: string) : ceStMeasure =
        let stW = CompactStringParser.parseFloat "stW" s
        let fUnsorted = CompactStringParser.parseBool "fUnsorted" s
        let fRefl = CompactStringParser.parseBool "fRefl" s
        let stcXW = CompactStringParser.parseFloat "stcXW" s
        ceStMeasure.create { StageWeight = stW; FilterUnsorted = fUnsorted; FilterReflectionSymmetric = fRefl; StageCrossingWeight = stcXW }


type ceStUcMeasureArgs = {
    StageWeight: float<stageWeight>
    UnsortedWeight: float<unsortedWeight>
    FilterReflectionSymmetric: bool<filterReflectionSymmetric>
}

type ceStUcMeasure = private {
    stageWeight: float<stageWeight>
    unsortedWeight: float<unsortedWeight>
    filterReflectionSymmetric: bool<filterReflectionSymmetric>
} with
    static member create (args: ceStUcMeasureArgs) : ceStUcMeasure =
        if not (Double.IsFinite %args.StageWeight && Double.IsFinite %args.UnsortedWeight) then invalidArg "args" "Weights must be finite."
        {
            stageWeight = args.StageWeight
            unsortedWeight = args.UnsortedWeight
            filterReflectionSymmetric = args.FilterReflectionSymmetric
        }

    member this.StageWeight: float<stageWeight> = this.stageWeight
    member this.UnsortedWeight: float<unsortedWeight> = this.unsortedWeight
    member this.FilterReflectionSymmetric: bool<filterReflectionSymmetric> = this.filterReflectionSymmetric

    member this.ToCompactString() =
        let stWStr = UmxExt.floatToRaw this.stageWeight
        let ucWStr = UmxExt.floatToRaw this.unsortedWeight
        sprintf "CeStUc(stW=%s, ucW=%s, fRefl=%b)" stWStr ucWStr (%this.filterReflectionSymmetric)

    static member FromCompactString(s: string) : ceStUcMeasure =
        let stW = CompactStringParser.parseFloat "stW" s
        let ucW = CompactStringParser.parseFloat "ucW" s
        let fRefl = CompactStringParser.parseBool "fRefl" s
        ceStUcMeasure.create { StageWeight = stW; UnsortedWeight = ucW; FilterReflectionSymmetric = fRefl }


    static member stageBiased () =
            ceStUcMeasure.create {
                StageWeight = 1.1<stageWeight>
                UnsortedWeight = 5.25<unsortedWeight>
                FilterReflectionSymmetric = false |> UMX.tag<filterReflectionSymmetric>
            }


type sorterEvalMeasure =
    | CeLength of ceLengthMeasure
    | StageLength of stageLengthMeasure
    | UnsortedCount of unsortedCountMeasure
    | CeSt of ceStMeasure
    | CeStUc of ceStUcMeasure


module SorterEvalMeasure =

    let stageBiasedFilterUnsorted = ceStMeasure.stageBiasedFilterUnsorted() |> sorterEvalMeasure.CeSt

    let stageBiased_UnsortedCount = ceStUcMeasure.stageBiased() |> sorterEvalMeasure.CeStUc


    let toCompactString (measure: sorterEvalMeasure) : string =
        match measure with
        | CeLength m -> m.ToCompactString()
        | StageLength m -> m.ToCompactString()
        | UnsortedCount m -> m.ToCompactString()
        | CeSt m -> m.ToCompactString()
        | CeStUc m -> m.ToCompactString()

    let fromCompactString (s: string) : sorterEvalMeasure =
        let trimmed = s.Trim()
        if trimmed.StartsWith("CeLen") then
            CeLength (ceLengthMeasure.FromCompactString trimmed)
        elif trimmed.StartsWith("StgLen") then
            StageLength (stageLengthMeasure.FromCompactString trimmed)
        elif trimmed.StartsWith("UnsortedCnt") then
            UnsortedCount (unsortedCountMeasure.FromCompactString trimmed)
        elif trimmed.StartsWith("CeStUc") then
            CeStUc (ceStUcMeasure.FromCompactString trimmed)
        elif trimmed.StartsWith("CeSt") then
            CeSt (ceStMeasure.FromCompactString trimmed)
        else
            failwithf "Unknown compact measure format in '%s'" s

    let fromCompactStringOpt (s: string) : sorterEvalMeasure option =
        try Some (fromCompactString s) with _ -> None

    let toDataTableRecord (measure: sorterEvalMeasure) : dataTableRecord =
        let baseRecord = dataTableRecord.createEmpty()
        match measure with
        | CeLength m ->
            baseRecord
            |> dataTableRecord.addData "MeasureType" "CeLength"
            |> dataTableRecord.addData "M_FilterUnsorted" (string %m.FilterUnsorted)
            |> dataTableRecord.addData "M_FilterReflectionSymmetric" (string %m.FilterReflectionSymmetric)
        | StageLength m ->
            baseRecord
            |> dataTableRecord.addData "MeasureType" "StageLength"
            |> dataTableRecord.addData "M_FilterUnsorted" (string %m.FilterUnsorted)
            |> dataTableRecord.addData "M_FilterReflectionSymmetric" (string %m.FilterReflectionSymmetric)
        | UnsortedCount m ->
            baseRecord
            |> dataTableRecord.addData "MeasureType" "UnsortedCount"
            |> dataTableRecord.addData "M_FilterUnsorted" "false"
            |> dataTableRecord.addData "M_FilterReflectionSymmetric" (string %m.FilterReflectionSymmetric)
        | CeSt m ->
            baseRecord
            |> dataTableRecord.addData "MeasureType" "CeSt"
            |> dataTableRecord.addData "M_StageLengthWeight" (UmxExt.floatToRaw m.StageWeight)
            |> dataTableRecord.addData "M_StageCrossingWeight" (UmxExt.floatToRaw m.StageCrossingWeight)
            |> dataTableRecord.addData "M_FilterUnsorted" (string %m.FilterUnsorted)
            |> dataTableRecord.addData "M_FilterReflectionSymmetric" (string %m.FilterReflectionSymmetric)
        | CeStUc m ->
            baseRecord
            |> dataTableRecord.addData "MeasureType" "CeStUc"
            |> dataTableRecord.addData "M_StageLengthWeight" (UmxExt.floatToRaw m.StageWeight)
            |> dataTableRecord.addData "M_UnsortedCountWeight" (UmxExt.floatToRaw m.UnsortedWeight)
            |> dataTableRecord.addData "M_FilterUnsorted" "false"
            |> dataTableRecord.addData "M_FilterReflectionSymmetric" (string %m.FilterReflectionSymmetric)


module SorterEvalFunctions =

    let byThreeWeighted
                (ceCountWeight: float)
                (stageLengthWeight: float)
                (stageCrossingWeight: float)
                (eval: sorterEval) : float<sorterEvalScore> =
        let ceCount = float (SorterEval.getCeLength eval)
        let stageLength = float (SorterEval.getStageLength eval)
        let stageCrossing = float (SorterEval.getStageCrossingsCount eval)
        let score = (ceCount * ceCountWeight) + (stageLength * stageLengthWeight) + (stageCrossing * stageCrossingWeight)
        UMX.tag<sorterEvalScore> score

    let byUnsortedCount (m: float) (eval: sorterEval) : float<sorterEvalScore> =
        let uc = SorterEval.getUnsortedCount eval |> float
        let score = if uc <= 0.0 then 0.0 else m * Math.Log uc
        UMX.tag<sorterEvalScore> score

    let getFunctionForMeasure (measure: sorterEvalMeasure) : (sorterEval -> float<sorterEvalScore>) =
        match measure with
        | CeLength _ ->
            SorterEval.getCeLength >> float >> UMX.tag<sorterEvalScore>
        | StageLength _ ->
            SorterEval.getStageLength >> float >> UMX.tag<sorterEvalScore>
        | UnsortedCount _ ->
            byUnsortedCount 1.0
        | CeSt m ->
            byThreeWeighted 1.0 (%m.StageWeight) (%m.StageCrossingWeight)
        | CeStUc m ->
            let ceFunc = SorterEval.getCeLength >> float
            let stageFunc = SorterEval.getStageLength >> float
            let unsortedFunc = byUnsortedCount (%m.UnsortedWeight) >> UMX.untag
            fun eval ->
                let cePart = ceFunc eval * 1.0
                let stagePart = stageFunc eval * (%m.StageWeight)
                let unsortedPart = unsortedFunc eval
                UMX.tag<sorterEvalScore> (cePart + stagePart + unsortedPart)

    let getFilterUnsortedFlag (measure: sorterEvalMeasure) : bool<filterUnsorted> =
        match measure with
        | CeLength m -> m.FilterUnsorted
        | StageLength m -> m.FilterUnsorted
        | CeSt m -> m.FilterUnsorted
        | UnsortedCount _ -> UMX.tag<filterUnsorted> false
        | CeStUc _ -> UMX.tag<filterUnsorted> false

    let getFilterReflectionSymmetricFlag (measure: sorterEvalMeasure) : bool<filterReflectionSymmetric> =
        match measure with
        | CeLength m -> m.FilterReflectionSymmetric
        | StageLength m -> m.FilterReflectionSymmetric
        | UnsortedCount m -> m.FilterReflectionSymmetric
        | CeSt m -> m.FilterReflectionSymmetric
        | CeStUc m -> m.FilterReflectionSymmetric

    let filterEvaluations
                    (measure: sorterEvalMeasure)
                    (items: seq<sorterEval>) : seq<sorterEval> =
        items
        |> Seq.filter (fun se ->
            let passUnsorted =
                not (%getFilterUnsortedFlag measure)
                || (SorterEval.getUnsortedCount se <= 0<sortableCount>)
            let passRefl =
                not (%getFilterReflectionSymmetricFlag measure) ||
                (SorterEval.getIsReflectionSymmetric se |> UMX.untag)
            passUnsorted && passRefl
        )

    let toCompactString (measure: sorterEvalMeasure) : string =
            match measure with
            | CeLength m -> m.ToCompactString()
            | StageLength m -> m.ToCompactString()
            | UnsortedCount m -> m.ToCompactString()
            | CeSt m -> m.ToCompactString()
            | CeStUc m -> m.ToCompactString()


    /// Parses a sorterEvalMeasure from its compact string representation.
    let fromCompactString (s: string) : sorterEvalMeasure =
        let trimmed = s.Trim()
        if trimmed.StartsWith("CeLen") then
            CeLength (ceLengthMeasure.FromCompactString trimmed)
        elif trimmed.StartsWith("StgLen") then
            StageLength (stageLengthMeasure.FromCompactString trimmed)
        elif trimmed.StartsWith("UnsortedCnt") then
            UnsortedCount (unsortedCountMeasure.FromCompactString trimmed)
        elif trimmed.StartsWith("CeStUc") then // Note: check CeStUc before CeSt to prevent false match on prefix
            CeStUc (ceStUcMeasure.FromCompactString trimmed)
        elif trimmed.StartsWith("CeSt") then
            CeSt (ceStMeasure.FromCompactString trimmed)
        else
            failwithf "Unknown compact measure format in '%s'" s

    /// Safe version of fromCompactString returning option.
    let fromCompactStringOpt (s: string) : sorterEvalMeasure option =
        try Some (fromCompactString s) with _ -> None
