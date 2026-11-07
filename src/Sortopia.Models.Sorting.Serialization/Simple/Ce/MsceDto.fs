namespace Sortopia.Models.Sorting.Serialization.Simple.Ce

open System
open FSharp.UMX
open Sortopia.Models.Sorting.Serialization
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Ce

type msceDto =
    { id: Guid
      channelCount: int
      ceCodes: int array
      prefixCeCodes: int array }

    static member Create(id: Guid, channelCount: int, ceCodes: int array, ?prefixCeCodes: int array) : msceDto =
        if isNull ceCodes || ceCodes.Length < 1 then
            invalidArg "ceCodes" "Must be at least 1 Ce"
        if channelCount < 1 then
            invalidArg "channelCount" "ChannelCount must be at least 1"

        { id = id; channelCount = channelCount; ceCodes = ceCodes
          prefixCeCodes = defaultArg prefixCeCodes [||] }

module MsceDto =

    let fromDomain (msce: msce) : msceDto =
        { id = %msce.Id
          channelCount = %msce.ChannelCount
          ceCodes = msce.CeCodes
          prefixCeCodes = PrefixDto.encode msce.Prefix }

    let toDomain (msceDto: msceDto) : msce =
        msce.create
            (UMX.tag<sorterModelId> msceDto.id)
            (UMX.tag<channelCount> msceDto.channelCount) (PrefixDto.decode msceDto.prefixCeCodes)
            msceDto.ceCodes
