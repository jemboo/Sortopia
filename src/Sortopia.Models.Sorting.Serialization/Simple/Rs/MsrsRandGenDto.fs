

namespace Sortopia.Models.Sorting.Serialization.Simple.Rs

open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization
open Sortopia.Permutations.Serialization.RatesAndOps
open Sortopia.Sorting
open Sortopia.Models.Sorting.Simple.Rs

type msrsRandGenDto =
    { channelCount: int
      stageLength: int
      rngFactoryDto: rngFactoryDto
      opsGenRatesDto: opsGenRatesDto }

module MsrsRandGenDto =

    let fromDomain (msrsRandGen: msrsRandGen) : msrsRandGenDto =
        { channelCount = %msrsRandGen.ChannelCount
          stageLength = %msrsRandGen.StageLength
          rngFactoryDto = msrsRandGen.RngFactory |> RngFactoryDto.fromDomain
          opsGenRatesDto = OpsGenRatesDto.fromDomain msrsRandGen.OpsGenRates }

    let toDomain (dto: msrsRandGenDto) : msrsRandGen =
        msrsRandGen.create
            (dto.rngFactoryDto |> RngFactoryDto.toDomain)
            (UMX.tag<channelCount> dto.channelCount)
            (OpsGenRatesDto.toDomain dto.opsGenRatesDto)
            (UMX.tag<stageLength> dto.stageLength)

