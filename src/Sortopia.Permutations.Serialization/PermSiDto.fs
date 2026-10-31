namespace Sortopia.Permutations.Serialization

open Sortopia.Core
open Sortopia.Permutations

[<Struct>]
type permSiDto =
    { permutationDto: permutationDto }
    
    static member Create(arr: int array) : permSiDto =
        let permDto = permutationDto.Create(arr)
        { permutationDto = permDto }

module PermSiDto =

    let fromDomain (permSi: permSi) : permSiDto =
        { permutationDto = PermutationDto.fromDomain permSi.Permutation }

    let toDomain (dto: permSiDto) : permSi =
        let perm = PermutationDto.toDomain dto.permutationDto
        permSi.create perm.Array