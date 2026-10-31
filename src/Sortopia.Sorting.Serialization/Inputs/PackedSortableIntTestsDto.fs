namespace Sortopia.Sorting.Serialization.Inputs

open System
open FSharp.UMX
open MessagePack
open Sortopia.Sorting.Inputs

[<MessagePackObject>]
type packedSortableIntTestsDto = {
    [<Key(0)>] Id: Guid
    [<Key(1)>] ChannelCount: int
    [<Key(2)>] Values: int[]
}

module PackedSortableIntTestsDto =
    let fromDomain (tests: packedSortableIntTests) : packedSortableIntTestsDto =
        { Id = %tests.Id; ChannelCount = int tests.ChannelCount; Values = tests.PackedValues }
    let toDomain (dto: packedSortableIntTestsDto) =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        packedSortableIntTests.createWithId (UMX.tag dto.Id) (UMX.tag dto.ChannelCount) dto.Values
