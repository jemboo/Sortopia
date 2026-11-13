namespace Sortopia.Models.Sorting.Simple.Uf4

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open MathUtils

[<Struct; CustomEquality; NoComparison>]
type msuf4RandGen =
    private
        {
          id: Guid<sorterModelGenId>
          rngFactory: rngFactory
          channelCount: int<channelCount>
          prefix: ce array
          stageLength: int<stageLength>
          genRates: uf4GenRates
        }
    with
    static member create
            (rngFactory: rngFactory)
            (channelCount: int<channelCount>)
            (prefix: ce array)
            (stageLength: int<stageLength>)
            (genRates: uf4GenRates)
            : msuf4RandGen =

        // Validation logic
        let prefix = ModelPrefix.snapshot channelCount prefix
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
                    box "Sortopia.Models.Sorting/msuf4RandGen/v2"
                    box rngFactory
                    box (channelCount |> UMX.untag)
                    box (stageLength |> UMX.untag)
                    box genRates
                ] @ (ModelPrefix.hashValues prefix |> Seq.toList) |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelGenId>

            { id = id
              rngFactory = rngFactory
              channelCount = channelCount
              prefix = prefix
              stageLength = stageLength
              genRates = genRates }

    member this.Prefix = Array.copy this.prefix
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
        hash (this.rngFactory, this.channelCount, this.stageLength, this.genRates, this.prefix)

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

        msuf4.create id width this.Prefix twoOrbitUnfolder4s


    member this.MakeSorterModelFromIndex (index: int<sortingGenIndex>) : msuf4 =
        let id = this.MakeSorterModelId index
        this.MakeSorterModelFromId id


module Msuf4RandGen =

    /// Source widths of unfolding levels within one stage, in generation order.
    let unfoldingWidths channelCount = GenerationValidation.unfoldingWidths simpleSorterModelType.Msuf4 4 channelCount

    /// Reuse the seed and per-level probabilities unchanged for every suffix stage.
    let create rngFactory channelCount prefix stageLength (seedProbabilities: opsGenRates) (unfoldingProbabilities: opsGenRates list) =
        GenerationValidation.dimensions simpleSorterModelType.Msuf4 channelCount stageLength
        GenerationValidation.ops "seedProbabilities" seedProbabilities
        let unfolding = GenerationValidation.unfolding (unfoldingWidths (int channelCount)).Length unfoldingProbabilities
        let rates = uf4GenRates.create (int channelCount) seedProbabilities unfolding
        msuf4RandGen.create rngFactory channelCount prefix stageLength rates

    let createUniform rngFactory channelCount prefix stageLength =
        create rngFactory channelCount prefix stageLength (opsGenRates.createUniform())
            (unfoldingWidths (int channelCount) |> List.map (fun _ -> opsGenRates.createUniform()))

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
