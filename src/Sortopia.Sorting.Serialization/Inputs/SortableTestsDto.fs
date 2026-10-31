namespace Sortopia.Sorting.Serialization.Inputs

open MessagePack
open Sortopia.Core.Serialization
open Sortopia.Sorting.Inputs

/// Versioned envelope with a stable format tag and an explicitly keyed MessagePack payload.
[<MessagePackObject>]
type sortableTestsDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Format: string
    [<Key(2)>] Payload: byte[]
}

module SortableTestsDto =
    let fromDomain (tests: sortableTests) : sortableTestsDto =
        let format, payload =
            match tests with
            | Ints value -> "Ints", (value |> SortableIntTestsDto.fromDomain |> MessagePackSetup.serialize)
            | Bools value -> "Bools", (value |> SortableBoolTestsDto.fromDomain |> MessagePackSetup.serialize)
            | PackedInts value -> "PackedInts", (value |> PackedSortableIntTestsDto.fromDomain |> MessagePackSetup.serialize)
            | Uint8v256 value -> "Uint8v256", (value |> SortableUint8v256TestsDto.fromDomain |> MessagePackSetup.serialize)
            | Uint8v512 value -> "Uint8v512", (value |> SortableUint8v512TestsDto.fromDomain |> MessagePackSetup.serialize)
            | Bitv512 value -> "Bitv512", (value |> SortableBitv512TestsDto.fromDomain |> MessagePackSetup.serialize)
        { Version = 1; Format = format; Payload = payload }

    let toDomain (dto: sortableTestsDto) : sortableTests =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        if dto.Version <> 1 then invalidArg "dto.Version" "Unsupported sorting dataset schema version."
        if isNull dto.Payload then nullArg "dto.Payload"
        match dto.Format with
        | "Ints" -> dto.Payload |> MessagePackSetup.deserialize<sortableIntTestsDto> |> SortableIntTestsDto.toDomain |> Ints
        | "Bools" -> dto.Payload |> MessagePackSetup.deserialize<sortableBoolTestsDto> |> SortableBoolTestsDto.toDomain |> Bools
        | "PackedInts" -> dto.Payload |> MessagePackSetup.deserialize<packedSortableIntTestsDto> |> PackedSortableIntTestsDto.toDomain |> PackedInts
        | "Uint8v256" -> dto.Payload |> MessagePackSetup.deserialize<sortableUint8v256TestsDto> |> SortableUint8v256TestsDto.toDomain |> Uint8v256
        | "Uint8v512" -> dto.Payload |> MessagePackSetup.deserialize<sortableUint8v512TestsDto> |> SortableUint8v512TestsDto.toDomain |> Uint8v512
        | "Bitv512" -> dto.Payload |> MessagePackSetup.deserialize<sortableBitv512TestsDto> |> SortableBitv512TestsDto.toDomain |> Bitv512
        | _ -> invalidArg "dto.Format" "Unknown sorting dataset format."
