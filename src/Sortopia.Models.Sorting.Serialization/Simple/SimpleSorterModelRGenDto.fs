namespace Sortopia.Models.Sorting.Serialization.Simple

open Sortopia.Models.Sorting.Simple

type simpleSorterModelRGenDto = {
    ModelGen: simpleSorterModelGenDto
    AMax: int
    AInitial: int
}

module SimpleSorterModelRGenDto =
    let fromDomain (generator: simpleSorterModelRGen) =
        { ModelGen = SimpleSorterModelGenDto.fromDomain generator.ModelGen
          AMax = generator.AMax; AInitial = generator.AInitial }

    let toDomain (dto: simpleSorterModelRGenDto) =
        simpleSorterModelRGen.create (SimpleSorterModelGenDto.toDomain dto.ModelGen) dto.AMax dto.AInitial
