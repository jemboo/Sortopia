namespace Sortopia.Sorting.Serialization.Inputs

open MessagePack

open System
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

[<MessagePackObject>]
type sortableIntTestsDto = {
    [<Key(0)>] Id: Guid
    [<Key(1)>] ChannelCount: int
    [<Key(2)>] SortableArrays: sortableIntArrayDto[]
}

module SortableIntTestsDto =

    let fromDomain (sit: sortableIntTests) : sortableIntTestsDto =
        { Id = %sit.Id
          ChannelCount = int sit.ChannelCount
          SortableArrays = sit.SortableIntArrays |> Array.map SortableIntArrayDto.fromDomain }

    let toDomain (dto: sortableIntTestsDto) : sortableIntTests =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        if isNull dto.SortableArrays then nullArg "dto.SortableArrays"
        sortableIntTests.create
            (UMX.tag<sortableTestsId> dto.Id)
            (UMX.tag<channelCount> dto.ChannelCount)
            (dto.SortableArrays |> Array.map SortableIntArrayDto.toDomain)
