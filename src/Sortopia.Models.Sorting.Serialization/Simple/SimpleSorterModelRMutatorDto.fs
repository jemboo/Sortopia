namespace Sortopia.Models.Sorting.Serialization.Simple

open Sortopia.Models.Sorting.Simple

type simpleSorterModelRMutatorDto = {
    ModelMutator: simpleSorterModelMutateDto
    ActivatorMutationRate: float
}

module SimpleSorterModelRMutatorDto =
    let fromDomain (mutator: simpleSorterModelRMutator) =
        { ModelMutator = SimpleSorterModelMutateDto.fromDomain mutator.ModelMutator
          ActivatorMutationRate = mutator.ActivatorMutationRate }

    let toDomain (dto: simpleSorterModelRMutatorDto) =
        simpleSorterModelRMutator.create
            (SimpleSorterModelMutateDto.toDomain dto.ModelMutator) dto.ActivatorMutationRate
