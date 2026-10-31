namespace Sortopia.Models.Sorting.Serialization.Simple

open Sortopia.Models.Sorting.Simple

type simpleSorterModelSMutatorDto = {
    ModelMutator: simpleSorterModelMutateDto
    ActivatorMutationRate: float
}

module SimpleSorterModelSMutatorDto =
    let fromDomain (mutator: simpleSorterModelSMutator) =
        { ModelMutator = SimpleSorterModelMutateDto.fromDomain mutator.ModelMutator
          ActivatorMutationRate = mutator.ActivatorMutationRate }

    let toDomain (dto: simpleSorterModelSMutatorDto) =
        simpleSorterModelSMutator.create
            (SimpleSorterModelMutateDto.toDomain dto.ModelMutator) dto.ActivatorMutationRate
