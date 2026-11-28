namespace Sortopia.Models.Sorting.Simple.Uf4

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
type msuf4 =
    private
        { id: Guid<sorterModelId>
          channelCount: int<channelCount>
          prefix: ce array
          twoOrbitUnfolder4s: twoOrbitUf4 array }
    with
    /// Creates an Msuf4 instance with the specified ID, channel count, and array of TwoOrbitUnfolder4 instances.
    /// Throws an exception if the array is empty, width is less than 1, or any TwoOrbitUnfolder4 has a mismatched order.
    static member create
            (id: Guid<sorterModelId>)
            (channelCount: int<channelCount>)
            (prefix: ce array)
            (twoOrbitUf4s: twoOrbitUf4 array) : msuf4 =
        let prefix = ModelPrefix.snapshot channelCount prefix
        if isNull twoOrbitUf4s then nullArg "twoOrbitUf4s"
        if twoOrbitUf4s.Length < 1 then
            failwith $"Must have at least 1 TwoOrbitUnfolder4, got %d{twoOrbitUf4s.Length}"
        else if %channelCount < 1 then
            failwith $"ChannelCount must be at least 1, got {%channelCount}"
        else if twoOrbitUf4s |> Array.exists (fun tou -> tou.Order <> %channelCount) then
            failwith $"All TwoOrbitUnfolder4 must have order {%channelCount}"
        else
            { id = id; channelCount = channelCount; prefix = prefix; twoOrbitUnfolder4s = Array.copy twoOrbitUf4s }

    member this.Prefix = Array.copy this.prefix

    /// Internal transformations own the new stage array and share the immutable prefix.
    member internal this.MapStages(id, mapping: twoOrbitUf4 -> twoOrbitUf4) =
        let stages = Array.map mapping this.twoOrbitUnfolder4s
        let width = int this.channelCount
        if stages |> Array.exists (fun stage -> stage.Order <> width) then
            invalidArg "mapping" $"All TwoOrbitUnfolder4 must have order {width}"
        { id = id; channelCount = this.channelCount; prefix = this.prefix; twoOrbitUnfolder4s = stages }

    member this.Id with get () = this.id
    /// Suffix size only; full-sorter properties come from MakeSorter.
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
            this.prefix = other.prefix &&
            this.id = other.id
        | _ -> false

    override this.GetHashCode() =
        hash (this.GetType(), this.prefix, this.id)

    interface IEquatable<msuf4> with
        member this.Equals(other) =
            this.prefix = other.prefix &&
            this.id = other.id

    member private this.MakeSuffix (maxCeLength: int<ceLength> option) =
        let stages = this.twoOrbitUnfolder4s
        let expand (stage: twoOrbitUf4) = stage.MakePerm_Si |> PermSi.getTwoOrbits |> Array.map Ce.fromTwoOrbit
        match maxCeLength with
        | None -> stages |> Array.collect expand
        | Some length when int length < 0 -> invalidArg "maxCeLength" "Suffix CE limit must be nonnegative."
        | Some length ->
            // SGD cutoffs become much shorter than the stored model. Unfold only
            // the stages needed for this evaluation, retaining exact CE order.
            seq { for stage in stages do yield! expand stage }
            |> Seq.truncate (int length)
            |> Seq.toArray

    /// Prepend the fixed prefix after limiting the suffix. The prefix is never truncated.
    member this.MakeSorter (maxCeLength: int<ceLength> option) =
        let suffix = this.MakeSuffix maxCeLength
        ModelPrefix.materialize this.Id this.ChannelCount this.prefix suffix


module Msuf4 =

    /// Returns a string representation of the Msuf4 instance.
    let toString (msuf4: msuf4) : string =
        sprintf "Msuf4(Id=%A, ChannelCount=%d, TwoOrbitUnfolder4Count=%d)"
                (%msuf4.Id)
                (%msuf4.ChannelCount)
                msuf4.StageLength
