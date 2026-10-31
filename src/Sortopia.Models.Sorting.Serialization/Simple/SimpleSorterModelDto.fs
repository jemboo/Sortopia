
namespace Sortopia.Models.Sorting.Serialization.Simple

open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Serialization.Simple.Ce
open Sortopia.Models.Sorting.Serialization.Simple.Rs
open Sortopia.Models.Sorting.Serialization.Simple.Si
open Sortopia.Models.Sorting.Serialization.Simple.Uf4
open Sortopia.Models.Sorting.Serialization.Simple.Uf6

type simpleSorterModelDto =
    | Msce of msceDto
    | Mssi of mssiDto
    | Msrs of msrsDto
    | Msuf4 of msuf4Dto
    | Msuf6 of msuf6Dto

module SimpleSorterModelDto =

    let fromDomain (ssm: simpleSorterModel) : simpleSorterModelDto =
        match ssm with
        | simpleSorterModel.Msce msce -> Msce (MsceDto.fromDomain msce)
        | simpleSorterModel.Mssi mssi -> Mssi (MssiDto.fromDomain mssi)
        | simpleSorterModel.Msrs msrs -> Msrs (MsrsDto.fromDomain msrs)
        | simpleSorterModel.Msuf4 msuf4 -> Msuf4 (Msuf4Dto.fromDomain msuf4)
        | simpleSorterModel.Msuf6 msuf6 -> Msuf6 (Msuf6Dto.fromDomain msuf6)

    let toDomain (dto: simpleSorterModelDto) : simpleSorterModel =
        try
            match dto with
            | Msce msceDto -> simpleSorterModel.Msce (MsceDto.toDomain msceDto)
            | Mssi mssiDto -> simpleSorterModel.Mssi (MssiDto.toDomain mssiDto)
            | Msrs msrsDto -> simpleSorterModel.Msrs (MsrsDto.toDomain msrsDto)
            | Msuf4 msuf4Dto -> simpleSorterModel.Msuf4 (Msuf4Dto.toDomain msuf4Dto)
            | Msuf6 msuf6Dto -> simpleSorterModel.Msuf6 (Msuf6Dto.toDomain msuf6Dto)
        with
        | ex -> failwith $"Failed to convert SorterModelDto: {ex.Message}"