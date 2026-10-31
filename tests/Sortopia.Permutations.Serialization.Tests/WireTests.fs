namespace Sortopia.Permutations.Serialization.Tests

open System
open Xunit
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization

module WireTests =
    [<Fact>]
    let ``permutation preserves legacy bytes and owns its decoded array`` () =
        // Produced by GeneSort.Core.Mp from the permutation [2; 0; 1].
        let bytes = Convert.FromHexString("81A8696E74417272617993020001")
        let dto = MessagePackSetup.deserialize<permutationDto> bytes
        let value = PermutationDto.toDomain dto
        dto.intArray.[0] <- 1
        Assert.Equal<int[]>([| 2; 0; 1 |], value.Array)
        Assert.Equal<byte[]>(bytes, value |> PermutationDto.fromDomain |> MessagePackSetup.serialize)

    [<Fact>]
    let ``duplicate permutation elements in a payload are rejected`` () =
        let bytes = Convert.FromHexString("81A8696E74417272617993020000")
        let dto = MessagePackSetup.deserialize<permutationDto> bytes
        Assert.ThrowsAny<Exception>(fun () -> PermutationDto.toDomain dto |> ignore) |> ignore
