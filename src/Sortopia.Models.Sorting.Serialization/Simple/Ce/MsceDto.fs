namespace Sortopia.Models.Sorting.Serialization.Simple.Ce

open System
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Ce

type msceDto =
    { id: Guid
      channelCount: int
      ceCodes: int array }

    static member Create(id: Guid, channelCount: int, ceCodes: int array) : msceDto =
        if isNull ceCodes || ceCodes.Length < 1 then
            invalidArg "ceCodes" "Must be at least 1 Ce"
        if channelCount < 1 then
            invalidArg "channelCount" "ChannelCount must be at least 1"

        { id = id; channelCount = channelCount; ceCodes = ceCodes }

module MsceDto =

    let fromDomain (msce: msce) : msceDto =
        { id = %msce.Id
          channelCount = %msce.ChannelCount
          ceCodes = msce.CeCodes }

    let toDomain (msceDto: msceDto) : msce =
        msce.create
            (UMX.tag<sorterModelId> msceDto.id)
            (UMX.tag<channelCount> msceDto.channelCount)
            msceDto.ceCodes
