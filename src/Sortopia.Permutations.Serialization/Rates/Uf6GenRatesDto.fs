namespace Sortopia.Permutations.Serialization.RatesAndOps

open System
open MessagePack
open MessagePack.FSharp
open MessagePack.Resolvers
open Sortopia.Core
open Sortopia.Permutations

type uf6GenRatesDto =
    { order: int
      seedGenRatesUf6: seed6GenRatesDto
      opsGenRatesArrays: opsGenRatesArrayDto }

module Uf6GenRatesDto =

    let fromDomain (uf6GenRates: uf6GenRates) : uf6GenRatesDto =
        { order = uf6GenRates.Order
          seedGenRatesUf6 = Seed6GenRatesDto.fromDomain uf6GenRates.SeedGenRatesUf6
          opsGenRatesArrays = OpsGenRatesArrayDto.fromDomain uf6GenRates.OpsGenRatesArray }

    let toDomain (dto: uf6GenRatesDto) : uf6GenRates =
        try
            if dto.order < 6 || dto.order % 6 <> 0 then
                failwith $"Order must be at least 6 and divisible by 6, got {dto.order}"
            if dto.opsGenRatesArrays.opsGenRatesDtos.Length <> MathUtils.exactLog2 (dto.order / 6) then
                failwith $"OpsGenRatesArray length ({dto.opsGenRatesArrays.opsGenRatesDtos.Length}) must match log2(order/6) ({MathUtils.exactLog2 (dto.order / 6)})"

            uf6GenRates.create dto.order (Seed6GenRatesDto.toDomain dto.seedGenRatesUf6) ( OpsGenRatesArrayDto.toDomain dto.opsGenRatesArrays )
        with
        | ex -> failwith $"Failed to convert Uf6GenRatesDto: {ex.Message}"