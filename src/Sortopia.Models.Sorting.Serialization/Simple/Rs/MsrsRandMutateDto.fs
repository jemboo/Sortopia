
namespace Sortopia.Models.Sorting.Serialization.Simple.Rs

open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization
open Sortopia.Permutations.Serialization.RatesAndOps
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Rs

type msrsRandMutateDto =
    { rngFactoryDto: rngFactoryDto
      opsActionRates: opsActionRatesDto
      mutVariant: string }

module MsrsRandMutateDto =

    let fromDomain (msrsRm: msrsRandMutate) : msrsRandMutateDto =
        {
          rngFactoryDto = msrsRm.RngFactory |> RngFactoryDto.fromDomain
          opsActionRates = OpsActionRatesDto.fromDomain msrsRm.OpsActionRates
          mutVariant = msrsRm.MutatorVariant |> MutatorVariant.toString
         }

    let toDomain (dto: msrsRandMutateDto) : msrsRandMutate =
        msrsRandMutate.create
            (dto.rngFactoryDto |> RngFactoryDto.toDomain)
            (OpsActionRatesDto.toDomain dto.opsActionRates)
            (dto.mutVariant |> MutatorVariant.fromString)

