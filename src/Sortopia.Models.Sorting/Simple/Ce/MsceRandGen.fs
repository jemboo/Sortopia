namespace Sortopia.Models.Sorting.Simple.Ce

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting

[<Struct; CustomEquality; NoComparison>]
type msceRandGen =
    private
        {
          id : Guid<sorterModelGenId>
          rngFactory: rngFactory
          channelCount: int<channelCount>
          excludeSelfCe: bool<excludeSelfCe>
          ceLength: int<ceLength> }
    with
    static member create
            (rngFactory: rngFactory)
            (channelCount: int<channelCount>)
            (excludeSelfCe: bool<excludeSelfCe>)
            (ceLength: int<ceLength>) : msceRandGen =
        if %ceLength < 1 then
            failwith "ceLength length must be at least 1"
        else if %channelCount < 1 then
            failwith "Width must be at least 1"
        else if int channelCount = 1 && UMX.untag excludeSelfCe then
            invalidArg "excludeSelfCe" "A non-self comparator requires at least two channels."
        else
            let id =
                [
                    box "msceRandGen"
                    box rngFactory
                    box (channelCount |> UMX.untag)
                    box (excludeSelfCe |> UMX.untag)
                    box (ceLength |> UMX.untag)
                ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelGenId>

            { id = id;
              rngFactory = rngFactory;
              channelCount = channelCount;
              excludeSelfCe = excludeSelfCe;
              ceLength = ceLength }


    member this.Id with get () = this.id
    member this.RngFactory with get () = this.rngFactory
    member this.ChannelCount with get () = this.channelCount
    member this.ExcludeSelfCe with get () = this.excludeSelfCe
    member this.CeLength with get () = this.ceLength

    override this.Equals(obj) =
        match obj with
        | :? msceRandGen as other ->
            this.rngFactory = other.rngFactory &&
            this.channelCount = other.channelCount &&
            this.excludeSelfCe = other.excludeSelfCe &&
            this.ceLength = other.ceLength
        | _ -> false

    override this.GetHashCode() =
        hash (this.rngFactory, this.channelCount, this.excludeSelfCe, this.ceLength)

    interface IEquatable<msceRandGen> with
        member this.Equals(other) =
            this.rngFactory = other.rngFactory &&
            this.channelCount = other.channelCount &&
            this.excludeSelfCe = other.excludeSelfCe &&
            this.ceLength = other.ceLength


    member this.MakeSorterModelId (index: int<sortingGenIndex>) : Guid<sorterModelId> =
        CommonGen.makeSorterModelId this.Id index

    member this.MakeSorterModelFromId (id: Guid<sorterModelId>) : msce =
        let rando = this.RngFactory.Create %id
        let ceCodes =
            if %this.ExcludeSelfCe then
                Ce.generateCeCodesExcludeSelf (rando.NextIndex) %this.ChannelCount
                |> Seq.take %this.ceLength
                |> Seq.toArray
            else
                Ce.generateCeCodes (rando.NextIndex) %this.ChannelCount
                |> Seq.take %this.ceLength
                |> Seq.toArray

        msce.create
            id
            this.ChannelCount
            ceCodes


    member this.MakeSorterModelFromIndex (index: int<sortingGenIndex>) : msce =
        let id = this.MakeSorterModelId index
        this.MakeSorterModelFromId id


module MsceRandGen =

    let toString (msceRandGen: msceRandGen) : string =
        sprintf "Model_CeGen(rngType=%A, Width=%d, Length=[%d])"
                    (msceRandGen.RngFactory) (%msceRandGen.ChannelCount) (%msceRandGen.ceLength)

