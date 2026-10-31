namespace Sortopia.Sorting.Serialization.Networks

open MessagePack

open Sortopia.Sorting.Networks

[<MessagePackObject>]
type ceDto = {
    [<Key(0)>] Index: int
}

module CeDto =

    let fromDomain (ce: ce) : ceDto =
        { Index = Ce.toIndex ce }

    let toDomain (dto: ceDto) : ce =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        Ce.fromIndex dto.Index
