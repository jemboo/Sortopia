namespace Sortopia.Models.Sorting.Simple.Si

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting

[<Struct; CustomEquality; NoComparison>]
type mssi =
    private
        { id: Guid<sorterModelId>
          channelCount: int<channelCount>
          perm_Sis: permSi array }
    with
    static member create
            (id: Guid<sorterModelId>)
            (channelCount: int<channelCount>)
            (perm_Sis: permSi array) : mssi =
        if isNull perm_Sis then nullArg "perm_Sis"
        if perm_Sis.Length < 1 then
            failwith "Must have at least 1 PermSi"
        else if %channelCount < 1 then
            failwith "Width must be at least 1"
        else if perm_Sis |> Array.exists (fun entry -> int entry.Order <> int channelCount) then
            invalidArg "perm_Sis" "Every permutation must match the model's channel count."
        else
            { id = id; channelCount = channelCount; perm_Sis = Array.copy perm_Sis }

    member this.Id with get () = this.id
    member this.ChannelCount with get () = this.channelCount
    member this.CeLength with get () = (this.StageLength * %this.ChannelCount / 2) |> UMX.tag<ceLength>
    member this.StageLength with get () = this.perm_Sis.Length |> UMX.tag<stageLength>
    member this.Perm_Sis with get () = Array.copy this.perm_Sis
    member this.toString() =
        sprintf "mssi(Id=%A, ChannelCount=%d, StageLength=%d)"
                (%this.Id)
                (%this.ChannelCount)
                (this.StageLength)

    override this.Equals(obj) =
        match obj with
        | :? mssi as other ->
            this.id = other.id &&
            this.channelCount = other.channelCount &&
            this.perm_Sis = other.perm_Sis
        | _ -> false

    override this.GetHashCode() =
        hash (this.GetType(), this.id, this.channelCount, this.perm_Sis)

    interface IEquatable<mssi> with
        member this.Equals(other) =
            this.id = other.id &&
            this.channelCount = other.channelCount &&
            this.perm_Sis = other.perm_Sis

    member this.MakeSorter (maxCeLength: int<ceLength> option) =
        let allCes = this.perm_Sis
                        |> Array.collect Ce.fromPermSi
        let ces =
            match maxCeLength with
            | Some n -> allCes |> Array.truncate %n
            | None   -> allCes
        sorter.create (%this.Id |> UMX.tag<sorterId>) this.ChannelCount ces


module Mssi =

    let toString (mssi: mssi) : string =
        sprintf "Mssi(Id=%A, Width=%d, StageLength=%d)"
                (%mssi.Id)
                (%mssi.ChannelCount)
                mssi.StageLength

    let makeSorter (mssi: mssi) : sorter =
        let ces = mssi.perm_Sis
                    |> Array.map (fun psi -> psi |> PermSi.getTwoOrbits)
                    |> Array.collect(id)
                    |> Array.map(fun tbit -> ce.create tbit.First tbit.Second)
        sorter.create (%mssi.Id |> UMX.tag<sorterId>) mssi.ChannelCount ces

