namespace Sortopia.Permutations.Serialization

open MessagePack
open Sortopia.Permutations

/// The string key is part of the original GeneSort wire format.
[<Struct; MessagePackObject>]
type permutationDto =
    { [<Key("intArray")>] intArray: int array }

    static member Create(values: int array) =
        { intArray = (permutation.create values).Array }

module PermutationDto =
    let fromDomain (perm: permutation) = { intArray = perm.Array }

    /// Validate the received permutation and detach it from the DTO buffer.
    let toDomain (dto: permutationDto) = permutation.create dto.intArray
