namespace Sortopia.Models.Sorting.Simple.Uf6

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Models.Sorting

[<Struct; CustomEquality; NoComparison>]
type msuf6RandGen =
    private
        { id: Guid<sorterModelGenId>
          rngFactory: rngFactory
          channelCount: int<channelCount>
          stageLength: int<stageLength>
          genRates: uf6GenRates } // Changed from uf6GenRatesArray

    static member create
            (rngFactory: rngFactory)
            (channelCount: int<channelCount>)
            (stageLength: int<stageLength>)
            (genRates: uf6GenRates) : msuf6RandGen =

        if %channelCount < 6 || %channelCount % 2 <> 0 then
            failwith $"ChannelCount must be at least 6 and even, got {%channelCount}"
        if %stageLength < 1 then
            failwith $"StageLength must be at least 1, got {%stageLength}"
        if genRates.Order <> %channelCount then
            failwith $"Uf6GenRates order (%d{genRates.Order}) must match channelCount ({%channelCount})"

        let id =
            [
                box "Sortopia.Models.Sorting/msuf6RandGen/v1"
                box rngFactory
                box (channelCount |> UMX.untag)
                box (stageLength |> UMX.untag)
                box genRates
            ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelGenId>

        {
          id = id
          rngFactory = rngFactory
          channelCount = channelCount
          stageLength = stageLength
          genRates = genRates
        }

    member this.Id with get() = this.id
    member this.CeLength with get () = (this.ChannelCount * %this.StageLength / 2) |> UMX.tag<ceLength>
    member this.RngFactory with get() = this.rngFactory
    member this.ChannelCount with get() = this.channelCount
    member this.StageLength with get() = this.stageLength
    member this.GenRates with get() = this.genRates

    override this.Equals(obj) =
        match obj with
        | :? msuf6RandGen as other -> this.id = other.id
        | _ -> false

    override this.GetHashCode() = hash this.id

    interface IEquatable<msuf6RandGen> with
        member this.Equals(other) = this.id = other.id

    member this.MakeSorterModelId (index: int<sortingGenIndex>) : Guid<sorterModelId> =
        CommonGen.makeSorterModelId this.Id index

    member this.MakeSorterModelFromId (id: Guid<sorterModelId>) : msuf6 =
        let rng = this.RngFactory.Create %id

        // HOIST: Local bindings for closure safety and performance
        let rates = this.genRates
        let sc = %this.stageLength
        let width = this.channelCount

        let twoOrbitUnfolder6s =
            Array.init sc (fun _ ->
                RandomUnfolderOps6.makeRandomTwoOrbitUf6 rng.NextFloat rates)

        msuf6.create id width twoOrbitUnfolder6s

    member this.MakeSorterModelFromIndex (index: int<sortingGenIndex>) : msuf6 =
        let id = this.MakeSorterModelId index
        this.MakeSorterModelFromId id


module Msuf6RandGen =

    let toString (msuf6RandGen: msuf6RandGen) : string =
        let gr = msuf6RandGen.GenRates
        // Simplified reporting for uniform rates
        let ratesStr = sprintf "[Uniform: Order=%d]" gr.Order

        sprintf "Msuf6RandGen(Id=%A, RngType=%A, ChannelCount=%d, StageLength=%d, GenRates=%s)"
                (%msuf6RandGen.Id)
                msuf6RandGen.RngFactory
                (%msuf6RandGen.ChannelCount)
                (%msuf6RandGen.StageLength)
                ratesStr