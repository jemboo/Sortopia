namespace Sortopia.Sorting.Serialization.Inputs

open MessagePack

open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

[<MessagePackObject>]
type sortableIntArrayDto = {
    [<Key(0)>] Values: int[]
    [<Key(1)>] ChannelCount: int
    [<Key(2)>] SymbolSetSize: int
}

module SortableIntArrayDto =

    let fromDomain (sia: sortableIntArray) : sortableIntArrayDto =
        { Values = sia.Values; ChannelCount = int sia.ChannelCount; SymbolSetSize = %sia.SymbolSetSize }

    let toDomain (dto: sortableIntArrayDto) : sortableIntArray =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        sortableIntArray.create(dto.Values, UMX.tag<channelCount> dto.ChannelCount, UMX.tag<symbolSetSize> dto.SymbolSetSize)
