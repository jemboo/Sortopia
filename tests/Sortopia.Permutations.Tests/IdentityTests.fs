namespace Sortopia.Permutations.Tests

open System
open System.Collections.Generic
open Xunit
open Sortopia.Core
open Sortopia.Permutations

module IdentityTests =
    let private modelId rates flag =
        GuidUtils.guidFromObjs [box "model/rate-test/v1"; box rates; box flag]

    [<Fact>]
    let ``model ID uses tagged rate content and model fields`` () =
        let rates = indelRates.create (0.25, 0.125, 0.125)
        Assert.Equal(Guid("a9094d99-dc42-6e13-2ea9-b82117d2e04e"), modelId rates true)
        Assert.NotEqual(modelId rates true, modelId rates false)
        Assert.NotEqual(modelId rates true, modelId (indelRates.create (0.125, 0.25, 0.125)) true)

    [<Fact>]
    let ``signed zero denotes the same rate in collections and model IDs`` () =
        let make rate = indelRates.create (rate, 0.25, 0.25)
        let a, b = make 0.0, make -0.0
        let keys = Dictionary<indelRates, int>()
        keys.Add(a, 7)
        Assert.Equal(7, keys.[b])
        Assert.Equal(modelId a true, modelId b true)

    [<Fact>]
    let ``nested rate IDs include all ordered elements and are independent of caller buffers`` () =
        let a = opsGenRates.create (0.25, 0.0, 0.75)
        let b = opsGenRates.create (0.0, 0.25, 0.75)
        let source = [| a; b |]
        let value = opsGenRatesArray.create source
        let originalId = modelId value false
        let keys = HashSet<opsGenRatesArray>()
        keys.Add(value) |> ignore
        source.[0] <- b
        value.RatesArray.[1] <- a
        Assert.True(keys.Contains(opsGenRatesArray.create [| a; b |]))
        Assert.Equal(originalId, modelId value false)
        Assert.NotEqual(originalId, modelId (opsGenRatesArray.create [| b; a |]) false)
        Assert.NotEqual(originalId, modelId (opsGenRatesArray.create [| a |]) false)

    [<Fact>]
    let ``permutation constructors and accessors isolate collection keys from mutation`` () =
        for create in [permutation.create; permutation.createUnsafe] do
            let source = [| 2; 0; 1 |]
            let key = create source
            let same = permutation.create [| 2; 0; 1 |]
            let keys = HashSet<permutation>()
            keys.Add(key) |> ignore
            source.[0] <- 1
            key.Array.[1] <- 2
            Assert.Equal(Guid("e801b20d-1037-e510-cd3b-719cf6c131ce"), key.Id)
            Assert.Equal<int[]>(same.Array, key.Array)
            Assert.Equal(0, compare key same)
            Assert.True(keys.Contains(same))

    [<Theory>]
    [<InlineData(1)>]
    [<InlineData(128)>]
    [<InlineData(129)>]
    let ``identity and ordinary construction use the same content ID`` (order: int) =
        let value = permutation.create (Array.init order id)
        Assert.Equal(value.Id, (Permutation.identity order).Id)
        Assert.True(Permutation.isIdentity value)
