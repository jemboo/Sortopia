namespace Sortopia.Evaluation.Serialization

open MessagePack
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Networks

[<MessagePackObject>]
type ceDataDto = {
    [<Key(0)>] CeIndex: int
    [<Key(1)>] UseCount: int
    [<Key(2)>] CeInt: int
}

module CeDataDto =
    let fromDomain (domain: ceUse) : ceDataDto =
        { CeIndex = %domain.CeIndex; UseCount = domain.UseCount; CeInt = Ce.toIndex domain.Ce }

    let toDomain (dto: ceDataDto) : ceUse =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        if dto.CeIndex < 0 then invalidArg "dto" "CE indices must be nonnegative."
        ceUse.create (UMX.tag<ceIndex> dto.CeIndex) dto.UseCount (Ce.fromIndex dto.CeInt)
