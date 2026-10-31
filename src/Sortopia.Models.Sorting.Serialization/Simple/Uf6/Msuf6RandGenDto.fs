
namespace Sortopia.Models.Sorting.Serialization.Simple.Uf6

open System
open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization
open Sortopia.Permutations.Serialization.RatesAndOps
open Sortopia.Sorting
open Sortopia.Models.Sorting.Simple.Uf6

type msuf6RandGenDto =
    { id: Guid
      rngFactoryDto: rngFactoryDto
      channelCount: int
      stageLength: int
      uf6GenRatesDto: uf6GenRatesDto }

module Msuf6RandGenDto =

    let fromDomain (msuf6RandGen: msuf6RandGen) : msuf6RandGenDto =
        { id = %msuf6RandGen.Id
          rngFactoryDto = msuf6RandGen.RngFactory |> RngFactoryDto.fromDomain
          channelCount = %msuf6RandGen.ChannelCount
          stageLength = %msuf6RandGen.StageLength
          uf6GenRatesDto = Uf6GenRatesDto.fromDomain msuf6RandGen.GenRates }

    let toDomain (dto: msuf6RandGenDto) : msuf6RandGen =
        try
            if dto.channelCount < 6 || dto.channelCount % 6 <> 0 then
                failwith $"ChannelCount must be at least 6 and divisible by 6, got {dto.channelCount}"
            if dto.stageLength < 1 then
                failwith $"StageLength must be at least 1, got {dto.stageLength}"
            let genRates = Uf6GenRatesDto.toDomain dto.uf6GenRatesDto

            msuf6RandGen.create
                    (dto.rngFactoryDto |> RngFactoryDto.toDomain)
                    (UMX.tag<channelCount> dto.channelCount)
                    (UMX.tag<stageLength> dto.stageLength)
                    genRates
        with
        | ex -> failwith $"Failed to convert Msuf6RandGenDto: {ex.Message}"

