namespace Sortopia.Sorting.Serialization.Inputs

open MessagePack

open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

[<MessagePackObject>]
type sortableBoolArrayDto = {
    [<Key(0)>] Values: bool[]
    [<Key(1)>] ChannelCount: int
}

module SortableBoolArrayDto =

    let fromDomain (sba: sortableBoolArray) : sortableBoolArrayDto =
        { Values = sba.Values; ChannelCount = int sba.ChannelCount }

    let toDomain (dto: sortableBoolArrayDto) : sortableBoolArray =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        sortableBoolArray.create(dto.Values, UMX.tag<channelCount> dto.ChannelCount)
