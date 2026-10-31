namespace Sortopia.Sorting.Serialization.Networks

open MessagePack

open System
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Networks

[<MessagePackObject>]
type sorterSetDto = {
    [<Key(0)>] SorterSetId: Guid
    [<Key(1)>] Sorters: SorterDto array
}

module SorterSetDto =
    let fromDomain (sorterSet: sorterSet) : sorterSetDto =
        { SorterSetId = %sorterSet.Id
          Sorters = sorterSet.Sorters |> Array.map SorterDto.fromDomain }

    let toDomain (dto: sorterSetDto) : sorterSet =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        if isNull dto.Sorters then nullArg "dto.Sorters"
        sorterSet.create
            (UMX.tag<sorterSetId> dto.SorterSetId)
            (dto.Sorters |> Array.map SorterDto.toDomain)
