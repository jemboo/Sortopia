module Sortopia.Models.Tests.SimpleSorterModelSTests

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
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Models.Sorting.Simple.Rs
open Sortopia.Models.Sorting.Simple.Si
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.Models.Sorting.Simple.Uf6
open Sortopia.Models.Sorting.Serialization
open Sortopia.Models.Sorting.Serialization.Simple

let private model kind =
    let id = UMX.tag (Guid.NewGuid())
    match kind with
    | "Msce" -> msce.create id 4<channelCount> [|0; 1; 2|] |> simpleSorterModel.Msce
    | "Mssi" -> mssi.create id 4<channelCount> ([|[|1; 0; 3; 2|]; [|2; 3; 0; 1|]; [|3; 2; 1; 0|]|] |> Array.map permSi.create) |> simpleSorterModel.Mssi
    | "Msrs" -> msrs.create id 4<channelCount> ([|[|1; 0; 3; 2|]; [|2; 3; 0; 1|]; [|3; 2; 1; 0|]|] |> Array.map permRs.create) |> simpleSorterModel.Msrs
    | "Msuf4" -> msuf4.create id 4<channelCount> ([|twoOrbitPairType.Ortho; twoOrbitPairType.Para; twoOrbitPairType.SelfRefl|] |> Array.map (fun seed -> twoOrbitUf4.create seed [||])) |> simpleSorterModel.Msuf4
    | "Msuf6" -> msuf6.create id 6<channelCount> ([|twoOrbitTripleType.Ortho1; twoOrbitTripleType.Ortho2; twoOrbitTripleType.SelfRefl|] |> Array.map (fun seed -> twoOrbitUf6.create seed [||])) |> simpleSorterModel.Msuf6
    | _ -> failwith "Unknown test model"

let private mutator kind (rate: float) =
    let rng = rngFactory.LcgFactory
    let r = UMX.tag rate
    match kind with
    | "Msce" -> msceRandMutate.create rng (indelRates.create (rate, 0.0, 0.0)) (UMX.tag true) mutatorVariant.V1 |> SmmMsceRandMutate
    | "Mssi" -> SimpleSorterModelMutator.getMssiModelMutator rng (UMX.tag true) r (UMX.tag 1.0) (UMX.tag 1.0) mutatorVariant.V1
    | "Msrs" -> SimpleSorterModelMutator.getMsrsModelMutator rng (UMX.tag true) r (UMX.tag 1.0) (UMX.tag 1.0) (UMX.tag 1.0) mutatorVariant.V1
    | "Msuf4" -> SimpleSorterModelMutator.getMsuf4ModelMutator 16<channelCount> rng (UMX.tag true) (UMX.tag rate) r (UMX.tag 1.0) (UMX.tag 1.0) (UMX.tag 1.0) mutatorVariant.V2
    | "Msuf6" -> SimpleSorterModelMutator.getMsuf6ModelMutator 12<channelCount> rng (UMX.tag true) (UMX.tag rate) r (UMX.tag 1.0) (UMX.tag 1.0) (UMX.tag 1.0) mutatorVariant.V1
    | _ -> failwith "Unknown test mutator"

let private options =
    MessagePackSerializerOptions.Standard.WithResolver(
        CompositeResolver.Create(FSharpResolver.Instance, ContractlessStandardResolver.Instance))

let private roundTrip source =
    let bytes = MessagePackSerializer.Serialize(SorterModelDto.fromDomain source, options)
    MessagePackSerializer.Deserialize<sorterModelDto>(bytes, options) |> SorterModelDto.toDomain

