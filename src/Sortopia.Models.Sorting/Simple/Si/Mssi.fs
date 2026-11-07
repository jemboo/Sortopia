namespace Sortopia.Models.Sorting.Simple.Si

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

[<Struct; CustomEquality; NoComparison>]
type mssi =
    private
        { id: Guid<sorterModelId>
          channelCount: int<channelCount>
          prefix: ce array
          perm_Sis: permSi array }
    with
    static member create
            (id: Guid<sorterModelId>)
            (channelCount: int<channelCount>)
            (prefix: ce array)
            (perm_Sis: permSi array) : mssi =
        let prefix = ModelPrefix.snapshot channelCount prefix
        if isNull perm_Sis then nullArg "perm_Sis"
        if perm_Sis.Length < 1 then
            failwith "Must have at least 1 PermSi"
        else if %channelCount < 1 then
            failwith "Width must be at least 1"
        else if perm_Sis |> Array.exists (fun entry -> int entry.Order <> int channelCount) then
            invalidArg "perm_Sis" "Every permutation must match the model's channel count."
        else
            { id = id; channelCount = channelCount; prefix = prefix; perm_Sis = Array.copy perm_Sis }

    member this.Prefix = Array.copy this.prefix
    member this.Id with get () = this.id
    member this.ChannelCount with get () = this.channelCount
    /// Suffix size only; full-sorter properties come from MakeSorterWithPrefix.
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
            this.prefix = other.prefix &&
            this.id = other.id &&
            this.channelCount = other.channelCount &&
            this.perm_Sis = other.perm_Sis
        | _ -> false

    override this.GetHashCode() =
        hash (this.GetType(), this.prefix, this.id, this.channelCount, this.perm_Sis)

    interface IEquatable<mssi> with
        member this.Equals(other) =
            this.prefix = other.prefix &&
            this.id = other.id &&
            this.channelCount = other.channelCount &&
            this.perm_Sis = other.perm_Sis

    member private this.MakeSuffix (maxCeLength: int<ceLength> option) =
        let allCes = this.perm_Sis
                        |> Array.collect ModelPrefix.expandPermutation
        ModelPrefix.truncate maxCeLength allCes

    /// Limit the suffix CEs; use with inputs already presorted by Prefix.
    member this.MakeSorterWithoutPrefix (maxCeLength: int<ceLength> option) =
        let ces = this.MakeSuffix maxCeLength
        ModelPrefix.materialize this.Id this.ChannelCount this.prefix false ces

    /// Prepend the fixed prefix after limiting the suffix. The prefix is never truncated.
    member this.MakeSorterWithPrefix (maxCeLength: int<ceLength> option) =
        let suffix = this.MakeSuffix maxCeLength
        ModelPrefix.materialize this.Id this.ChannelCount this.prefix true suffix


module Mssi =

    let toString (mssi: mssi) : string =
        sprintf "Mssi(Id=%A, Width=%d, StageLength=%d)"
                (%mssi.Id)
                (%mssi.ChannelCount)
                mssi.StageLength

    let makeSorterWithoutPrefix (model: mssi) : sorter =
        model.MakeSorterWithoutPrefix None

    let makeSorterWithPrefix (model: mssi) : sorter =
        model.MakeSorterWithPrefix None
