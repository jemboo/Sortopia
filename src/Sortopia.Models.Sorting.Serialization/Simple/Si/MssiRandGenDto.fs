
namespace Sortopia.Models.Sorting.Serialization.Simple.Si

open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization
open Sortopia.Sorting
open Sortopia.Models.Sorting.Simple.Si

type mssiRandGenDto =
    { channelCount: int
      rngFactoryDto: rngFactoryDto
      stageLength: int }

module MssiRandGenDto =

    let fromDomain (mssiRandGen: mssiRandGen) : mssiRandGenDto =
        { channelCount = %mssiRandGen.ChannelCount
          rngFactoryDto = mssiRandGen.RngFactory |> RngFactoryDto.fromDomain
          stageLength = %mssiRandGen.StageLength }

    let toDomain (dto: mssiRandGenDto) : Result<mssiRandGen, string> =
        try
            if dto.channelCount < 2 then
                Error "ChannelCount must be at least 2"
            else if dto.stageLength < 1 then
                Error "StageLength must be at least 1"
            else
                let mssiRandGen =
                    mssiRandGen.create
                        (dto.rngFactoryDto |> RngFactoryDto.toDomain)
                        (UMX.tag<channelCount> dto.channelCount)
                        (UMX.tag<stageLength> dto.stageLength)
                Ok mssiRandGen
        with
        | ex -> Error ex.Message
