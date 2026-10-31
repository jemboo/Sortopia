namespace Sortopia.Permutations.Serialization.RatesAndOps

open Sortopia.Core
open Sortopia.Permutations

type opsActionRatesDto = {
    orthoThresh: float
    paraThresh: float
    selfReflThresh: float
}

module OpsActionRatesDto =

    let toDomain (dto: opsActionRatesDto) : opsActionRates =
        opsActionRates.create (dto.orthoThresh) (dto.paraThresh - dto.orthoThresh) (dto.selfReflThresh - dto.paraThresh)

    let fromDomain (domain: opsActionRates) : opsActionRatesDto = {
        orthoThresh = domain.OrthoRate
        paraThresh = domain.OrthoRate + domain.ParaRate
        selfReflThresh = domain.OrthoRate + domain.ParaRate + domain.SelfReflRate
    }