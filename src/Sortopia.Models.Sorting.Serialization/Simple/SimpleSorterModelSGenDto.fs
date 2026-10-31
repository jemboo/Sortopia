namespace Sortopia.Models.Sorting.Serialization.Simple

open Sortopia.Models.Sorting.Simple

type simpleSorterModelSGenDto = {
    ModelGen: simpleSorterModelGenDto
    ActivationProbability: float
}

module SimpleSorterModelSGenDto =
    let fromDomain (generator: simpleSorterModelSGen) =
        { ModelGen = SimpleSorterModelGenDto.fromDomain generator.ModelGen
          ActivationProbability = generator.ActivationProbability }

    let toDomain (dto: simpleSorterModelSGenDto) =
        simpleSorterModelSGen.create (SimpleSorterModelGenDto.toDomain dto.ModelGen) dto.ActivationProbability
