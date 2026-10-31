namespace Sortopia.Sorting.Networks

open FSharp.UMX
open Sortopia.Sorting

/// A disjoint comparator stage. Comparator order is canonical; width participates in equality.
[<StructuralEquality; NoComparison>]
type stage =
    private { ces: ce[]; channelCount: int<channelCount> }

    static member create (ces: ce[]) (channelCount: int<channelCount>) =
        if isNull ces then nullArg "ces"
        if int channelCount < 1 then invalidArg "channelCount" "Channel count must be positive."
        let occupied = Array.zeroCreate<bool> (int channelCount)
        for c in ces do
            let lo, hi = int c.LowChannel, int c.HighChannel
            if hi >= occupied.Length then invalidArg "ces" "Comparator endpoints must be within the stage width."
            if occupied.[lo] || occupied.[hi] then invalidArg "ces" "Stage comparators must use disjoint channels."
            occupied.[lo] <- true
            occupied.[hi] <- true
        { ces = Array.sort ces; channelCount = channelCount }

    member this.Ces = Array.copy this.ces
    member this.ChannelCount = this.channelCount
    member this.AverageCeLength =
        if this.ces.Length = 0 then 0.0
        else this.ces |> Array.averageBy (fun c -> float c.ChannelSpan)
    member this.GetReflectiveCount() = Ce.countReflectiveOrReflected this.channelCount this.ces

module Stage =
    let reflect (stg: stage) = stage.create (stg.Ces |> Array.map (Ce.reflect stg.ChannelCount)) stg.ChannelCount
    let isReflectionSymmetric (stg: stage) = stg = reflect stg

/// An ordered, immutable sequence of stages of one width.
[<StructuralEquality; NoComparison>]
type stageSequence =
    private { channelCount: int<channelCount>; stages: stage[] }

    static member create (stages: stage[]) (channelCount: int<channelCount>) =
        if isNull stages then nullArg "stages"
        if int channelCount < 1 then invalidArg "channelCount" "Channel count must be positive."
        if stages |> Array.exists (fun s -> s.ChannelCount <> channelCount) then
            invalidArg "stages" "All stages must have the sequence channel count."
        { channelCount = channelCount; stages = Array.copy stages }

    member this.ChannelCount = this.channelCount
    member this.Stages = Array.copy this.stages
    member this.StageLength = this.stages.Length |> UMX.tag<stageLength>
    member this.GetReflectiveCount() : int<reflectiveCount> = this.stages |> Array.sumBy (fun s -> s.GetReflectiveCount())
