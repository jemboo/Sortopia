namespace Sortopia.Sorting.Serialization.Networks

open MessagePack

open System
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Networks

[<MessagePackObject>]
type SorterDto = {
    [<Key(0)>] SorterId: Guid
    [<Key(1)>] Width: int
    [<Key(2)>] Ces: ceDto array
}

module SorterDto =
    let fromDomain (sorter: sorter) : SorterDto =
        { SorterId = %sorter.SorterId
          Width = %sorter.ChannelCount
          Ces = sorter.Ces |> Array.map CeDto.fromDomain }

    let toDomain (dto: SorterDto) : sorter =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        if dto.SorterId = Guid.Empty then
            failwith "Sorter ID must not be empty"
        if dto.Width < 1 then
            failwith "Width must be at least 1"
        if isNull dto.Ces then nullArg "dto.Ces"
        sorter.create
            (UMX.tag<sorterId> dto.SorterId)
            (UMX.tag<channelCount> dto.Width)
            (dto.Ces |> Array.map CeDto.toDomain)
