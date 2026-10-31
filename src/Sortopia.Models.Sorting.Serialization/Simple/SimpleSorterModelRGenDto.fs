namespace Sortopia.Models.Sorting.Serialization.Simple

open Sortopia.Models.Sorting.Simple

type simpleSorterModelRGenDto = {
    ModelGen: simpleSorterModelGenDto
    AMax: int
}

module SimpleSorterModelRGenDto =
    let fromDomain (generator: simpleSorterModelRGen) =
        { ModelGen = SimpleSorterModelGenDto.fromDomain generator.ModelGen
          AMax = generator.AMax }

    let toDomain (dto: simpleSorterModelRGenDto) =
        simpleSorterModelRGen.create (SimpleSorterModelGenDto.toDomain dto.ModelGen) dto.AMax
