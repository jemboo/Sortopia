
namespace Sortopia.Models.Sorting.Serialization.Simple

open Sortopia.Models.Sorting.Serialization.Simple.Ce
open Sortopia.Models.Sorting.Serialization.Simple.Rs
open Sortopia.Models.Sorting.Serialization.Simple.Si
open Sortopia.Models.Sorting.Serialization.Simple.Uf4
open Sortopia.Models.Sorting.Serialization.Simple.Uf6
open Sortopia.Models.Sorting.Simple

type simpleSorterModelGenDto =
    | MsceRandGen of msceRandGenDto
    | MssiRandGen of mssiRandGenDto
    | MsrsRandGen of msrsRandGenDto
    | Msuf4RandGen of msuf4RandGenDto
    | Msuf6RandGen of msuf6RandGenDto

module SimpleSorterModelGenDto =

    let fromDomain (sorterModelGen: simpleSorterModelGen) : simpleSorterModelGenDto =
        match sorterModelGen with
        | simpleSorterModelGen.SmmMsceRandGen msceRandGen ->
            MsceRandGen (MsceRandGenDto.fromDomain msceRandGen)
        | simpleSorterModelGen.SmmMssiRandGen mssiRandGen ->
            MssiRandGen (MssiRandGenDto.fromDomain mssiRandGen)
        | simpleSorterModelGen.SmmMsrsRandGen msrsRandGen ->
            MsrsRandGen (MsrsRandGenDto.fromDomain msrsRandGen)
        | simpleSorterModelGen.SmmMsuf4RandGen msuf4RandGen ->
            Msuf4RandGen (Msuf4RandGenDto.fromDomain msuf4RandGen)
        | simpleSorterModelGen.SmmMsuf6RandGen msuf6RandGen ->
            Msuf6RandGen (Msuf6RandGenDto.fromDomain msuf6RandGen)

    let toDomain (dto: simpleSorterModelGenDto) : simpleSorterModelGen =
        try
            match dto with
            | MsceRandGen msceRandGenDto ->
                simpleSorterModelGen.SmmMsceRandGen (MsceRandGenDto.toDomain msceRandGenDto)
            | MssiRandGen mssiRandGenDto ->
                simpleSorterModelGen.SmmMssiRandGen (MssiRandGenDto.toDomain mssiRandGenDto |> Result.toOption |> Option.get )
            | MsrsRandGen msrsRandGenDto ->
                simpleSorterModelGen.SmmMsrsRandGen (MsrsRandGenDto.toDomain msrsRandGenDto)
            | Msuf4RandGen msuf4RandGenDto ->
                simpleSorterModelGen.SmmMsuf4RandGen (Msuf4RandGenDto.toDomain msuf4RandGenDto)
            | Msuf6RandGen msuf6RandGenDto ->
                simpleSorterModelGen.SmmMsuf6RandGen (Msuf6RandGenDto.toDomain msuf6RandGenDto)
        with
        | ex -> failwith $"Failed to convert sorterModelGenDto: {ex.Message}"