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
type sorterPoolSetDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Generation: int
    [<Key(2)>] TestsId: Guid
    [<Key(3)>] Pools: sorterPoolDto array
}

module SorterPoolSetDto =
    let fromDomain (value: sorterPoolSet) : sorterPoolSetDto =
        let value = SorterPoolSet.validate value
        { Version = 2; Generation = value.Generation; TestsId = %value.TestsId
          Pools = value.Pools |> Array.map (fun pool -> {
              Id = pool.Id; RawCeLength = pool.RawCeLength
              Members = pool.Members |> Array.map (fun m -> {
                  Id = m.Id; Model = SorterModelDto.fromDomain m.Model; MutationIndex = m.MutationIndex
                  MutationMod = m.MutationMod; Birthday = m.Birthday; ParentId = m.ParentId; Evaluation = SorterEvalDto.fromDomain m.Evaluation }) }) }

    let toDomain (dto: sorterPoolSetDto) : sorterPoolSet =
        if isNull (box dto) || dto.Version <> 2 || isNull dto.Pools then
            invalidArg "dto" "Unsupported or invalid SGD pool payload."
        { Generation = dto.Generation; TestsId = UMX.tag dto.TestsId
          Pools = dto.Pools |> Array.map (fun pool -> {
              Id = pool.Id; RawCeLength = pool.RawCeLength
              Members = pool.Members |> Array.map (fun m -> {
                  Id = m.Id; Model = SorterModelDto.toDomain m.Model; MutationIndex = m.MutationIndex
                  MutationMod = m.MutationMod; Birthday = m.Birthday; ParentId = m.ParentId; Evaluation = SorterEvalDto.toDomain m.Evaluation }) }) }
        |> SorterPoolSet.validate
