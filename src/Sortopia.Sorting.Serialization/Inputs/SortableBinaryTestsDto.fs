namespace Sortopia.Sorting.Serialization.Inputs

open MessagePack

open System
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

[<MessagePackObject>]
type sortableBoolTestsDto = {
    [<Key(0)>] Id: Guid
    [<Key(1)>] ChannelCount: int
    [<Key(2)>] SortableArrays: sortableBoolArrayDto[]
}

module SortableBoolTestsDto =

    let fromDomain (sbt: sortableBinaryTests) : sortableBoolTestsDto =
        { Id = %sbt.Id
          ChannelCount = int sbt.ChannelCount
          SortableArrays = sbt.SortableBinaryArrays |> Array.map SortableBoolArrayDto.fromDomain }

    let toDomain (dto: sortableBoolTestsDto) : sortableBinaryTests =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        if isNull dto.SortableArrays then nullArg "dto.SortableArrays"
        sortableBinaryTests.create
            (UMX.tag<sortableTestsId> dto.Id)
            (UMX.tag<channelCount> dto.ChannelCount)
            (dto.SortableArrays |> Array.map SortableBoolArrayDto.toDomain)