[<Theory>]
[<InlineData("Msce", 1)>]
[<InlineData("Mssi", 2)>]
[<InlineData("Msrs", 2)>]
[<InlineData("Msuf4", 2)>]
[<InlineData("Msuf6", 3)>]
let ``Boolean entries filter before expansion and truncation and survive serialization`` kind blockSize =
    let source = model kind
    let blocks = (SimpleSorterModel.makeSorter None source).Ces |> Array.chunkBySize blockSize
    for mask in [ [|true; false; true|]; [|false; true; false|]; [|false; false; false|]; [|true; true; true|] ] do
        let paired = simpleSorterModelS.create source mask
        let wrapped = sorterModel.SimpleS paired
        let expected = blocks |> Array.indexed |> Array.filter (fun (i, _) -> mask.[i]) |> Array.collect snd
        for current in [wrapped; roundTrip wrapped] do
            Assert.Equal<bool>(mask, (SorterModel.asSimpleSorterModelS current).Activators)
            Assert.Equal(SimpleSorterModel.getId source, SorterModel.getId current)
            Assert.Equal(SimpleSorterModel.getChannelCount source, SorterModel.getChannelCount current)
            Assert.Equal(expected.Length, int (SorterModel.getCeLength current))
            let active = mask |> Array.filter id |> Array.length
            let stages = if kind = "Msce" then 2 * active / 4 else active
            Assert.Equal(stages, int (SorterModel.getStageLength current))
            for limit in [None; Some 0<ceLength>; Some 1<ceLength>; Some 100<ceLength>] do
                let ces = match limit with None -> expected | Some n -> Array.truncate (int n) expected
                Assert.Equal<ce>(ces, (SorterModel.makeSorter current limit).Ces)
            Assert.Throws<ArgumentException>(fun () -> SorterModel.asSimpleSorterModel current |> ignore) |> ignore
            Assert.Throws<ArgumentException>(fun () -> SorterModel.asSimpleSorterModelR current |> ignore) |> ignore
    let tag = sorterModelType.SimpleS (SimpleSorterModelType.fromString kind)
    Assert.Equal(kind + "S", SorterModelType.toString tag)
    Assert.Equal(tag, SorterModelType.fromString (kind + "S"))

[<Fact>]
let ``Boolean activators are validated and defensively copied including decoded data`` () =
    let source = model "Msrs"
    let mask = [|true; false; true|]
    let paired = simpleSorterModelS.create source mask
    mask.[0] <- false
    paired.Activators.[1] <- true
    Assert.Equal<bool>([|true; false; true|], paired.Activators)
    for invalid in [null; [||]; [|true; false|]; [|true; true; false; false|]] do
        Assert.ThrowsAny<ArgumentException>(fun () -> simpleSorterModelS.create source invalid |> ignore) |> ignore
        let dto = { SimpleSorterModelSDto.fromDomain paired with Activators = invalid }
        let bytes = MessagePackSerializer.Serialize(sorterModelDto.SimpleS dto, options)
        let decoded = MessagePackSerializer.Deserialize<sorterModelDto>(bytes, options)
        Assert.Throws<Exception>(fun () -> SorterModelDto.toDomain decoded |> ignore) |> ignore
    Assert.Throws<ArgumentNullException>(fun () -> simpleSorterModelS.create Unchecked.defaultof<_> [||] |> ignore) |> ignore
    let invalidEntry = msce.create (UMX.tag (Guid.NewGuid())) 4<channelCount> [|-1; 1|] |> simpleSorterModel.Msce
    Assert.Equal<ce>([|Ce.fromIndex 1|], (simpleSorterModelS.create invalidEntry [|false; true|]).MakeSorter(None).Ces)

