namespace Sortopia.Models.Sorting.Serialization

open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Serialization.Simple

type sorterModelGenDto =
    | Simple of simpleSorterModelGenDto
    | SimpleR of simpleSorterModelRGenDto
    | SimpleS of simpleSorterModelSGenDto
    | Unknown

module SorterModelGenDto =
    let fromDomain (generator: sorterModelGen) =
        match generator with
        | sorterModelGen.Simple g -> Simple (SimpleSorterModelGenDto.fromDomain g)
        | sorterModelGen.SimpleR g -> SimpleR (SimpleSorterModelRGenDto.fromDomain g)
        | sorterModelGen.SimpleS g -> SimpleS (SimpleSorterModelSGenDto.fromDomain g)
        | sorterModelGen.Unknown -> Unknown

    let toDomain (dto: sorterModelGenDto) =
        match dto with
        | Simple g -> SimpleSorterModelGenDto.toDomain g |> sorterModelGen.Simple
        | SimpleR g -> SimpleSorterModelRGenDto.toDomain g |> sorterModelGen.SimpleR
        | SimpleS g -> SimpleSorterModelSGenDto.toDomain g |> sorterModelGen.SimpleS
        | Unknown -> sorterModelGen.Unknown
