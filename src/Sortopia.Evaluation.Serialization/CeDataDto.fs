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
        if dto.UseCount < -1 then invalidArg "dto" "Only -1 (prefix) or nonnegative usage counts are valid."
        ceUse.create (UMX.tag<ceIndex> dto.CeIndex) dto.UseCount (Ce.fromIndex dto.CeInt)
