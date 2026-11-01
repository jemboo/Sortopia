namespace Sortopia.Models.Tests.Serialization

open System
open FSharp.UMX
open MessagePack
open MessagePack.FSharp
open MessagePack.Resolvers
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Models.Sorting.Simple.Rs
open Sortopia.Models.Sorting.Simple.Si
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.Models.Sorting.Simple.Uf6
open Sortopia.Models.Sorting.Serialization.Simple

type SorterModelDtoTests() =


    // Mock floatPicker for deterministic testing
    let mockFloatPicker () = 0.5

    // Helper function to create a TwoOrbitUf4GenRates for a given order
    let createTestGenRates (order: int) =
        Uf4GenRates.makeUniform order

    // Helper function to create a valid TwoOrbitUnfolder4 for a given order, SeedType, and optional TwoOrbitType override
    let createTestTwoOrbitUnfolder4
            (order: int)
            (topType: twoOrbitPairType)
            (twoOrbitPairTypeOverride: twoOrbitPairType option)
     : twoOrbitUf4  =
        let baseGenRates = createTestGenRates order
        let orthoRate = if topType = twoOrbitPairType.Ortho then 1.0 else 0.0
        let paraRate = if topType = twoOrbitPairType.Para then 1.0 else 0.0
        let selfSyymRate = if topType = twoOrbitPairType.SelfRefl then 1.0 else 0.0

        let ratesArray =
                match twoOrbitPairTypeOverride with
                | Some tot ->
                    let orthoRate = if topType = twoOrbitPairType.Ortho then 1.0 else 0.0
                    let paraRate = if topType = twoOrbitPairType.Para then 1.0 else 0.0
                    let selfSyymRate = if topType = twoOrbitPairType.SelfRefl then 1.0 else 0.0
                    Array.init baseGenRates.OpsGenRatesArray.RatesArray.Length (
                        fun _ -> opsGenRates.create(orthoRate, paraRate, selfSyymRate))
                | None -> baseGenRates.OpsGenRatesArray.RatesArray

        let genRates : uf4GenRates =
                    uf4GenRates.create
                        order
                        (opsGenRates.create(orthoRate, paraRate, selfSyymRate))
                        (opsGenRatesArray.create ratesArray)

        RandomUnfolderOps4.makeRandomTwoOrbitUf4 mockFloatPicker genRates


    // Helper function to create a valid TwoOrbitUnfolder4 for a given order, SeedType, and optional TwoOrbitType override
    let createTestTwoOrbitUnfolder6
            (order: int)
            (twoOrbitPairType: twoOrbitPairType)
            (twoOrbitPairTypeOverride: twoOrbitPairType option)
     : twoOrbitUf6  =
        let genRates : uf6GenRates =
            Uf6GenRates.makeUniform order

        RandomUnfolderOps6.makeRandomTwoOrbitUf6 mockFloatPicker genRates


    let resolver = CompositeResolver.Create(FSharpResolver.Instance, ContractlessStandardResolver.Instance)
    let options = MessagePackSerializerOptions.Standard.WithResolver(resolver)

    // Helper function to perform round-trip serialization
    let roundTrip (simpleSorterModel: simpleSorterModel) : simpleSorterModel =
        let dto = SimpleSorterModelDto.fromDomain simpleSorterModel
        let bytes = MessagePackSerializer.Serialize(dto, options)
        let deserializedDto = MessagePackSerializer.Deserialize<simpleSorterModelDto>(bytes, options)
        SimpleSorterModelDto.toDomain deserializedDto

    [<Fact>]
    let ``Msce round-trip serialization and deserialization should succeed`` () =
        let msce = msce.create (Guid.NewGuid() |> UMX.tag<sorterModelId>) (UMX.tag<channelCount> 16) [|1;2;3|]
        let simpleSorterModel = simpleSorterModel.Msce msce
        let result = roundTrip simpleSorterModel
        match result with
        | simpleSorterModel.Msce resultMsce ->
            Assert.Equal(msce.Id, resultMsce.Id)
            Assert.Equal(msce.ChannelCount, resultMsce.ChannelCount)
            Assert.Equal<int>(msce.CeCodes :> seq<int>, resultMsce.CeCodes :> seq<int>)
        | _ -> Assert.True(false, "Expected Msce case")

    [<Fact>]
    let ``Mssi round-trip serialization and deserialization should succeed`` () =
        let permSi = permSi.create [|1; 0; 2; 3|] // (0 1)
        let mssi = mssi.create (Guid.NewGuid() |> UMX.tag<sorterModelId>) (UMX.tag<channelCount> 4) [|permSi|]
        let simpleSorterModel = simpleSorterModel.Mssi mssi
        let result = roundTrip simpleSorterModel
        match result with
        | simpleSorterModel.Mssi resultMssi ->
            Assert.Equal(mssi.Id, resultMssi.Id)
            Assert.Equal(mssi.ChannelCount, resultMssi.ChannelCount)
            Assert.Equal<permSi>(mssi.Perm_Sis :> seq<permSi>, resultMssi.Perm_Sis :> seq<permSi>)
        | _ -> Assert.True(false, "Expected Mssi case")


    [<Fact>]
    let ``Msrs round-trip serialization and deserialization should succeed`` () =
        let permRss = [| permRs.create([| 3; 2; 1; 0 |]); permRs.create([| 1; 0; 3; 2 |]) |]
        let msrs = msrs.create (Guid.NewGuid() |> UMX.tag<sorterModelId>) (UMX.tag<channelCount> 4) permRss
        let simpleSorterModel = simpleSorterModel.Msrs msrs
        let result = roundTrip simpleSorterModel
        match result with
        | simpleSorterModel.Msrs resultMsrs ->
            Assert.Equal(msrs.Id, resultMsrs.Id)
            Assert.Equal(msrs.ChannelCount, resultMsrs.ChannelCount)
            Assert.Equal<permRs>(msrs.Perm_Rss :> seq<permRs>, resultMsrs.Perm_Rss :> seq<permRs>)
        | _ -> Assert.True(false, "Expected Msrs case")


    [<Fact>]
    let ``Msuf4 round-trip serialization and deserialization should succeed`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let width = 16<channelCount>
        let tou = createTestTwoOrbitUnfolder4 16 twoOrbitPairType.Ortho (Some twoOrbitPairType.Para)
        let msuf4 = msuf4.create id width [|tou|]
        let simpleSorterModel = simpleSorterModel.Msuf4 msuf4
        let result = roundTrip simpleSorterModel
        match result with
        | simpleSorterModel.Msuf4 resultMsuf4 ->
            Assert.Equal(msuf4.Id, resultMsuf4.Id)
            Assert.Equal(msuf4.ChannelCount, resultMsuf4.ChannelCount)
            Assert.Equal<twoOrbitUf4>(msuf4.TwoOrbitUnfolder4s :> seq<twoOrbitUf4>, resultMsuf4.TwoOrbitUnfolder4s :> seq<twoOrbitUf4>)
        | _ -> Assert.True(false, "Expected Msuf4 case")


    [<Fact>]
    let ``Msuf6 round-trip serialization and deserialization should succeed`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let order = 12
        let width = 12<channelCount>
        let tou = createTestTwoOrbitUnfolder6 order twoOrbitPairType.Ortho (Some twoOrbitPairType.Para)

        let msuf6 = msuf6.create id width [|tou|]
        let simpleSorterModel = simpleSorterModel.Msuf6 msuf6
        let result = roundTrip simpleSorterModel
        match result with
        | simpleSorterModel.Msuf6 resultMsuf6 ->
            Assert.Equal(msuf6.Id, resultMsuf6.Id)
            Assert.Equal(msuf6.ChannelCount, resultMsuf6.ChannelCount)
            Assert.Equal<twoOrbitUf6>(msuf6.TwoOrbitUnfolder6s :> seq<twoOrbitUf6>, resultMsuf6.TwoOrbitUnfolder6s :> seq<twoOrbitUf6>)
        | _ -> Assert.True(false, "Expected Msuf6 case")