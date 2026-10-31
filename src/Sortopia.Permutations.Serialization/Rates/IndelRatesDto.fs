namespace Sortopia.Permutations.Serialization.RatesAndOps

open Sortopia.Core
open Sortopia.Permutations

type indelRatesDto = {
    mutationThresh: float
    insertionThresh: float
    deletionThresh: float
}

module IndelRatesDto =

    let toDomain (dto: indelRatesDto) : indelRates =
        indelRates.create (dto.mutationThresh, dto.insertionThresh - dto.mutationThresh, dto.deletionThresh - dto.insertionThresh)

    let fromDomain (domain: indelRates) : indelRatesDto = {
        mutationThresh = domain.MutationRate
        insertionThresh = domain.MutationRate + domain.InsertionRate
        deletionThresh = domain.MutationRate + domain.InsertionRate + domain.DeletionRate
    }