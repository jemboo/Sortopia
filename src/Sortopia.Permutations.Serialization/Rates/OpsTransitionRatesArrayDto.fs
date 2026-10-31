namespace Sortopia.Permutations.Serialization.RatesAndOps

open Sortopia.Core
open Sortopia.Permutations

type opsTransitionRatesArrayDto = {
    opsTransitionRatesDtos: opsTransitionRatesDto array
}

module OpsTransitionRatesArrayDto =

    let toDomain (dto: opsTransitionRatesArrayDto) : opsTransitionRatesArray =
        let rates = Array.map OpsTransitionRatesDto.toDomain dto.opsTransitionRatesDtos
        opsTransitionRatesArray.create rates

    let fromDomain (domain: opsTransitionRatesArray) : opsTransitionRatesArrayDto = {
        opsTransitionRatesDtos = Array.map OpsTransitionRatesDto.fromDomain domain.RatesArray
    }