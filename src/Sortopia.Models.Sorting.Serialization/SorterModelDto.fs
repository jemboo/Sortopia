namespace Sortopia.Models.Sorting.Serialization

open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Serialization.Simple

type UnknownDto = UnknownDto

type sorterModelDto =
    | Simple of simpleSorterModelDto
    | Unknown of UnknownDto
    | SimpleR of simpleSorterModelRDto
    | SimpleS of simpleSorterModelSDto

module SorterModelDto =

    let fromDomain (model: sorterModel) : sorterModelDto =
        match model with
        | sorterModel.Simple sms -> Simple (SimpleSorterModelDto.fromDomain sms)
        | sorterModel.SimpleR sms -> SimpleR (SimpleSorterModelRDto.fromDomain sms)
        | sorterModel.SimpleS sms -> SimpleS (SimpleSorterModelSDto.fromDomain sms)
        | sorterModel.Unknown -> Unknown UnknownDto

    let toDomain (dto: sorterModelDto) : sorterModel =
        try
            match dto with
            | Simple simpleDto -> sorterModel.Simple (SimpleSorterModelDto.toDomain simpleDto)
            | SimpleR simpleDto -> sorterModel.SimpleR (SimpleSorterModelRDto.toDomain simpleDto)
            | SimpleS simpleDto -> sorterModel.SimpleS (SimpleSorterModelSDto.toDomain simpleDto)
            | Unknown _ -> sorterModel.Unknown
        with
        | ex -> failwith $"Failed to convert SorterModelDto: {ex.Message}"
