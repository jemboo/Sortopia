namespace Sortopia.Sorting.Serialization.Inputs

open MessagePack

open System
open System.Runtime.Intrinsics
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs

[<MessagePackObject>]
type sortableUint8v512TestsDto = {
    [<Key(0)>] Id: Guid
    [<Key(1)>] ChannelCount: int
    [<Key(2)>] Blocks: simdSortBlockDto[]
}

module SortableUint8v512TestsDto =

    // --- Helper for simd512SortBlock ---

    let private blockFromDomain (block: sortBlockUint8v512) : simdSortBlockDto =
        let raw = 
            block.Vectors 
            |> Array.map (fun v -> 
                let buf = Array.zeroCreate<byte> 64
                v.CopyTo(buf.AsSpan())
                buf)
        { RawVectors = raw; SortableCount = block.SortableCount }

    let private blockToDomain (dto: simdSortBlockDto) : sortBlockUint8v512 =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        if isNull dto.RawVectors then nullArg "dto.RawVectors"
        let vecs = 
            dto.RawVectors 
            |> Array.map (fun bytes ->
                if isNull bytes || bytes.Length <> 64 then
                    invalidArg "dto.RawVectors" "Each vector must have exactly 64 bytes."
                Vector512.Create<byte>(ReadOnlySpan<byte>(bytes)))
        
        sortBlockUint8v512.createFromVectors vecs dto.SortableCount

    // --- Main DTO Logic ---

    let fromDomain (test: sortableUint8v512Tests) : sortableUint8v512TestsDto =
        { Id = %test.Id
          ChannelCount = %test.ChannelCount
          Blocks = test.SimdSortBlocks |> Array.map blockFromDomain }

    let toDomain (dto: sortableUint8v512TestsDto) : sortableUint8v512Tests =
        if obj.ReferenceEquals(dto, null) then nullArg "dto"
        let id = UMX.tag<sortableTestsId> dto.Id
        let sw = UMX.tag<channelCount> dto.ChannelCount
        if isNull dto.Blocks then nullArg "dto.Blocks"
        let blocks = dto.Blocks |> Array.map blockToDomain
        sortableUint8v512Tests.create id sw blocks
