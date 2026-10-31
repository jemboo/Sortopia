namespace Sortopia.Models.Sorting.Serialization.Simple

open Sortopia.Models.Sorting.Simple

type simpleSorterModelSDto = {
    Model: simpleSorterModelDto
    Activators: bool[]
}

module SimpleSorterModelSDto =
    let fromDomain (model: simpleSorterModelS) : simpleSorterModelSDto =
        { Model = SimpleSorterModelDto.fromDomain model.Model
          Activators = model.Activators }

    let toDomain (dto: simpleSorterModelSDto) : simpleSorterModelS =
        simpleSorterModelS.create (SimpleSorterModelDto.toDomain dto.Model) dto.Activators
