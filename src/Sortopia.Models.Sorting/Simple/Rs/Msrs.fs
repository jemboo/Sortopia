namespace Sortopia.Models.Sorting.Simple.Rs

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

/// Represents a rank-swap-based sorting model composed of an array of Perm_Rs instances.
[<Struct; CustomEquality; NoComparison>]
type msrs =
    private
        { id: Guid<sorterModelId>
          channelCount: int<channelCount>
          prefix: ce array
          perm_Rss: permRs array }
    with
    /// Creates an Msrs instance with the specified ID, width, and Perm_Rs array.
    /// Throws an exception if the Perm_Rs array is empty, width is less than 1, or any Perm_Rs has a mismatched width.
    static member create
            (id: Guid<sorterModelId>)
            (width: int<channelCount>)
            (prefix: ce array)
            (perm_Rss: permRs array) : msrs =

        let prefix = ModelPrefix.snapshot width prefix
        if isNull perm_Rss then nullArg "perm_Rss"
        if perm_Rss.Length < 1 then
            failwith $"Must have at least 1 Perm_Rs, got %d{perm_Rss.Length}"
        else if %width < 1 then
            failwith $"Width must be at least 1, got {%width}"
        else if perm_Rss |> Array.exists (fun prs -> %prs.Order <> %width) then
            failwith $"All Perm_Rs must have width {%width}"
        else
            { id = id; channelCount = width; prefix = prefix; perm_Rss = Array.copy perm_Rss }

    member this.Prefix = Array.copy this.prefix
    member this.Id with get () = this.id
    /// Suffix size only; full-sorter properties come from MakeSorterWithPrefix.
    member this.CeLength with get () = (this.StageLength * %this.ChannelCount / 2) |> UMX.tag<ceLength>
    member this.StageLength with get () = this.perm_Rss.Length |> UMX.tag<stageLength>
    member this.ChannelCount with get () = this.channelCount
    member this.Perm_Rss with get () = Array.copy this.perm_Rss
    member this.toString() =
        sprintf "msrs(Id=%A, Width=%d, Perm_Rs_count=%d)"
                (%this.Id)
                (%this.ChannelCount)
                (this.Perm_Rss.Length)

    override this.Equals(obj) =
        match obj with
        | :? msrs as other ->
            this.prefix = other.prefix &&
            this.id = other.id
        | _ -> false

    override this.GetHashCode() =
        hash (this.GetType(), this.prefix, this.id)

    interface IEquatable<msrs> with
        member this.Equals(other) =
            this.prefix = other.prefix &&
            this.id = other.id

    member private this.MakeSuffix (maxCeLength: int<ceLength> option) =
        let allCes =
            this.Perm_Rss
            |> Array.collect (fun prs ->
                prs.PermSi
                |> PermSi.getTwoOrbits
                |> Array.map (fun tbit -> ce.create tbit.First tbit.Second))
        ModelPrefix.truncate maxCeLength allCes

    /// Limit the suffix CEs; use with inputs already presorted by Prefix.
    member this.MakeSorterWithoutPrefix (maxCeLength: int<ceLength> option) =
        let ces = this.MakeSuffix maxCeLength
        ModelPrefix.materialize this.Id this.ChannelCount this.prefix false ces

    /// Prepend the fixed prefix after limiting the suffix. The prefix is never truncated.
    member this.MakeSorterWithPrefix (maxCeLength: int<ceLength> option) =
        let suffix = this.MakeSuffix maxCeLength
        ModelPrefix.materialize this.Id this.ChannelCount this.prefix true suffix


module Msrs =

    /// Returns a string representation of the Msrs instance.
    let toString (msrs: msrs) : string =
        sprintf "Msrs(Id=%A, Width=%d, Perm_Rs_count=%d)"
                (%msrs.Id)
                (%msrs.ChannelCount)
                msrs.Perm_Rss.Length

    /// Returns the number of Perm_Rs instances in the Msrs.
    let getLength (msrs: msrs) : int =
        msrs.Perm_Rss.Length

    /// Converts an Msrs instance to a Sorter by mapping each Perm_Rs to comparison elements (Ce) via TwoOrbits.
    /// <param name="msrs">The Msrs instance to convert.</param>
    /// <returns>A Sorter instance with the same width and derived comparison elements.</returns>
    let makeSorterWithoutPrefix (model: msrs) : sorter =
        model.MakeSorterWithoutPrefix None

    let makeSorterWithPrefix (model: msrs) : sorter =
        model.MakeSorterWithPrefix None
