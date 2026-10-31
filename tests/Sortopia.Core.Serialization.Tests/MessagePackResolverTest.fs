namespace Sortopia.Core.Serialization.Tests

open System
open MessagePack
open Xunit
open Sortopia.Core.Serialization


type TestRestriction =
    | NoRestriction
    | Split

type TestUnion =
    | CaseA of string
    | CaseB of TestRestriction * int

module ResolverTests =

    [<Fact>]
    let ``nested F# unions round trip with explicit wire options`` () =
        // Arrange: Create a nested F# Discriminated Union
        let original = CaseB (Split, 42)

        let bytes = MessagePackSetup.serialize original
        let deserialized = MessagePackSetup.deserialize<TestUnion> bytes

        // Assert
        Assert.Equal(original, deserialized)