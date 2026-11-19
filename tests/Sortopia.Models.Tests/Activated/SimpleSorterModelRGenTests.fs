module Sortopia.Models.Tests.SimpleSorterModelRGenTests

open System
open FSharp.UMX
open Xunit
open MessagePack
open MessagePack.FSharp
open MessagePack.Resolvers
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Serialization
open Sortopia.Models.Sorting.Serialization.Simple

let private generator factory kind =
    let width = if kind = "Msuf6" then 12 else 16
    SimpleSorterModelGen.makeUniform factory (UMX.tag width) [||] 5<stageLength>
        (SimpleSorterModelType.fromString kind) (UMX.tag true)

let private options =
    MessagePackSerializerOptions.Standard.WithResolver(
        CompositeResolver.Create(FSharpResolver.Instance, ContractlessStandardResolver.Instance))

let private roundTrip generator =
    let bytes = MessagePackSerializer.Serialize(SorterModelGenDto.fromDomain generator, options)
    MessagePackSerializer.Deserialize<sorterModelGenDto>(bytes, options) |> SorterModelGenDto.toDomain

let private assertSameModel (expected: sorterModel) (actual: sorterModel) =
    Assert.Equal(SorterModel.getId expected, SorterModel.getId actual)
    let e, a = SorterModel.asSimpleSorterModelR expected, SorterModel.asSimpleSorterModelR actual
    Assert.Equal(e.AMax, a.AMax)
    Assert.Equal<int>(e.Activators, a.Activators)
    Assert.Equal<ce>((SimpleSorterModel.makeSorter None e.Model).Ces, (SimpleSorterModel.makeSorter None a.Model).Ces)

[<Theory>]
[<InlineData("Msce")>]
[<InlineData("Mssi")>]
[<InlineData("Msrs")>]
[<InlineData("Msuf4")>]
[<InlineData("Msuf6")>]
let ``All SimpleR generators initialize each activator to AInitial and replay by ID`` kind =
    let underlying = generator rngFactory.LcgFactory kind
    let configured = simpleSorterModelRGen.create underlying 3 2
    let wrapped = sorterModelGen.SimpleR configured
    let restored = roundTrip wrapped
    Assert.Equal(configured.Id, SorterModelGen.getId wrapped)
    Assert.Equal(configured.Id, SorterModelGen.getId restored)
    Assert.Equal(SimpleSorterModelGen.getChannelCount underlying, SorterModelGen.getChannelCount wrapped)
    Assert.Equal(SimpleSorterModelGen.getCeLength underlying, SorterModelGen.getCeLength wrapped)
    for index in 0 .. 31 do
        let child = SorterModelGen.makeSorterModelFromIndex (UMX.tag index) wrapped
        let actual = SorterModel.asSimpleSorterModelR child
        let id = SorterModel.getId child
        Assert.Equal(configured.MakeSorterModelId (UMX.tag index), id)
        Assert.Equal(3, actual.AMax)
        Assert.Equal(SimpleSorterModel.getLength actual.Model, actual.Activators.Length)
        Assert.Equal((if kind = "Msce" then 40 else 5), actual.Activators.Length)
        Assert.All(actual.Activators, fun value -> Assert.Equal(2, value))
        let ordinary = SimpleSorterModelGen.makeSorterModelFromId id underlying
        Assert.Equal<ce>((SimpleSorterModel.makeSorter None ordinary).Ces, (SimpleSorterModel.makeSorter None actual.Model).Ces)
        assertSameModel child (SorterModelGen.makeSorterModelFromId id wrapped)
        assertSameModel child (SorterModelGen.makeSorterModelFromIndex (UMX.tag index) restored)
        let cesPerEntry = if kind = "Msce" then 1 else int configured.ChannelCount / 2
        let expected = (SimpleSorterModel.makeSorter None ordinary).Ces |> Array.chunkBySize cesPerEntry
                       |> Array.indexed |> Array.filter (fun (i, _) -> actual.Activators.[i] > 0) |> Array.collect snd
        Assert.Equal<ce>(expected, (SorterModel.makeSorter child None).Ces)
        let bytes = MessagePackSerializer.Serialize(SorterModelDto.fromDomain child, options)
        let decoded = MessagePackSerializer.Deserialize<sorterModelDto>(bytes, options) |> SorterModelDto.toDomain
        assertSameModel child decoded

[<Theory>]
[<InlineData(0)>]
[<InlineData(1)>]
[<InlineData(1073741824)>]
[<InlineData(Int32.MaxValue)>]
let ``Activator generation supports inclusive bounds across the full nonnegative maximum range`` maximum =
    for factory in [rngFactory.LcgFactory; rngFactory.NetFactory; rngFactory.SmxFactory] do
        for initial in [-maximum; 0; maximum] do
            let configured = simpleSorterModelRGen.create (generator factory "Msrs") maximum initial
            let restored = roundTrip (sorterModelGen.SimpleR configured)
            let model = SorterModelGen.makeSorterModelFromIndex (UMX.tag 0) restored |> SorterModel.asSimpleSorterModelR
            Assert.All(model.Activators, fun value -> Assert.Equal(initial, value))
            if initial <= 0 then Assert.Empty(model.MakeSorter(None).Ces)
            else Assert.NotEmpty(model.MakeSorter(None).Ces)

