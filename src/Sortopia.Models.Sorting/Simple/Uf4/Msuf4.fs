namespace Sortopia.Models.Sorting.Simple.Uf4

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting

/// Represents a collection of TwoOrbitUnfolder4 instances with a consistent channel count.
[<Struct; CustomEquality; NoComparison>]
type msuf4 =
    private
        { id: Guid<sorterModelId>
          channelCount: int<channelCount>
          twoOrbitUnfolder4s: twoOrbitUf4 array }
    with
    /// Creates an Msuf4 instance with the specified ID, channel count, and array of TwoOrbitUnfolder4 instances.
    /// Throws an exception if the array is empty, width is less than 1, or any TwoOrbitUnfolder4 has a mismatched order.
    static member create
            (id: Guid<sorterModelId>)
            (channelCount: int<channelCount>)
            (twoOrbitUf4s: twoOrbitUf4 array) : msuf4 =
        if isNull twoOrbitUf4s then nullArg "twoOrbitUf4s"
        if twoOrbitUf4s.Length < 1 then
            failwith $"Must have at least 1 TwoOrbitUnfolder4, got %d{twoOrbitUf4s.Length}"
        else if %channelCount < 1 then
            failwith $"ChannelCount must be at least 1, got {%channelCount}"
        else if twoOrbitUf4s |> Array.exists (fun tou -> tou.Order <> %channelCount) then
            failwith $"All TwoOrbitUnfolder4 must have order {%channelCount}"
        else
            { id = id; channelCount = channelCount; twoOrbitUnfolder4s = Array.copy twoOrbitUf4s }

    member this.Id with get () = this.id
    member this.CeLength with get () = (this.StageLength * %this.ChannelCount / 2) |> UMX.tag<ceLength>
    member this.ChannelCount with get () = this.channelCount
    member this.TwoOrbitUnfolder4s with get () = Array.copy this.twoOrbitUnfolder4s
    member this.StageLength with get () = (this.twoOrbitUnfolder4s.Length |> UMX.tag<stageLength>)
    member this.toString() =
        sprintf "msuf4(Id=%A, ChannelCount=%d, TwoOrbitUnfolder4Count=%d)"
                (%this.Id)
                (%this.ChannelCount)
                (this.TwoOrbitUnfolder4s.Length)

    override this.Equals(obj) =
        match obj with
        | :? msuf4 as other ->
            this.id = other.id
        | _ -> false

    override this.GetHashCode() =
        hash (this.GetType(), this.id)

    interface IEquatable<msuf4> with
        member this.Equals(other) =
            this.id = other.id

    member this.MakeSorter (maxCeLength: int<ceLength> option) =
        let allCes =
            this.TwoOrbitUnfolder4s
            |> Array.collect (fun tou ->
                tou.MakePerm_Si
                |> PermSi.getTwoOrbits
                |> Array.map Ce.fromTwoOrbit)
        let ces =
            match maxCeLength with
            | Some n -> allCes |> Array.truncate %n
            | None   -> allCes
        sorter.create (%this.Id |> UMX.tag<sorterId>) this.ChannelCount ces


module Msuf4 =

    /// Returns a string representation of the Msuf4 instance.
    let toString (msuf4: msuf4) : string =
        sprintf "Msuf4(Id=%A, ChannelCount=%d, TwoOrbitUnfolder4Count=%d)"
                (%msuf4.Id)
                (%msuf4.ChannelCount)
                msuf4.StageLength