namespace Sortopia.Permutations.Serialization.RatesAndOps

open System
open MessagePack
open MessagePack.FSharp
open MessagePack.Resolvers
open Sortopia.Core
open Sortopia.Permutations

type uf4GenRatesDto =
    { order: int
      seedOpsGenRatesDto: opsGenRatesDto
      opsGenRatesArrayDtos: opsGenRatesArrayDto }

module Uf4GenRatesDto =

    let fromDomain (uf4GenRates: uf4GenRates) : uf4GenRatesDto =
        { order = uf4GenRates.Order
          seedOpsGenRatesDto = OpsGenRatesDto.fromDomain uf4GenRates.SeedOpsGenRates
          opsGenRatesArrayDtos = OpsGenRatesArrayDto.fromDomain uf4GenRates.OpsGenRatesArray }

    let toDomain (dto: uf4GenRatesDto) : uf4GenRates =
        try
            if dto.order < 4 || dto.order % 4 <> 0 then
                failwith $"Order must be at least 4 and divisible by 4, got {dto.order}"
            if dto.opsGenRatesArrayDtos.opsGenRatesDtos.Length <> MathUtils.exactLog2 (dto.order / 4) then
                failwith $"OpsGenRatesArray length ({dto.opsGenRatesArrayDtos.opsGenRatesDtos.Length}) must match log2(order/4) ({MathUtils.exactLog2 (dto.order / 4)})"

            uf4GenRates.create 
                dto.order 
                (OpsGenRatesDto.toDomain dto.seedOpsGenRatesDto) 
                (OpsGenRatesArrayDto.toDomain dto.opsGenRatesArrayDtos)

        with
        | ex -> failwith $"Failed to convert Uf4GenRatesDto: {ex.Message}"