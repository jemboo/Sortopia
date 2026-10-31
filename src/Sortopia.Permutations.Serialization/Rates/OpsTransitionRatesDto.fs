namespace Sortopia.Permutations.Serialization.RatesAndOps

open Sortopia.Core
open Sortopia.Permutations

type opsTransitionRatesDto = {
    orthoRates: opsActionRatesDto
    paraRates: opsActionRatesDto
    selfReflRates: opsActionRatesDto
}

module OpsTransitionRatesDto =

    let toDomain (dto: opsTransitionRatesDto) : opsTransitionRates =
        opsTransitionRates.create
            (OpsActionRatesDto.toDomain dto.orthoRates)
            (OpsActionRatesDto.toDomain dto.paraRates)
            (OpsActionRatesDto.toDomain dto.selfReflRates)
        

    let fromDomain (domain: opsTransitionRates) : opsTransitionRatesDto = {
        orthoRates = OpsActionRatesDto.fromDomain domain.OrthoRates
        paraRates = OpsActionRatesDto.fromDomain domain.ParaRates
        selfReflRates = OpsActionRatesDto.fromDomain domain.SelfReflRates
    }