[<Theory>]
[<InlineData("Msce")>]
[<InlineData("Mssi")>]
[<InlineData("Msrs")>]
[<InlineData("Msuf4")>]
[<InlineData("Msuf6")>]
let ``Boolean generation and mutation replay through unions and MessagePack`` kind =
    let width = if kind = "Msuf6" then 12 else 16
    let underlying = SimpleSorterModelGen.makeUniform rngFactory.LcgFactory (UMX.tag width) 5<stageLength>
                        (SimpleSorterModelType.fromString kind) (UMX.tag true)
    let gen = simpleSorterModelSGen.create underlying 0.5 |> sorterModelGen.SimpleS
    let genBytes = MessagePackSerializer.Serialize(SorterModelGenDto.fromDomain gen, options)
    let decodedGen = MessagePackSerializer.Deserialize<sorterModelGenDto>(genBytes, options) |> SorterModelGenDto.toDomain
    Assert.Equal(SorterModelGen.getId gen, SorterModelGen.getId decodedGen)
    let models = SorterModelGen.makeSorterModelsFromIndexSpan 3<sorterCount> 32<sorterCount> gen
    let ids = models |> Array.map SorterModel.getId
    let fromIds = SorterModelGen.makeSorterModelsFromIds ids decodedGen
    let mutable observed = Set.empty
    for parent, replay in Array.zip models fromIds do
        let paired = SorterModel.asSimpleSorterModelS parent
        observed <- Set.union observed (Set.ofArray paired.Activators)
        Assert.Equal(SimpleSorterModel.getLength paired.Model, paired.Activators.Length)
        Assert.Equal<bool>(paired.Activators, (SorterModel.asSimpleSorterModelS replay).Activators)
        for rate in [0.0; 0.35; 1.0] do
            let configured = simpleSorterModelSMutator.create (mutator kind 1.0) rate
            let bytes = MessagePackSerializer.Serialize(SimpleSorterModelSMutatorDto.fromDomain configured, options)
            let restored = MessagePackSerializer.Deserialize<simpleSorterModelSMutatorDto>(bytes, options) |> SimpleSorterModelSMutatorDto.toDomain
            Assert.Equal(configured.Id, restored.Id)
            let wrapped = sorterModelMutator.SimpleS configured
            let child = SorterModelMutator.makeMutantSorterModelFromIndexAndMod wrapped parent (UMX.tag 2) (UMX.tag 7)
            let actual = SorterModel.asSimpleSorterModelS child
            let replay = SorterModelMutator.makeMutantSorterModelFromId (sorterModelMutator.SimpleS restored) parent (SorterModel.getId child)
            Assert.Equal<bool>(actual.Activators, (SorterModel.asSimpleSorterModelS replay).Activators)
            let ordinary = SimpleSorterModelMutator.makeMutantSorterModelFromId configured.ModelMutator paired.Model (SorterModel.getId child)
            Assert.Equal<ce>((SimpleSorterModel.makeSorter None ordinary).Ces, (SimpleSorterModel.makeSorter None actual.Model).Ces)
            if rate = 0.0 then Assert.Equal<bool>(paired.Activators, actual.Activators)
            if rate = 1.0 then Assert.Equal<bool>(paired.Activators |> Array.map not, actual.Activators)
            let parentMap = SorterModelMutator.makeMutantIdToParentIdMap wrapped [|parent|] 3 (UMX.tag 7)
            Assert.Equal(SorterModel.getId parent, parentMap.[SorterModel.getId child])
    Assert.Equal<Set<bool>>(set [false; true], observed)
    let setId = UMX.tag (Guid.NewGuid())
    let generated = SorterModelGen.makeSorterModelSetFromIndexSpan setId 3<sorterCount> 32<sorterCount> gen
    let bytes = MessagePackSerializer.Serialize(SorterModelSetDto.fromDomain generated, options)
    let decoded = MessagePackSerializer.Deserialize<sorterModelSetDto>(bytes, options) |> SorterModelSetDto.toDomain
    Assert.Equal(32, decoded.Count)
    Assert.Equal(SorterModelGen.getCeLength gen, decoded.RawCeLength)
    Assert.Equal(32, (SorterModelSet.makeSorterSet (UMX.tag (Guid.NewGuid())) None decoded).Sorters.Length)

