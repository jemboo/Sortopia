namespace Sortopia.Permutations.Serialization.RatesAndOps

open Sortopia.Core
open Sortopia.Permutations

type opsGenRatesDto = {
    orthoThresh: float
    paraThresh: float
    selfReflThresh: float
}

module OpsGenRatesDto =

    let toDomain (dto: opsGenRatesDto) : opsGenRates =
        opsGenRates.create (dto.orthoThresh, dto.paraThresh - dto.orthoThresh, dto.selfReflThresh - dto.paraThresh)

    let fromDomain (domain: opsGenRates) : opsGenRatesDto = {
        orthoThresh = domain.OrthoRate
        paraThresh = domain.OrthoRate + domain.ParaRate
        selfReflThresh = domain.OrthoRate + domain.ParaRate + domain.SelfReflRate
    }