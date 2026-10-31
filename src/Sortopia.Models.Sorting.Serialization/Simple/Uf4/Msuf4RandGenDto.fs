
namespace Sortopia.Models.Sorting.Serialization.Simple.Uf4

open System
open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization
open Sortopia.Permutations.Serialization.RatesAndOps
open Sortopia.Sorting
open Sortopia.Models.Sorting.Simple.Uf4

type msuf4RandGenDto =
    { id: Guid
      rngFactoryDto: rngFactoryDto
      channelCount: int
      stageLength: int
      uf4GenRatesDto: uf4GenRatesDto }

module Msuf4RandGenDto =

    let fromDomain (msuf4RandGen: msuf4RandGen) : msuf4RandGenDto =
        { id = %msuf4RandGen.Id
          rngFactoryDto = msuf4RandGen.RngFactory |> RngFactoryDto.fromDomain
          channelCount = %msuf4RandGen.ChannelCount
          stageLength = %msuf4RandGen.StageLength
          uf4GenRatesDto = Uf4GenRatesDto.fromDomain msuf4RandGen.GenRates }

    let toDomain (dto: msuf4RandGenDto) : msuf4RandGen =
        try
            if dto.channelCount < 1 then
                failwith $"ChannelCount must be at least 1, got {dto.channelCount}"
            if (dto.channelCount - 1) &&& dto.channelCount <> 0 then
                failwith $"ChannelCount must be a power of 2, got {dto.channelCount}"
            if dto.stageLength < 1 then
                failwith $"StageLength must be at least 1, got {dto.stageLength}"
            let genRates = Uf4GenRatesDto.toDomain dto.uf4GenRatesDto

            msuf4RandGen.create
                    (dto.rngFactoryDto |> RngFactoryDto.toDomain)
                    (UMX.tag<channelCount> dto.channelCount)
                    (UMX.tag<stageLength> dto.stageLength)
                    genRates
        with
        | ex -> failwith $"Failed to convert Msuf4RandGenDto: {ex.Message}"

