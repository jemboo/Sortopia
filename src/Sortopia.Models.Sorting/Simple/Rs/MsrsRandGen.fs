namespace Sortopia.Models.Sorting.Simple.Rs

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Models.Sorting

/// Represents a configuration for generating random Msrs instances with specified mutation rates.
/// Combines random generation and mutation probabilities for Perm_Rs instances.
[<Struct; CustomEquality; NoComparison>]
type msrsRandGen =
    private
        {
              id : Guid<sorterModelGenId>
              rngFactory: rngFactory
              opsGenRates: opsGenRates
              channelCount: int<channelCount>
              stageLength: int<stageLength>
        }
    static member create
            (rngFactory: rngFactory)
            (channelCount: int<channelCount>)
            (opsGenRates: opsGenRates)
            (stageLength: int<stageLength>)
            : msrsRandGen =

        if %channelCount < 2 then
            failwith $"ChannelCount must be at least 2, got {%channelCount}"
        if int stageLength < 1 then invalidArg "stageLength" "Stage length must be positive."
        let id =
            [
                box "Sortopia.Models.Sorting/msrsRandGen/v1"
                box rngFactory
                box (channelCount |> UMX.untag)
                box (stageLength |> UMX.untag)
                box opsGenRates
            ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelGenId>

        {
            id = id
            channelCount = channelCount
            opsGenRates = opsGenRates
            rngFactory = rngFactory
            stageLength = stageLength
        }

    member this.Id with get () = this.id
    member this.CeLength with get () = (this.ChannelCount * %this.StageLength / 2) |> UMX.tag<ceLength>
    member this.RngFactory with get () = this.rngFactory
    member this.OpsGenRates with get () = this.opsGenRates
    member this.ChannelCount with get () = this.channelCount
    member this.StageLength with get () = this.stageLength

    override this.Equals(obj) =
        match obj with
        | :? msrsRandGen as other ->
            this.id = other.id
        | _ -> false

    override this.GetHashCode() =
        hash (this.rngFactory, this.opsGenRates, this.channelCount, this.stageLength)

    interface IEquatable<msrsRandGen> with
        member this.Equals(other) =
            this.Id = other.Id


    member this.MakeSorterModelId (index: int<sortingGenIndex>) : Guid<sorterModelId> =
        CommonGen.makeSorterModelId this.Id index


    member this.MakeSorterModelFromId (id: Guid<sorterModelId>) : msrs =
        let rng = this.RngFactory.Create %id
        let stageLength = %this.StageLength
        let channelCount = %this.ChannelCount
        let opsGenRates = this.OpsGenRates
        let perm_Rss =
            [| for dex in 0 .. (stageLength - 1) ->
                PermRsOps.makeRandomPerm_Rs
                    (rng.NextIndex)
                    (rng.NextFloat)
                    (opsGenRates)
                    (channelCount) |]

        msrs.create id this.ChannelCount perm_Rss


    member this.MakeSorterModelFromIndex (index: int<sortingGenIndex>) : msrs =
        let id = this.MakeSorterModelId index
        this.MakeSorterModelFromId id


module MsrsRandGen =

    /// Returns a string representation of the MsrsRandGen configuration.
    let toString (msrsGen: msrsRandGen) : string =
        sprintf "MsrsRandGen(RngType=%A, Width=%d, StageLength=%d, OpActionRatesArray=%s)"
                msrsGen.RngFactory
                (%msrsGen.ChannelCount)
                (%msrsGen.StageLength)
                (msrsGen.OpsGenRates.toString())

