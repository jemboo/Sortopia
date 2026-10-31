
namespace Sortopia.Models.Sorting.Serialization.Simple.Uf6

open System
open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Uf6
open Sortopia.Permutations.Serialization.RatesAndOps

type msuf6RandMutateDto =
    { id: Guid
      rngFactoryDto: rngFactoryDto
      uf6MutationRates: uf6MutationRatesDto
      mutVariant: string}

module Msuf6RandMutateDto =

    let fromDomain (msuf6Rm: msuf6RandMutate) : msuf6RandMutateDto =
        { id = %msuf6Rm.Id
          rngFactoryDto = msuf6Rm.RngFactory |> RngFactoryDto.fromDomain
          uf6MutationRates = Uf6MutationRatesDto.fromDomain msuf6Rm.Uf6MutationRates
          mutVariant = msuf6Rm.MutatorVariant |> MutatorVariant.toString
        }

    let toDomain (dto: msuf6RandMutateDto) : msuf6RandMutate =
        try
            let uf6MutationRates = Uf6MutationRatesDto.toDomain dto.uf6MutationRates
            let rngFactory = dto.rngFactoryDto |> RngFactoryDto.toDomain
            let mutVariant = dto.mutVariant |> MutatorVariant.fromString
            msuf6RandMutate.create rngFactory uf6MutationRates mutVariant
        with
        | ex -> failwith $"Failed to convert Msuf6RandMutateDto: {ex.Message}"

