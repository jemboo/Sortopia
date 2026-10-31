namespace Sortopia.Models.Sorting.Serialization.Simple.Ce

open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization
open Sortopia.Sorting
open Sortopia.Models.Sorting.Simple.Ce

type msceRandGenDto =
    { channelCount: int
      rngFactoryDto: rngFactoryDto
      ceLength: int
      excludeSelfCe: bool }

module MsceRandGenDto =

    let fromDomain (msceRandGen: msceRandGen) : msceRandGenDto =
        { channelCount = %msceRandGen.ChannelCount
          ceLength = %msceRandGen.CeLength
          rngFactoryDto = msceRandGen.RngFactory |> RngFactoryDto.fromDomain
          excludeSelfCe = %msceRandGen.ExcludeSelfCe }

    let toDomain (dto: msceRandGenDto) : msceRandGen =
        if dto.channelCount < 1 then
            failwith "ChannelCount must be at least 1"
        if dto.ceLength < 1 then
            failwith "CeCount must be at least 1"
        if dto.excludeSelfCe && dto.channelCount < 2 then
            failwith "ChannelCount must be at least 2 when ExcludeSelfCe is true"
        msceRandGen.create
                    (dto.rngFactoryDto |> RngFactoryDto.toDomain)
                    (UMX.tag<channelCount> dto.channelCount)
                    (dto.excludeSelfCe |> UMX.tag)
                    (UMX.tag<ceLength> dto.ceLength)

