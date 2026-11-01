namespace Sortopia.Evaluation.Serialization

open System
open MessagePack
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Serialization.Networks
open Sortopia.Evaluation

[<MessagePackObject>]
type ceBlockDto = {
    [<Key(0)>] CeBlockId: Guid
    [<Key(1)>] ChannelCount: int
    [<Key(2)>] Ces: ceDto array
}

module CeBlockDto =
    let fromDomain (ceBlock: ceBlock) : ceBlockDto =
        { 
            CeBlockId = %ceBlock.CeBlockId
            Ces = ceBlock.CeArray |> Array.map CeDto.fromDomain
            ChannelCount = %ceBlock.ChannelCount
        }

    let toDomain (dto: ceBlockDto) : ceBlock =
        ceBlock.create 
            (dto.CeBlockId |> UMX.tag<ceBlockId>) 
            (dto.ChannelCount |> UMX.tag<channelCount>)
            (dto.Ces |> Array.map CeDto.toDomain)