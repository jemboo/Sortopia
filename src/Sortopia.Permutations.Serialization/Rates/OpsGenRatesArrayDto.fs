namespace Sortopia.Permutations.Serialization.RatesAndOps

open Sortopia.Core
open Sortopia.Permutations

type opsGenRatesArrayDto = {
    opsGenRatesDtos: opsGenRatesDto array
}

module OpsGenRatesArrayDto =

    let toDomain (dto: opsGenRatesArrayDto) : opsGenRatesArray =
        let rates = Array.map OpsGenRatesDto.toDomain dto.opsGenRatesDtos
        opsGenRatesArray.create rates

    let fromDomain (domain: opsGenRatesArray) : opsGenRatesArrayDto = {
        opsGenRatesDtos = Array.map OpsGenRatesDto.fromDomain domain.RatesArray
    }