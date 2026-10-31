
namespace Sortopia.Models.Sorting.Serialization.Simple.Si

open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization
open Sortopia.Permutations.Serialization.RatesAndOps
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Si

type mssiRandMutateDto =
    { rngFactoryDto: rngFactoryDto
      opActionRatesDto: opActionRatesDto
      mutVariant: string }

module MssiRandMutateDto =

    let fromDomain (mssiRm: mssiRandMutate) : mssiRandMutateDto =
        { rngFactoryDto = mssiRm.RngFactory |> RngFactoryDto.fromDomain
          opActionRatesDto = OpActionRatesDto.fromDomain mssiRm.OpActionRates
          mutVariant = mssiRm.MutatorVariant |> MutatorVariant.toString
        }

    let toDomain (dto: mssiRandMutateDto) : mssiRandMutate =
        mssiRandMutate.create
            (dto.rngFactoryDto |> RngFactoryDto.toDomain)
            (OpActionRatesDto.toDomain dto.opActionRatesDto)
            (dto.mutVariant |> MutatorVariant.fromString)

