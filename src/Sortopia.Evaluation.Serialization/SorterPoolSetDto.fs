namespace Sortopia.Evaluation.Serialization

open System
open MessagePack
open FSharp.UMX
open Sortopia.Models.Sorting.Serialization
open Sortopia.Evaluation.Sgd

[<MessagePackObject>]
type sorterPoolMemberDto = {
    [<Key(0)>] Id: Guid
    [<Key(1)>] Model: sorterModelDto
    [<Key(2)>] MutationIndex: int
    [<Key(3)>] MutationMod: int
    [<Key(4)>] Birthday: int
    [<Key(5)>] ParentId: Guid option
    [<Key(6)>] Evaluation: sorterEvalDto
}

[<MessagePackObject>]
type sorterPoolDto = {
    [<Key(0)>] Id: Guid
    [<Key(1)>] RawCeLength: int
    [<Key(2)>] Members: sorterPoolMemberDto array
}

[<MessagePackObject>]
type sorterPoolSummaryDto = {
    [<Key(0)>] Generation: int
    [<Key(1)>] PoolIndex: int
    [<Key(2)>] MemberCount: int
    [<Key(3)>] SuccessfulCount: int
    [<Key(4)>] RawCeLength: int
    [<Key(5)>] MinimumCeLength: int
    [<Key(6)>] MinimumStageLength: int
    [<Key(7)>] BestScore: float
}

[<MessagePackObject>]
type sorterPoolSetDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Generation: int
    [<Key(2)>] TestsId: Guid
    [<Key(3)>] Pools: sorterPoolDto array
    [<Key(4)>] History: sorterPoolSummaryDto array
}

module SorterPoolSetDto =
    let fromDomain (value: sorterPoolSet) : sorterPoolSetDto =
        let value = SorterPoolSet.validate value
        { Version = 1; Generation = value.Generation; TestsId = %value.TestsId
          Pools = value.Pools |> Array.map (fun pool -> {
              Id = pool.Id; RawCeLength = pool.RawCeLength
              Members = pool.Members |> Array.map (fun m -> {
                  Id = m.Id; Model = SorterModelDto.fromDomain m.Model; MutationIndex = m.MutationIndex
                  MutationMod = m.MutationMod; Birthday = m.Birthday; ParentId = m.ParentId; Evaluation = SorterEvalDto.fromDomain m.Evaluation }) })
          History = value.History |> Array.map (fun h -> {
              Generation = h.Generation; PoolIndex = h.PoolIndex; MemberCount = h.MemberCount; SuccessfulCount = h.SuccessfulCount
              RawCeLength = h.RawCeLength; MinimumCeLength = h.MinimumCeLength; MinimumStageLength = h.MinimumStageLength; BestScore = h.BestScore }) }

    let toDomain (dto: sorterPoolSetDto) : sorterPoolSet =
        if isNull (box dto) || dto.Version <> 1 || isNull dto.Pools || isNull dto.History then
            invalidArg "dto" "Unsupported or invalid SGD pool payload."
        { Generation = dto.Generation; TestsId = UMX.tag dto.TestsId
          Pools = dto.Pools |> Array.map (fun pool -> {
              Id = pool.Id; RawCeLength = pool.RawCeLength
              Members = pool.Members |> Array.map (fun m -> {
                  Id = m.Id; Model = SorterModelDto.toDomain m.Model; MutationIndex = m.MutationIndex
                  MutationMod = m.MutationMod; Birthday = m.Birthday; ParentId = m.ParentId; Evaluation = SorterEvalDto.toDomain m.Evaluation }) })
          History = dto.History |> Array.map (fun h -> {
              Generation = h.Generation; PoolIndex = h.PoolIndex; MemberCount = h.MemberCount; SuccessfulCount = h.SuccessfulCount
              RawCeLength = h.RawCeLength; MinimumCeLength = h.MinimumCeLength; MinimumStageLength = h.MinimumStageLength; BestScore = h.BestScore }) }
        |> SorterPoolSet.validate
