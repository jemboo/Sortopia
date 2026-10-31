namespace Sortopia.Models.Sorting.Serialization.Simple.Ce

open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization
open Sortopia.Permutations.Serialization.RatesAndOps
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Ce

type msceRandMutateDto =
    { rngFactoryDto: rngFactoryDto
      indelRatesDto: indelRatesDto
      excludeSelfCe: bool
      mutVariant: string }

module MsceRandMutateDto =

    let fromDomain (msceRm: msceRandMutate) : msceRandMutateDto =
        {
          rngFactoryDto = msceRm.RngFactory |> RngFactoryDto.fromDomain
          indelRatesDto = IndelRatesDto.fromDomain msceRm.IndelRates
          excludeSelfCe = %msceRm.ExcludeSelfCe
          mutVariant = msceRm.MutatorVariant |> MutatorVariant.toString
        }

    let toDomain (dto: msceRandMutateDto) : msceRandMutate =
        msceRandMutate.create
            (dto.rngFactoryDto |> RngFactoryDto.toDomain)
            (dto.indelRatesDto |> IndelRatesDto.toDomain)
            (dto.excludeSelfCe |> UMX.tag)
            (dto.mutVariant |> MutatorVariant.fromString)

