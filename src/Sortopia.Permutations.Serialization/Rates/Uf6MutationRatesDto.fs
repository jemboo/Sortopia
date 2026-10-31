namespace Sortopia.Permutations.Serialization.RatesAndOps

open System
open MessagePack
open MessagePack.FSharp
open MessagePack.Resolvers
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Permutations.Serialization.RatesAndOps

type uf6MutationRatesDto =
    { order: int
      seed6TransitionRates: Seed6TransitionRatesDto
      opsTransitionRates: opsTransitionRatesArrayDto }

module Uf6MutationRatesDto =

    let fromDomain (uf6MutationRates: uf6MutationRates) : uf6MutationRatesDto =
        { order = uf6MutationRates.Order
          seed6TransitionRates = Seed6TransitionRatesDto.fromDomain uf6MutationRates.Seed6TransitionRates
          opsTransitionRates = OpsTransitionRatesArrayDto.fromDomain uf6MutationRates.OpsTransitionRates }

    let toDomain (dto: uf6MutationRatesDto) : uf6MutationRates =
        try
            if dto.order < 6 || dto.order % 6 <> 0 then
                failwith $"Order must be at least 6 and divisible by 6, got {dto.order}"
            if dto.opsTransitionRates.opsTransitionRatesDtos.Length <> MathUtils.exactLog2 (dto.order / 6) && dto.order <> 6 then
                failwith $"OpsTransitionRates length ({dto.opsTransitionRates.opsTransitionRatesDtos.Length}) must match log2(order/6) ({MathUtils.exactLog2 (dto.order / 6)})"
            uf6MutationRates.create dto.order 
                (Seed6TransitionRatesDto.toDomain dto.seed6TransitionRates) 
                (OpsTransitionRatesArrayDto.toDomain dto.opsTransitionRates)
        with
        | ex -> failwith $"Failed to convert Uf6MutationRatesDto: {ex.Message}"