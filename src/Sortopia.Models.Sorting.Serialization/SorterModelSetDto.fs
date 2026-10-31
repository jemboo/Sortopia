namespace Sortopia.Models.Sorting.Serialization

open System
open FSharp.UMX
open MessagePack
open Sortopia.Models.Sorting

[<MessagePackObject>]
type sorterModelSetDto =
    { [<Key(0)>] Id: Guid
      [<Key(1)>] Models: sorterModelDto array
      [<Key(2)>] RawCeLength: int }

module SorterModelSetDto =
    let fromDomain (models: sorterModelSet) : sorterModelSetDto =
        { Id = %models.Id
          Models = models.SorterModels |> Array.map SorterModelDto.fromDomain
          RawCeLength = %models.RawCeLength }

    let toDomain (dto: sorterModelSetDto) : sorterModelSet =
        sorterModelSet.create (UMX.tag dto.Id)
            (dto.Models |> Array.map SorterModelDto.toDomain) (UMX.tag dto.RawCeLength)
