namespace Sortopia.Permutations.Serialization.RatesAndOps

open Sortopia.Core
open Sortopia.Permutations

type opActionRatesDto = {
    orthoThresh: float
    paraThresh: float
}

module OpActionRatesDto =

    let toDomain (dto: opActionRatesDto) : opActionRates =
        opActionRates.create (dto.orthoThresh, dto.paraThresh - dto.orthoThresh)

    let fromDomain (domain: opActionRates) : opActionRatesDto = {
        orthoThresh = domain.OrthoRate
        paraThresh = domain.OrthoRate + domain.ParaRate
    }