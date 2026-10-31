namespace Sortopia.Core.Serialization.Tests

open System
open Xunit
open Sortopia.Core
open Sortopia.Core.Serialization

module WireTests =
    [<Fact>]
    let ``Smx factory preserves GeneSort bytes and identity`` () =
        let bytes = Convert.FromHexString("82A26964D92430303030303030302D303030302D303030302D303030302D303030303030303030303033AA726E675479706544746F02")
        let factory = MessagePackSetup.deserialize<rngFactoryDto> bytes |> RngFactoryDto.toDomain
        Assert.Equal(rngFactory.SmxFactory, factory)
        Assert.Equal<byte[]>(bytes, factory |> RngFactoryDto.fromDomain |> MessagePackSetup.serialize)

    [<Fact>]
    let ``factory id must agree with its algorithm`` () =
        let dto = RngFactoryDto.fromDomain rngFactory.SmxFactory
        let mismatched = { dto with rngTypeDto = rngTypeDto.Net }
        Assert.ThrowsAny<Exception>(fun () -> RngFactoryDto.toDomain mismatched |> ignore) |> ignore
