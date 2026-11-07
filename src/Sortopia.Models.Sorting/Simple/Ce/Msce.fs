namespace Sortopia.Models.Sorting.Simple.Ce

open System
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

[<Struct; CustomEquality; NoComparison>]
type msce =
    private
        { id: Guid<sorterModelId>
          channelCount: int<channelCount>
          prefix: ce array
          ceCodes: int array }
    with
    static member create
            (id: Guid<sorterModelId>)
            (channelCount: int<channelCount>)
            (prefix: ce array)
            (ceCodes: int array) : msce =
        let prefix = ModelPrefix.snapshot channelCount prefix
        if isNull ceCodes then nullArg "ceCodes"
        if ceCodes.Length < 1 then
            failwith "Must be at least 1 Ce"
        else if %channelCount < 1 then
            failwith "ChannelCount must be at least 1"
        else
            { id = id; channelCount = channelCount; prefix = prefix; ceCodes = Array.copy ceCodes }

    member this.Prefix = Array.copy this.prefix
    member this.Id with get () = this.id
    member this.ChannelCount with get () = this.channelCount
    member this.CeCodes with get () = Array.copy this.ceCodes
    /// Suffix size only; full-sorter properties come from MakeSorterWithPrefix.
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
            this.prefix = other.prefix &&
            this.id = other.id &&
            this.channelCount = other.channelCount &&
            this.ceCodes = other.ceCodes
        | _ -> false

    override this.GetHashCode() =
        hash (this.GetType(), this.prefix, this.id, this.channelCount, this.ceCodes)

    interface IEquatable<msce> with
        member this.Equals(other) =
            this.prefix = other.prefix &&
            this.id = other.id &&
            this.channelCount = other.channelCount &&
            this.ceCodes = other.ceCodes

    // take up to n ces if n is Some, otherwise take all ces
    member private this.MakeSuffix (maxCeLength: int<ceLength> option) =
        let allCes = this.ceCodes |> Array.map (Ce.fromIndex)
        ModelPrefix.truncate maxCeLength allCes

    /// Limit the suffix CEs; use with inputs already presorted by Prefix.
    member this.MakeSorterWithoutPrefix (maxCeLength: int<ceLength> option) =
        let ces = this.MakeSuffix maxCeLength
        ModelPrefix.materialize this.Id this.ChannelCount this.prefix false ces

    /// Prepend the fixed prefix after limiting the suffix. The prefix is never truncated.
    member this.MakeSorterWithPrefix (maxCeLength: int<ceLength> option) =
        let suffix = this.MakeSuffix maxCeLength
        ModelPrefix.materialize this.Id this.ChannelCount this.prefix true suffix


module Msce =

    let toString (msce: msce) : string =
        sprintf "msce(Id=%A, ChannelCount=%d, SorterLength=%d)"
                (%msce.Id)
                (%msce.ChannelCount)
                (msce.CeCodes.Length)
