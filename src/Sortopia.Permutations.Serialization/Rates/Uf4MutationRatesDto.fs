namespace Sortopia.Permutations.Serialization.RatesAndOps

open System
open MessagePack
open MessagePack.FSharp
open MessagePack.Resolvers
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Permutations.Serialization.RatesAndOps

type uf4MutationRatesDto =
    { order: int
      seedOpsTransitionRates: opsTransitionRatesDto
      twoOrbitPairOpsTransitionRates: opsTransitionRatesArrayDto }

module Uf4MutationRatesDto =

    let fromDomain (uf4Mr: uf4MutationRates) : uf4MutationRatesDto =
        { order = uf4Mr.Order
          seedOpsTransitionRates = OpsTransitionRatesDto.fromDomain uf4Mr.SeedOpsTransitionRates
          twoOrbitPairOpsTransitionRates = OpsTransitionRatesArrayDto.fromDomain uf4Mr.TwoOrbitPairOpsTransitionRates }

    let toDomain (dto: uf4MutationRatesDto) : uf4MutationRates =
        try
            if dto.order < 4 || dto.order % 4 <> 0 then
                failwith $"Order must be at least 4 and divisible by 4, got {dto.order}"
            if dto.twoOrbitPairOpsTransitionRates.opsTransitionRatesDtos.Length <> MathUtils.exactLog2 (dto.order / 4) && 
                        dto.order <> 4 then
                failwith $"TwoOrbitPairOpsTransitionRates length ({dto.twoOrbitPairOpsTransitionRates.opsTransitionRatesDtos.Length})                             
                            must match log2(order/4) ({MathUtils.exactLog2 (dto.order / 4)})"

            uf4MutationRates.create dto.order
                 (OpsTransitionRatesDto.toDomain dto.seedOpsTransitionRates)
                 (OpsTransitionRatesArrayDto.toDomain dto.twoOrbitPairOpsTransitionRates)
        with
        | ex -> failwith $"Failed to convert Uf4MutationRatesDto: {ex.Message}"