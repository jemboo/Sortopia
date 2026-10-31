namespace Sortopia.Models.Sorting.Simple.Ce

open System
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting

[<Struct; CustomEquality; NoComparison>]
type msce =
    private
        { id: Guid<sorterModelId>
          channelCount: int<channelCount>
          ceCodes: int array }
    with
    static member create
            (id: Guid<sorterModelId>)
            (channelCount: int<channelCount>)
            (ceCodes: int array) : msce =
        if isNull ceCodes then nullArg "ceCodes"
        if ceCodes.Length < 1 then
            failwith "Must be at least 1 Ce"
        else if %channelCount < 1 then
            failwith "ChannelCount must be at least 1"
        else
            { id = id; channelCount = channelCount; ceCodes = Array.copy ceCodes }

    member this.Id with get () = this.id
    member this.ChannelCount with get () = this.channelCount
    member this.CeCodes with get () = Array.copy this.ceCodes
    member this.CeLength with get () = (this.ceCodes.Length |> UMX.tag<ceLength>)
    member this.StageLength with get () =
                ((2 * this.CeCodes.Length) / %this.ChannelCount) |> UMX.tag<stageLength>

    member this.toString() =
        sprintf "msce(Id=%A, ChannelCount=%d, SorterLength=%d)"
                (%this.Id)
                (%this.ChannelCount)
                (this.CeCodes.Length)

    override this.Equals(obj) =
        match obj with
        | :? msce as other ->
            this.id = other.id &&
            this.channelCount = other.channelCount &&
            this.ceCodes = other.ceCodes
        | _ -> false

    override this.GetHashCode() =
        hash (this.GetType(), this.id, this.channelCount, this.ceCodes)

    interface IEquatable<msce> with
        member this.Equals(other) =
            this.id = other.id &&
            this.channelCount = other.channelCount &&
            this.ceCodes = other.ceCodes

    // take up to n ces if n is Some, otherwise take all ces
    member this.MakeSorter (maxCeLength: int<ceLength> option) =
        let allCes = this.ceCodes |> Array.map (Ce.fromIndex)
        let ces =
            match maxCeLength with
            | Some n -> allCes |> Array.truncate %n
            | None   -> allCes
        sorter.create (%this.Id |> UMX.tag<sorterId>) this.ChannelCount ces


module Msce =

    let toString (msce: msce) : string =
        sprintf "msce(Id=%A, ChannelCount=%d, SorterLength=%d)"
                (%msce.Id)
                (%msce.ChannelCount)
                (msce.CeCodes.Length)
