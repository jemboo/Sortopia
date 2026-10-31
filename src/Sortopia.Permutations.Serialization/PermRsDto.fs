namespace Sortopia.Permutations.Serialization

open Sortopia.Core
open Sortopia.Permutations

[<Struct>]
type permRsDto =
    { permSiDto: permSiDto }
    
    static member Create(arr: int array) : permRsDto =
        if arr.Length < 4 then
            failwith "Perm_Rs order must be at least 4"
        else if arr.Length % 2 <> 0 then
            failwith "Perm_Rs order must be divisible by 2"
        else
            { permSiDto = permSiDto.Create(arr) }

module PermRsDto =

    let toPerm_RsDto (permRs: permRs) : permRsDto =
        { permSiDto = PermSiDto.fromDomain permRs.PermSi }

    let toPerm_Rs (dto: permRsDto) : permRs =
        let permSi = PermSiDto.toDomain dto.permSiDto
        permRs.create permSi.Array