[<Fact>]
let ``Probability endpoints and invalid settings are handled explicitly`` () =
    for factory in [rngFactory.LcgFactory; rngFactory.NetFactory; rngFactory.SmxFactory] do
        let underlying = SimpleSorterModelGen.makeUniform factory 4<channelCount> 20<stageLength> simpleSorterModelType.Msrs (UMX.tag true)
        for probability in [0.0; 1.0] do
            let gen = simpleSorterModelSGen.create underlying probability
            let paired = gen.MakeSorterModelFromIndex (UMX.tag 0)
            Assert.All(paired.Activators, fun value -> Assert.Equal((probability = 1.0), value))
            let bytes = MessagePackSerializer.Serialize(SimpleSorterModelSGenDto.fromDomain gen, options)
            let restored = MessagePackSerializer.Deserialize<simpleSorterModelSGenDto>(bytes, options) |> SimpleSorterModelSGenDto.toDomain
            Assert.Equal(gen.Id, restored.Id)
            Assert.Equal<bool>(paired.Activators, (restored.MakeSorterModelFromIndex (UMX.tag 0)).Activators)
            let modelMutator = SimpleSorterModelMutator.getMsrsModelMutator factory (UMX.tag true)
                                   (UMX.tag 0.0) (UMX.tag 1.0) (UMX.tag 1.0) (UMX.tag 1.0) mutatorVariant.V1
            for rate in [0.0; 1.0] do
                let child = (simpleSorterModelSMutator.create modelMutator rate).MakeSorterModelFromIndexAndMod paired (UMX.tag 0) (UMX.tag 0)
                Assert.Equal<bool>((paired.Activators |> Array.map (fun value -> if rate = 1.0 then not value else value)), child.Activators)
        for invalid in [-0.1; 1.1; Double.NaN; Double.PositiveInfinity; Double.NegativeInfinity] do
            Assert.Throws<ArgumentException>(fun () -> simpleSorterModelSGen.create underlying invalid |> ignore) |> ignore
            Assert.Throws<ArgumentException>(fun () -> simpleSorterModelSMutator.create (mutator "Msrs" 0.0) invalid |> ignore) |> ignore
            let genDto = { ModelGen = SimpleSorterModelGenDto.fromDomain underlying; ActivationProbability = invalid }
            let bytes = MessagePackSerializer.Serialize(genDto, options)
            let decoded = MessagePackSerializer.Deserialize<simpleSorterModelSGenDto>(bytes, options)
            Assert.Throws<ArgumentException>(fun () -> SimpleSorterModelSGenDto.toDomain decoded |> ignore) |> ignore
            let mutDto = { ModelMutator = SimpleSorterModelMutateDto.fromDomain (mutator "Msrs" 0.0); ActivatorMutationRate = invalid }
            let bytes = MessagePackSerializer.Serialize(mutDto, options)
            let decoded = MessagePackSerializer.Deserialize<simpleSorterModelSMutatorDto>(bytes, options)
            Assert.Throws<ArgumentException>(fun () -> SimpleSorterModelSMutatorDto.toDomain decoded |> ignore) |> ignore
        Assert.NotEqual((simpleSorterModelSGen.create underlying 0.25).Id, (simpleSorterModelSGen.create underlying 0.75).Id)
        Assert.NotEqual(SimpleSorterModelGen.getId underlying, (simpleSorterModelSGen.create underlying 0.5).Id)

[<Fact>]
let ``Boolean CE insertions and deletions retain alignment and insert inactive entries`` () =
    let parent = simpleSorterModelS.create (model "Msce") [|true; false; true|]
    for insertion, deletion, expected in [1.0, 0.0, [|false; true; false|]; 0.0, 1.0, [|false; false; false|]] do
        let underlying = msceRandMutate.create rngFactory.LcgFactory (indelRates.create (0.0, insertion, deletion)) (UMX.tag true) mutatorVariant.V1 |> SmmMsceRandMutate
        let child = (simpleSorterModelSMutator.create underlying 0.0).MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0)
        Assert.Equal<bool>(expected, child.Activators)
        Assert.Equal(SimpleSorterModel.getLength child.Model, child.Activators.Length)
        if insertion = 1.0 then
            match child.Model, parent.Model with
            | simpleSorterModel.Msce c, simpleSorterModel.Msce p -> Assert.Equal(p.CeCodes.[0], c.CeCodes.[1])
            | _ -> failwith "Expected CE model"

