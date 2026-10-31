namespace Sortopia.Permutations.Serialization.RatesAndOps

open Sortopia.Core
open Sortopia.Permutations

type Seed6TransitionRatesDto = {
    ortho1Rates: Seed6ActionRatesDto
    ortho2Rates: Seed6ActionRatesDto
    para1Rates: Seed6ActionRatesDto
    para2Rates: Seed6ActionRatesDto
    para3Rates: Seed6ActionRatesDto
    para4Rates: Seed6ActionRatesDto
    selfReflRates: Seed6ActionRatesDto
}

module Seed6TransitionRatesDto =

    let toDomain (dto: Seed6TransitionRatesDto) : seed6TransitionRates =
        seed6TransitionRates.create
            (Seed6ActionRatesDto.toDomain dto.ortho1Rates)
            (Seed6ActionRatesDto.toDomain dto.ortho2Rates)
            (Seed6ActionRatesDto.toDomain dto.para1Rates)
            (Seed6ActionRatesDto.toDomain dto.para2Rates)
            (Seed6ActionRatesDto.toDomain dto.para3Rates)
            (Seed6ActionRatesDto.toDomain dto.para4Rates)
            (Seed6ActionRatesDto.toDomain dto.selfReflRates)
       

    let fromDomain (domain: seed6TransitionRates) : Seed6TransitionRatesDto = {
        ortho1Rates = Seed6ActionRatesDto.fromDomain domain.Ortho1Rates
        ortho2Rates = Seed6ActionRatesDto.fromDomain domain.Ortho2Rates
        para1Rates = Seed6ActionRatesDto.fromDomain domain.Para1Rates
        para2Rates = Seed6ActionRatesDto.fromDomain domain.Para2Rates
        para3Rates = Seed6ActionRatesDto.fromDomain domain.Para3Rates
        para4Rates = Seed6ActionRatesDto.fromDomain domain.Para4Rates
        selfReflRates = Seed6ActionRatesDto.fromDomain domain.SelfReflRates
    }