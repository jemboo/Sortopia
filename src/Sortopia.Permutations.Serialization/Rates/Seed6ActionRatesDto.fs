namespace Sortopia.Permutations.Serialization.RatesAndOps

open Sortopia.Core
open Sortopia.Permutations

type Seed6ActionRatesDto = {
    ortho1Thresh: float
    ortho2Thresh: float
    para1Thresh: float
    para2Thresh: float
    para3Thresh: float
    para4Thresh: float
    selfReflThresh: float
}

module Seed6ActionRatesDto =

    let toDomain (dto: Seed6ActionRatesDto) : seed6ActionRates =
        seed6ActionRates.create
            dto.ortho1Thresh
            (dto.ortho2Thresh - dto.ortho1Thresh)
            (dto.para1Thresh - dto.ortho2Thresh)
            (dto.para2Thresh - dto.para1Thresh)
            (dto.para3Thresh - dto.para2Thresh)
            (dto.para4Thresh - dto.para3Thresh)
            (dto.selfReflThresh - dto.para4Thresh)
        

    let fromDomain (domain: seed6ActionRates) : Seed6ActionRatesDto = {
        ortho1Thresh = domain.Ortho1Rate
        ortho2Thresh = domain.Ortho1Rate + domain.Ortho2Rate
        para1Thresh = domain.Ortho1Rate + domain.Ortho2Rate + domain.Para1Rate
        para2Thresh = domain.Ortho1Rate + domain.Ortho2Rate + domain.Para1Rate + domain.Para2Rate
        para3Thresh = domain.Ortho1Rate + domain.Ortho2Rate + domain.Para1Rate + domain.Para2Rate + domain.Para3Rate
        para4Thresh = domain.Ortho1Rate + domain.Ortho2Rate + domain.Para1Rate + domain.Para2Rate + domain.Para3Rate + domain.Para4Rate
        selfReflThresh = domain.Ortho1Rate + domain.Ortho2Rate + domain.Para1Rate + domain.Para2Rate + domain.Para3Rate + domain.Para4Rate + domain.SelfReflRate
    }