[<Fact>]
let ``SimpleR batch and model set generation agree with individual index and ID generation`` () =
    let configured = simpleSorterModelRGen.create (generator rngFactory.LcgFactory "Msrs") 2 1 |> sorterModelGen.SimpleR
    let models = SorterModelGen.makeSorterModelsFromIndexSpan 7<sorterCount> 4<sorterCount> configured
    let ids = models |> Array.map SorterModel.getId
    Assert.Equal(4, ids |> Array.distinct |> Array.length)
    Array.iteri (fun i model -> assertSameModel model (SorterModelGen.makeSorterModelFromIndex (UMX.tag (7 + i)) configured)) models
    Array.iter2 assertSameModel models (SorterModelGen.makeSorterModelsFromIds ids configured)
    let setId = Guid.Parse("e3522fb4-ac14-489d-a606-c2c129b35d8c") |> UMX.tag<sorterModelSetId>
    let byIndex = SorterModelGen.makeSorterModelSetFromIndexSpan setId 7<sorterCount> 4<sorterCount> configured
    let byId = SorterModelGen.makeSorterModelSetFromIds setId ids configured
    for modelSet in [byIndex; byId] do
        Assert.Equal(4, modelSet.Count)
        Assert.Equal(SorterModelGen.getCeLength configured, modelSet.RawCeLength)
        for model in modelSet.SorterModels do
            assertSameModel (models |> Array.find (fun source -> SorterModel.getId source = SorterModel.getId model)) model
    Assert.Empty(SorterModelGen.makeSorterModelsFromIndexSpan 7<sorterCount> 0<sorterCount> configured)
    Assert.Empty(SorterModelGen.makeSorterModelsFromIds [||] configured)

[<Fact>]
let ``Generator identity includes bounds and differs from the underlying generator`` () =
    let underlying = generator rngFactory.LcgFactory "Msrs"
    let a = simpleSorterModelRGen.create underlying 2 1
    let b = simpleSorterModelRGen.create underlying 3 1
    Assert.Equal(a.Id, (simpleSorterModelRGen.create underlying 2 1).Id)
    Assert.NotEqual(a.Id, (simpleSorterModelRGen.create underlying 2 0).Id)
    Assert.NotEqual(a.Id, b.Id)
    Assert.NotEqual(a.Id, SimpleSorterModelGen.getId underlying)
    Assert.NotEqual(a.MakeSorterModelId (UMX.tag 0), a.MakeSorterModelId (UMX.tag 1))
    Assert.NotEqual(a.MakeSorterModelId (UMX.tag 0), b.MakeSorterModelId (UMX.tag 0))

[<Fact>]
let ``Creation and deserialization reject negative activator bounds`` () =
    let underlying = generator rngFactory.LcgFactory "Msrs"
    for maximum in [-1; Int32.MinValue] do
        Assert.Throws<ArgumentException>(fun () -> simpleSorterModelRGen.create underlying maximum 0 |> ignore) |> ignore
        let dto = { ModelGen = SimpleSorterModelGenDto.fromDomain underlying; AMax = maximum; AInitial = 0 }
        let bytes = MessagePackSerializer.Serialize(sorterModelGenDto.SimpleR dto, options)
        let decoded = MessagePackSerializer.Deserialize<sorterModelGenDto>(bytes, options)
        Assert.Throws<ArgumentException>(fun () -> SorterModelGenDto.toDomain decoded |> ignore) |> ignore
    for initial in [Int32.MinValue; -3; 3; Int32.MaxValue] do
        Assert.Throws<ArgumentException>(fun () -> simpleSorterModelRGen.create underlying 2 initial |> ignore) |> ignore
        let dto = { ModelGen = SimpleSorterModelGenDto.fromDomain underlying; AMax = 2; AInitial = initial }
        Assert.Throws<ArgumentException>(fun () -> SimpleSorterModelRGenDto.toDomain dto |> ignore) |> ignore
    Assert.Throws<ArgumentNullException>(fun () -> simpleSorterModelRGen.create Unchecked.defaultof<simpleSorterModelGen> 2 0 |> ignore) |> ignore

[<Fact>]
let ``Existing Simple and Unknown generator cases still serialize`` () =
    let original = generator rngFactory.LcgFactory "Msrs" |> sorterModelGen.Simple
    let restored = roundTrip original
    Assert.Equal(SorterModelGen.getId original, SorterModelGen.getId restored)
    let a = SorterModelGen.makeSorterModelFromIndex (UMX.tag 3) original
    let b = SorterModelGen.makeSorterModelFromIndex (UMX.tag 3) restored
    Assert.Equal(SorterModel.getId a, SorterModel.getId b)
    Assert.Equal<ce>((SorterModel.makeSorter a None).Ces, (SorterModel.makeSorter b None).Ces)
    Assert.Equal(sorterModelGen.Unknown, roundTrip sorterModelGen.Unknown)
