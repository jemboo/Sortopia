namespace Sortopia.Models.Sorting.Serialization.Simple

open Sortopia.Models.Sorting.Simple

type simpleSorterModelRDto = {
    Model: simpleSorterModelDto
    AMax: int
    Activators: int[]
}

module SimpleSorterModelRDto =
    let fromDomain (model: simpleSorterModelR) : simpleSorterModelRDto =
        { Model = SimpleSorterModelDto.fromDomain model.Model
          AMax = model.AMax
          Activators = model.Activators }

    let toDomain (dto: simpleSorterModelRDto) : simpleSorterModelR =
        simpleSorterModelR.create (SimpleSorterModelDto.toDomain dto.Model) dto.AMax dto.Activators
