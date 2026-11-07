namespace Sortopia.Models.Sorting.Simple.Uf6

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

/// Represents a collection of TwoOrbitUnfolder4 instances with a consistent channel count.
[<Struct; CustomEquality; NoComparison>]
type msuf6 =
    private
        { id: Guid<sorterModelId>
          channelCount: int<channelCount>
          prefix: ce array
          twoOrbitUnfolder6s: twoOrbitUf6 array }
    with
    /// Creates an Msuf6 instance with the specified ID, channel count, and array of TwoOrbitUnfolder4 instances.
    /// Throws an exception if the array is empty, width is less than 1, or any TwoOrbitUnfolder4 has a mismatched order.
    static member create
            (id: Guid<sorterModelId>)
            (channelCount: int<channelCount>)
            (prefix: ce array)
            (twoOrbitUnfolder6s: twoOrbitUf6 array) : msuf6 =
        let prefix = ModelPrefix.snapshot channelCount prefix
        if isNull twoOrbitUnfolder6s then nullArg "twoOrbitUnfolder6s"
        if twoOrbitUnfolder6s.Length < 1 then
            failwith $"Must have at least 1 TwoOrbitUnfolder6, got %d{twoOrbitUnfolder6s.Length}"
        else if %channelCount < 1 then
            failwith $"ChannelCount must be at least 1, got {%channelCount}"
        else if twoOrbitUnfolder6s |> Array.exists (fun tou -> tou.Order <> %channelCount) then
            failwith $"All TwoOrbitUnfolder6 must have order {%channelCount}"
        else
            { id = id; channelCount = channelCount; prefix = prefix; twoOrbitUnfolder6s = Array.copy twoOrbitUnfolder6s }

    member this.Prefix = Array.copy this.prefix
    member this.Id with get () = this.id
    /// Suffix size only; full-sorter properties come from MakeSorterWithPrefix.
    member this.CeLength with get () = (this.StageLength * %this.ChannelCount / 2) |> UMX.tag<ceLength>
    member this.ChannelCount with get () = this.channelCount
    member this.TwoOrbitUnfolder6s with get () = Array.copy this.twoOrbitUnfolder6s
    member this.StageLength with get () = (this.twoOrbitUnfolder6s.Length |> UMX.tag<stageLength>)
    member this.toString() =
        sprintf "msuf6(Id=%A, ChannelCount=%d, TwoOrbitUnfolder6Count=%d)"
                (%this.Id)
                (%this.ChannelCount)
                (this.TwoOrbitUnfolder6s.Length)

    override this.Equals(obj) =
        match obj with
        | :? msuf6 as other ->
            this.prefix = other.prefix &&
            this.id = other.id
        | _ -> false

    override this.GetHashCode() =
        hash (this.GetType(), this.prefix, this.id)

    interface IEquatable<msuf6> with
        member this.Equals(other) =
            this.prefix = other.prefix &&
            this.id = other.id

    member private this.MakeSuffix (maxCeLength: int<ceLength> option) =
        let allCes =
            this.TwoOrbitUnfolder6s
            |> Array.collect (fun tou ->
                tou.MakePerm_Si
                |> PermSi.getTwoOrbits
                |> Array.map Ce.fromTwoOrbit)
        ModelPrefix.truncate maxCeLength allCes

    /// Limit the suffix CEs; use with inputs already presorted by Prefix.
    member this.MakeSorterWithoutPrefix (maxCeLength: int<ceLength> option) =
        let ces = this.MakeSuffix maxCeLength
        ModelPrefix.materialize this.Id this.ChannelCount this.prefix false ces

    /// Prepend the fixed prefix after limiting the suffix. The prefix is never truncated.
    member this.MakeSorterWithPrefix (maxCeLength: int<ceLength> option) =
        let suffix = this.MakeSuffix maxCeLength
        ModelPrefix.materialize this.Id this.ChannelCount this.prefix true suffix


module Msuf6 =

    /// Returns a string representation of the Msuf6 instance.
    let toString (msuf6: msuf6) : string =
        sprintf "Msuf6(Id=%A, ChannelCount=%d, TwoOrbitUnfolder6Count=%d)"
                (%msuf6.Id)
                (%msuf6.ChannelCount)
                msuf6.StageLength