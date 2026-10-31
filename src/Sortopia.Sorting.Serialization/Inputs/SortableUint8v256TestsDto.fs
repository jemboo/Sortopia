namespace Sortopia.Sorting.Serialization.Inputs

open MessagePack

open System
open System.Runtime.Intrinsics
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

/// DTO for the full SIMD-optimized test suite
[<MessagePackObject>]
type sortableUint8v256TestsDto = {
    [<Key(0)>] Id: Guid
    [<Key(1)>] ChannelCount: int
    [<Key(2)>] Blocks: simdSortBlockDto[]
}

module SortableUint8v256TestsDto =

    let private blockFromDomain (block: sortBlockUint8v256) : simdSortBlockDto =
        let raw = 
            block.Vectors 
            |> Array.map (fun v -> 
                let buf = Array.zeroCreate<byte> 32
                v.CopyTo(buf.AsSpan())
                buf)
        { RawVectors = raw; SortableCount = block.SortableCount }

    let private blockToDomain (dto: simdSortBlockDto) : sortBlockUint8v256 =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        if isNull dto.RawVectors then nullArg "dto.RawVectors"
        let vecs = 
            dto.RawVectors 
            |> Array.map (fun bytes ->
                if isNull bytes || bytes.Length <> 32 then
                    invalidArg "dto.RawVectors" "Each vector must have exactly 32 bytes."
                Vector256.Create<byte>(ReadOnlySpan<byte>(bytes)))
        
        sortBlockUint8v256.createFromVectors vecs dto.SortableCount

    let fromDomain (test: sortableUint8v256Tests) : sortableUint8v256TestsDto =
        { Id = %test.Id
          ChannelCount = %test.ChannelCount
          Blocks = test.SimdSortBlocks |> Array.map blockFromDomain }

    let toDomain (dto: sortableUint8v256TestsDto) : sortableUint8v256Tests =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        let id = UMX.tag<sortableTestsId> dto.Id
        let sw = UMX.tag<channelCount> dto.ChannelCount
        if isNull dto.Blocks then nullArg "dto.Blocks"
        let blocks = dto.Blocks |> Array.map blockToDomain
        sortableUint8v256Tests.create id sw blocks
