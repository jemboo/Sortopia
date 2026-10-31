namespace Sortopia.Models.Sorting.Simple.Uf4

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Models.Sorting
open MathUtils

[<Struct; CustomEquality; NoComparison>]
type msuf4RandGen =
    private
        {
          id: Guid<sorterModelGenId>
          rngFactory: rngFactory
          channelCount: int<channelCount>
          stageLength: int<stageLength>
          genRates: uf4GenRates
        }
    with
    static member create
            (rngFactory: rngFactory)
            (channelCount: int<channelCount>)
            (stageLength: int<stageLength>)
            (genRates: uf4GenRates)
            : msuf4RandGen =

        // Validation logic
        if %channelCount < 1 then
            failwith $"ChannelCount must be at least 1, got {%channelCount}"
        else if (%channelCount - 1) &&& %channelCount <> 0 then
            failwith $"ChannelCount must be a power of 2, got {%channelCount}"
        else if %stageLength < 1 then
            failwith $"StageLength must be at least 1, got {%stageLength}"
        else if genRates.Order <> %channelCount then
            failwith $"genRates order (%d{genRates.Order}) must match channelCount ({%channelCount})"
        else
            let id =
                [
                    box "Sortopia.Models.Sorting/msuf4RandGen/v1"
                    box rngFactory
                    box (channelCount |> UMX.untag)
                    box (stageLength |> UMX.untag)
                    box genRates
                ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelGenId>

            { id = id
              rngFactory = rngFactory
              channelCount = channelCount
              stageLength = stageLength
              genRates = genRates }

    member this.Id with get () = this.id
    member this.CeLength with get () = (this.ChannelCount * %this.StageLength / 2) |> UMX.tag<ceLength>
    member this.RngFactory with get () = this.rngFactory
    member this.ChannelCount with get () = this.channelCount
    member this.StageLength with get () = this.stageLength
    member this.GenRates with get () = this.genRates

    override this.Equals(obj) =
        match obj with
        | :? msuf4RandGen as other ->
            this.id = other.id
        | _ -> false

    override this.GetHashCode() =
        hash (this.rngFactory, this.channelCount, this.stageLength, this.genRates)

    interface IEquatable<msuf4RandGen> with
        member this.Equals(other) = this.id = other.id

    member this.MakeSorterModelId (index: int<sortingGenIndex>) : Guid<sorterModelId> =
        CommonGen.makeSorterModelId this.Id index

    member this.MakeSorterModelFromId (id: Guid<sorterModelId>) : msuf4 =
        let rando = this.RngFactory.Create %id
        let sc = %this.stageLength
        let rates = this.genRates
        let width = this.channelCount

        let twoOrbitUnfolder4s =
            Array.init sc (fun _ ->
                RandomUnfolderOps4.makeRandomTwoOrbitUf4 rando.NextFloat rates)

        msuf4.create id width twoOrbitUnfolder4s


    member this.MakeSorterModelFromIndex (index: int<sortingGenIndex>) : msuf4 =
        let id = this.MakeSorterModelId index
        this.MakeSorterModelFromId id


module Msuf4RandGen =

    let toString (msuf4Gen: msuf4RandGen) : string =
        let gr = msuf4Gen.GenRates
        let ratesStr =
            sprintf "[Uniform: Ortho=%.2f, Para=%.2f, SelfRefl=%.2f]"
                gr.SeedOpsGenRates.OrthoRate
                gr.SeedOpsGenRates.ParaRate
                gr.SeedOpsGenRates.SelfReflRate

        sprintf "Msuf4RandGen(RngType=%A, Width=%d, StageLength=%d, GenRates=%s)"
                msuf4Gen.RngFactory
                (%msuf4Gen.ChannelCount)
                (%msuf4Gen.StageLength)
                ratesStr
