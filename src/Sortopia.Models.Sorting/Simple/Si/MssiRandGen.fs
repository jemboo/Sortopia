namespace Sortopia.Models.Sorting.Simple.Si

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

/// Represents a configuration for generating random Mssi instances with specified width and stage count.
[<Struct; CustomEquality; NoComparison>]
type mssiRandGen =
    private
        {
          id : Guid<sorterModelGenId>
          rngFactory: rngFactory
          channelCount: int<channelCount>
          prefix: ce array
          stageLength: int<stageLength>
        }
    with

    static member create
            (rngFactory: rngFactory)
            (channelCount: int<channelCount>)
            (prefix: ce array)
            (stageLength: int<stageLength>) : mssiRandGen =
        let prefix = ModelPrefix.snapshot channelCount prefix
        if %channelCount < 2 then
            failwith $"ChannelCount must be at least 2, got {%channelCount}"
        else if %stageLength < 1 then
            failwith $"StageLength must be at least 1, got {%stageLength}"
        else
            let id =
                [
                    box "Sortopia.Models.Sorting/mssiRandGen/v2"
                    box rngFactory
                    box (channelCount |> UMX.untag)
                    box (stageLength |> UMX.untag)
                ] @ (ModelPrefix.hashValues prefix |> Seq.toList) |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelGenId>

            {
                id = id;
                rngFactory = rngFactory;
                channelCount = channelCount
                prefix = prefix
                stageLength = stageLength
            }

    member this.Prefix = Array.copy this.prefix
    member this.Id with get () = this.id
    member this.CeLength with get () = (this.ChannelCount * %this.StageLength / 2) |> UMX.tag<ceLength>
    member this.RngFactory with get () = this.rngFactory
    member this.ChannelCount with get () = this.channelCount
    member this.StageLength with get () = this.stageLength

    override this.Equals(obj) =
        match obj with
        | :? mssiRandGen as other ->
            this.RngFactory = other.RngFactory &&
            this.prefix = other.prefix &&
            this.channelCount = other.channelCount &&
            this.stageLength = other.stageLength
        | _ -> false

    override this.GetHashCode() =
        hash (this.GetType(), this.RngFactory, this.channelCount, this.stageLength, this.prefix)

    interface IEquatable<mssiRandGen> with
        member this.Equals(other) =
            this.RngFactory = other.RngFactory &&
            this.prefix = other.prefix &&
            this.channelCount = other.channelCount &&
            this.stageLength = other.stageLength


    member this.MakeSorterModelId (index: int<sortingGenIndex>) : Guid<sorterModelId> =
        CommonGen.makeSorterModelId this.Id index


    member this.MakeSorterModelFromId (id: Guid<sorterModelId>) : mssi =
        let rando = this.RngFactory.Create %id
        let perm_Sis = PermSi.makeRandoms (rando.NextIndex) (%this.ChannelCount)
                        |> Seq.take (%this.StageLength)
                        |> Seq.toArray
        mssi.create id this.ChannelCount this.Prefix perm_Sis


    member this.MakeSorterModelFromIndex (index: int<sortingGenIndex>) : mssi =
        let id = this.MakeSorterModelId index
        this.MakeSorterModelFromId id


module MssiRandGen =

    /// Every suffix stage uses the same random-involution generator.
    let create rngFactory channelCount prefix stageLength =
        GenerationValidation.dimensions simpleSorterModelType.Mssi channelCount stageLength
        mssiRandGen.create rngFactory channelCount prefix stageLength

    /// Returns a string representation of the MssiRandGen.
    let toString (mssiRandGen: mssiRandGen) : string =
        sprintf "MssiRandGen(RngType=%A, Width=%d, StageLength=%d)"
                mssiRandGen.RngFactory (%mssiRandGen.ChannelCount) (%mssiRandGen.StageLength)
