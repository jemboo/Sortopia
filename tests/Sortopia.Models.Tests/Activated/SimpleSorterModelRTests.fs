module Sortopia.Models.Tests.SimpleSorterModelRTests

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

[<Theory>]
[<InlineData("Msce")>]
[<InlineData("Mssi")>]
[<InlineData("Msrs")>]
[<InlineData("Msuf4")>]
[<InlineData("Msuf6")>]
let ``Pairs every model variant with one activator per underlying array entry`` kind =
    let source = model kind
    let paired = simpleSorterModelR.create source 5 [|-5; 0; 5|]
    Assert.Equal(3, SimpleSorterModel.getLength source)
    Assert.Same(source, paired.Model)
    Assert.Equal(5, paired.AMax)
    Assert.Equal<int>([|-5; 0; 5|], paired.Activators)
    // Array length is not expanded CE count or a computed stage count for Msce.
    let otherLength = if kind = "Msce" then int (SimpleSorterModel.getStageLength source) else int (SimpleSorterModel.getCeLength source)
    Assert.NotEqual(3, otherLength)
    for length in [0; 2; 4; otherLength] do
        let error = Assert.Throws<ArgumentException>(fun () -> simpleSorterModelR.create source 5 (Array.zeroCreate length) |> ignore)
        Assert.Equal("activators", error.ParamName)
        Assert.Contains("Expected 3 activators", error.Message)

[<Theory>]
[<InlineData(-6)>]
[<InlineData(6)>]
[<InlineData(Int32.MinValue)>]
[<InlineData(Int32.MaxValue)>]
let ``Rejects activators outside the inclusive bounds`` value =
    let error = Assert.Throws<ArgumentException>(fun () -> simpleSorterModelR.create (model "Msrs") 5 [|0; value; 0|] |> ignore)
    Assert.Equal("activators", error.ParamName)

[<Fact>]
let ``Zero maximum permits only zeros`` () =
    let source = model "Msce"
    let paired = simpleSorterModelR.create source 0 [|0; 0; 0|]
    Assert.Equal<int>([|0; 0; 0|], paired.Activators)
    for value in [-1; 1] do
        Assert.Throws<ArgumentException>(fun () -> simpleSorterModelR.create source 0 [|value; 0; 0|] |> ignore) |> ignore

[<Theory>]
[<InlineData(-1)>]
[<InlineData(Int32.MinValue)>]
let ``Rejects negative maximum`` maximum =
    let error = Assert.Throws<ArgumentException>(fun () -> simpleSorterModelR.create (model "Msce") maximum [|0; 0; 0|] |> ignore)
    Assert.Equal("aMax", error.ParamName)

[<Fact>]
let ``Maximum integer bound handles endpoints without overflow`` () =
    let source = model "Msce"
    let values = [|-Int32.MaxValue; 0; Int32.MaxValue|]
    Assert.Equal<int>(values, (simpleSorterModelR.create source Int32.MaxValue values).Activators)
    Assert.Throws<ArgumentException>(fun () -> simpleSorterModelR.create source Int32.MaxValue [|Int32.MinValue; 0; 0|] |> ignore) |> ignore

[<Fact>]
let ``Rejects null model or activator array`` () =
    Assert.Throws<ArgumentNullException>(fun () -> simpleSorterModelR.create Unchecked.defaultof<simpleSorterModel> 5 [|0|] |> ignore) |> ignore
    Assert.Throws<ArgumentNullException>(fun () -> simpleSorterModelR.create (model "Msce") 5 null |> ignore) |> ignore

[<Fact>]
let ``External array mutation cannot change stored activators`` () =
    let values = [|-5; 0; 5|]
    let paired = simpleSorterModelR.create (model "Msce") 5 values
    values.[0] <- 99
    let exposed = paired.Activators
    exposed.[1] <- 99
    Assert.Equal<int>([|-5; 0; 5|], paired.Activators)

[<Theory>]
[<InlineData("Msce", 1)>]
[<InlineData("Mssi", 2)>]
[<InlineData("Msrs", 2)>]
[<InlineData("Msuf4", 2)>]
[<InlineData("Msuf6", 3)>]
let ``Sorter includes whole active entries in original order before truncation`` kind cesPerEntry =
    let source = model kind
    let original = SimpleSorterModel.makeSorter None source
    let blocks = original.Ces |> Array.chunkBySize cesPerEntry
    for activators, indices in
        [ [|0; -2; 1|], [|2|]
          [|1; 0; 2|], [|0; 2|]
          [|-1; 2; 0|], [|1|]
          [|1; 2; 1|], [|0; 1; 2|]
          [|0; -1; -2|], [||] ] do
        let paired = simpleSorterModelR.create source 2 activators
        let expected = indices |> Array.collect (fun index -> blocks.[index])
        for limit in [None; Some 0<ceLength>; Some 1<ceLength>; Some 3<ceLength>; Some 100<ceLength>] do
            let sorter = paired |> SimpleSorterModelR.makeSorter limit
            let expectedCes = match limit with None -> expected | Some n -> Array.truncate (int n) expected
            Assert.Equal<ce>(expectedCes, sorter.Ces)
            Assert.Equal(original.ChannelCount, sorter.ChannelCount)
            Assert.Equal(original.SorterId, sorter.SorterId)
    Assert.Equal<ce>(original.Ces, (SimpleSorterModel.makeSorter None source).Ces)

[<Fact>]
let ``Inactive permutations are skipped before expanding their orbits`` () =
    let source = mssi.create (UMX.tag (Guid.NewGuid())) 4<channelCount>
                    [|permSi.create [|0; 1; 2; 3|]; permSi.create [|1; 0; 2; 3|]; permSi.create [|3; 2; 1; 0|]|]
                 |> simpleSorterModel.Mssi
    // Fixed points cannot be expanded by the existing CE converter, but are inactive.
    let sorter = (simpleSorterModelR.create source 1 [|0; -1; 1|]).MakeSorter None
    Assert.Equal<ce>([|ce.create 0 3; ce.create 1 2|], sorter.Ces)

[<Fact>]
let ``Inactive entries are not converted to comparison elements`` () =
    let source = msce.create (UMX.tag (Guid.NewGuid())) 4<channelCount> [|-1; 1; -1|] |> simpleSorterModel.Msce
    let sorter = (simpleSorterModelR.create source 1 [|-1; 1; 0|]).MakeSorter None
    Assert.Equal<ce>([|Ce.fromIndex 1|], sorter.Ces)

let private options =
    MessagePackSerializerOptions.Standard.WithResolver(
        CompositeResolver.Create(FSharpResolver.Instance, ContractlessStandardResolver.Instance))

let private roundTrip (source: sorterModel) =
    let bytes = MessagePackSerializer.Serialize(SorterModelDto.fromDomain source, options)
    MessagePackSerializer.Deserialize<sorterModelDto>(bytes, options) |> SorterModelDto.toDomain

[<Theory>]
[<InlineData("Msce", 2, 1)>]
[<InlineData("Mssi", 4, 2)>]
[<InlineData("Msrs", 4, 2)>]
[<InlineData("Msuf4", 4, 2)>]
[<InlineData("Msuf6", 6, 2)>]
let ``SimpleR union helpers and MessagePack preserve active entries and model data`` kind ceCount stageCount =
    let source = model kind
    let paired = simpleSorterModelR.create source 5 [|1; 0; 5|]
    let wrapped = sorterModel.SimpleR paired
    let restored = roundTrip wrapped
    for current in [wrapped; restored] do
        let actual = SorterModel.asSimpleSorterModelR current
        Assert.Equal(5, actual.AMax)
        Assert.Equal<int>([|1; 0; 5|], actual.Activators)
        Assert.Equal(SimpleSorterModel.getId source, SorterModel.getId current)
        Assert.Equal(SimpleSorterModel.getChannelCount source, SorterModel.getChannelCount current)
        Assert.Equal(ceCount, int (SorterModel.getCeLength current))
        Assert.Equal(stageCount, int (SorterModel.getStageLength current))
        Assert.Equal(SorterModel.getId current, SorterModelSet.getId current)
        Assert.Equal(SorterModel.getChannelCount current, SorterModelSet.getChannelCount current)
        Assert.Equal(SorterModel.getStageLength current, SorterModelSet.getStageLength current)
        Assert.Equal<ce>((SimpleSorterModel.makeSorter None source).Ces, (SimpleSorterModel.makeSorter None actual.Model).Ces)
        for limit in [None; Some 1<ceLength>; Some 100<ceLength>] do
            Assert.Equal<ce>((paired.MakeSorter limit).Ces, (SorterModel.makeSorter current limit).Ces)
        Assert.Throws<ArgumentException>(fun () -> SorterModel.asSimpleSorterModel current |> ignore) |> ignore
    let tag = SimpleSorterModelType.fromString kind |> sorterModelType.SimpleR
    Assert.Equal(kind + "R", SorterModelType.toString tag)
    Assert.Equal(tag, SorterModelType.fromString (kind + "R"))

[<Fact>]
let ``Inactive SimpleR survives serialization as an empty sorter`` () =
    let wrapped = simpleSorterModelR.create (model "Msrs") 5 [|-5; 0; -1|] |> sorterModel.SimpleR |> roundTrip
    Assert.Empty((SorterModel.makeSorter wrapped None).Ces)
    Assert.Equal(0<ceLength>, SorterModel.getCeLength wrapped)
    Assert.Equal(0<stageLength>, SorterModel.getStageLength wrapped)
    Assert.Equal<int>([|-5; 0; -1|], (SorterModel.asSimpleSorterModelR wrapped).Activators)

[<Fact>]
let ``Simple and Unknown union cases still round trip`` () =
    let source = model "Msce"
    let restored = source |> sorterModel.Simple |> roundTrip
    Assert.Equal(SimpleSorterModel.getId source, SimpleSorterModel.getId (SorterModel.asSimpleSorterModel restored))
    Assert.Equal<ce>((SimpleSorterModel.makeSorter None source).Ces, (SorterModel.makeSorter restored None).Ces)
    Assert.Throws<ArgumentException>(fun () -> SorterModel.asSimpleSorterModelR restored |> ignore) |> ignore
    Assert.Equal(sorterModel.Unknown, roundTrip sorterModel.Unknown)

[<Fact>]
let ``MessagePack rejects invalid SimpleR activator data through domain validation`` () =
    let valid = simpleSorterModelR.create (model "Msrs") 5 [|-5; 0; 5|] |> SimpleSorterModelRDto.fromDomain
    for invalid in
        [ { valid with AMax = -1 }
          { valid with Activators = [|0; 0|] }
          { valid with Activators = [|0; 0; 6|] }
          { valid with Activators = null } ] do
        let bytes = MessagePackSerializer.Serialize(sorterModelDto.SimpleR invalid, options)
        let decoded = MessagePackSerializer.Deserialize<sorterModelDto>(bytes, options)
        let error = Assert.Throws<Exception>(fun () -> SorterModelDto.toDomain decoded |> ignore)
        Assert.Contains("Failed to convert SorterModelDto", error.Message)

[<Fact>]
let ``Model sets containing Simple and SimpleR serialize and produce filtered sorter sets`` () =
    let models = [|
        model "Msce" |> sorterModel.Simple
        simpleSorterModelR.create (model "Msrs") 2 [|0; 1; -2|] |> sorterModel.SimpleR
        simpleSorterModelR.create (model "Msuf6") 0 [|0; 0; 0|] |> sorterModel.SimpleR
    |]
    let original = sorterModelSet.create (UMX.tag (Guid.NewGuid())) models 100<ceLength>
    let bytes = MessagePackSerializer.Serialize(SorterModelSetDto.fromDomain original, options)
    let restored = MessagePackSerializer.Deserialize<sorterModelSetDto>(bytes, options) |> SorterModelSetDto.toDomain
    Assert.Equal(3, restored.Count)
    for current in restored.SorterModels do
        let expected = models |> Array.find (fun expected -> SorterModel.getId expected = SorterModel.getId current)
        Assert.Equal<ce>((SorterModel.makeSorter expected None).Ces, (SorterModel.makeSorter current None).Ces)
    let sorters = SorterModelSet.makeSorterSet (UMX.tag (Guid.NewGuid())) None restored
    Assert.Equal(3, sorters.Sorters.Length)
    Assert.Equal<int>([|0; 2; 3|], sorters.Sorters |> Array.map (fun sorter -> int sorter.ceLength) |> Array.sort)

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

[<Theory>]
[<InlineData("Msce")>]
[<InlineData("Mssi")>]
[<InlineData("Msrs")>]
[<InlineData("Msuf4")>]
[<InlineData("Msuf6")>]
let ``SimpleR mutation delegates model changes and reproduces bounded activators by ID`` kind =
    let id = Guid.Parse("9081a492-6ec3-42e9-a962-b14476d560b4") |> UMX.tag<sorterModelId>
    let source =
        match kind with
        | "Msuf4" -> msuf4.create id 16<channelCount> (Array.init 3 (fun _ -> RandomUnfolderOps4.makeRandomTwoOrbitUf4 (fun () -> 0.5) (Uf4GenRates.makeUniform 16))) |> simpleSorterModel.Msuf4
        | "Msuf6" -> msuf6.create id 12<channelCount> (Array.init 3 (fun _ -> RandomUnfolderOps6.makeRandomTwoOrbitUf6 (fun () -> 0.5) (Uf6GenRates.makeUniform 12))) |> simpleSorterModel.Msuf6
        | _ -> model kind
    let parent = simpleSorterModelR.create source 2 [|-2; 0; 2|]
    let underlying = mutator kind 1.0
    let configured = simpleSorterModelRMutator.create underlying 1.0
    let wrapped = sorterModelMutator.SimpleR configured
    Assert.Equal(configured.Id, SorterModelMutator.getId wrapped)
    let parentMap = SorterModelMutator.makeMutantIdToParentIdMap wrapped [|sorterModel.SimpleR parent|] 32 (UMX.tag 7)
    let mutable sawModelChange = false
    let mutable seenZeroSteps = Set.empty
    for index in 0 .. 31 do
        let child = SorterModelMutator.makeMutantSorterModelFromIndexAndMod wrapped (sorterModel.SimpleR parent) (UMX.tag index) (UMX.tag 7)
        let id = SorterModel.getId child
        Assert.Equal(SimpleSorterModelR.getId parent, parentMap.[id])
        let actual = SorterModel.asSimpleSorterModelR child
        let reconstructed = SorterModelMutator.makeMutantSorterModelFromId wrapped (sorterModel.SimpleR parent) id |> SorterModel.asSimpleSorterModelR
        let ordinary = SimpleSorterModelMutator.makeMutantSorterModelFromId underlying source id
        Assert.Equal<ce>((SimpleSorterModel.makeSorter None ordinary).Ces, (SimpleSorterModel.makeSorter None actual.Model).Ces)
        Assert.Equal<int>(actual.Activators, reconstructed.Activators)
        Assert.Equal(2, actual.AMax)
        Assert.All(actual.Activators, fun value -> Assert.InRange(value, -2, 2))
        Assert.Equal(1, abs actual.Activators.[1])
        seenZeroSteps <- Set.add actual.Activators.[1] seenZeroSteps
        sawModelChange <- sawModelChange || (SimpleSorterModel.makeSorter None source).Ces <> (SimpleSorterModel.makeSorter None actual.Model).Ces
        Assert.Equal<ce>((actual.MakeSorter None).Ces, (SorterModel.makeSorter (roundTrip child) None).Ces)
    Assert.True(sawModelChange)
    Assert.Equal<Set<int>>(set [-1; 1], seenZeroSteps)
    Assert.Equal<int>([|-2; 0; 2|], parent.Activators)

[<Theory>]
[<InlineData(0)>]
[<InlineData(1)>]
[<InlineData(Int32.MaxValue)>]
let ``Activator random walks respect bounds including zero and maximum integer`` maximum =
    let configured = simpleSorterModelRMutator.create (mutator "Msrs" 0.0) 1.0
    let mutable current = simpleSorterModelR.create (model "Msrs") maximum [|-maximum; 0; maximum|]
    for index in 0 .. 199 do
        let previous = current.Activators
        current <- configured.MakeSorterModelFromIndexAndMod current (UMX.tag index) (UMX.tag 0)
        Assert.All(current.Activators, fun value -> Assert.InRange(value, -maximum, maximum))
        Array.iter2 (fun before after -> Assert.InRange(abs (int64 after - int64 before), 0L, 1L)) previous current.Activators

[<Fact>]
let ``Zero activator mutation rate preserves values and parent while the model mutates`` () =
    let source = model "Msrs"
    let parent = simpleSorterModelR.create source 5 [|-5; 0; 5|]
    let configured = simpleSorterModelRMutator.create (mutator "Msrs" 1.0) 0.0
    let child = configured.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0)
    Assert.Equal<int>(parent.Activators, child.Activators)
    Assert.NotEqual(SimpleSorterModelR.getId parent, SimpleSorterModelR.getId child)
    Assert.Equal<int>([|-5; 0; 5|], parent.Activators)

[<Fact>]
let ``CE insertions and deletions preserve entry activator alignment`` () =
    let parent = simpleSorterModelR.create (model "Msce") 3 [|1; -2; 3|]
    for insertion, deletion, expected in [1.0, 0.0, [|0; 1; 0|]; 0.0, 1.0, [|0; 0; 0|]] do
        let underlying = msceRandMutate.create rngFactory.LcgFactory (indelRates.create (0.0, insertion, deletion)) (UMX.tag true) mutatorVariant.V1 |> SmmMsceRandMutate
        let child = (simpleSorterModelRMutator.create underlying 0.0).MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0)
        Assert.Equal<int>(expected, child.Activators)
        Assert.Equal(SimpleSorterModel.getLength child.Model, child.Activators.Length)
        if insertion = 1.0 then
            match child.Model, parent.Model with
            | simpleSorterModel.Msce childCe, simpleSorterModel.Msce parentCe -> Assert.Equal(parentCe.CeCodes.[0], childCe.CeCodes.[1])
            | _ -> failwith "Expected CE model"

[<Fact>]
let ``All inactive SimpleR models can mutate with zero maximum`` () =
    let source = model "Msce"
    let parent = simpleSorterModelR.create source 0 [|0; 0; 0|]
    let child = (simpleSorterModelRMutator.create (mutator "Msce" 1.0) 1.0).MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0)
    Assert.Equal<int>([|0; 0; 0|], child.Activators)
    Assert.Empty((child.MakeSorter None).Ces)

[<Fact>]
let ``SimpleR mutators validate rates and serialize their configuration`` () =
    let underlying = mutator "Msrs" 0.5
    for rate in [-0.1; 1.1; Double.NaN; Double.PositiveInfinity; Double.NegativeInfinity] do
        Assert.Throws<ArgumentException>(fun () -> simpleSorterModelRMutator.create underlying rate |> ignore) |> ignore
    let configured = simpleSorterModelRMutator.create underlying 0.25
    let bytes = MessagePackSerializer.Serialize(SimpleSorterModelRMutatorDto.fromDomain configured, options)
    let restored = MessagePackSerializer.Deserialize<simpleSorterModelRMutatorDto>(bytes, options) |> SimpleSorterModelRMutatorDto.toDomain
    Assert.Equal(configured.Id, restored.Id)
    Assert.NotEqual(configured.Id, (simpleSorterModelRMutator.create underlying 0.5).Id)
    let parent = simpleSorterModelR.create (model "Msrs") 2 [|-2; 0; 2|]
    let original = configured.MakeSorterModelFromIndexAndMod parent (UMX.tag 3) (UMX.tag 4)
    let decoded = restored.MakeSorterModelFromIndexAndMod parent (UMX.tag 3) (UMX.tag 4)
    Assert.Equal<int>(original.Activators, decoded.Activators)
    Assert.Equal<ce>((original.MakeSorter None).Ces, (decoded.MakeSorter None).Ces)
    Assert.NotEqual(SimpleSorterModelR.getId original, configured.MakeSorterModelId parent (UMX.tag 4) (UMX.tag 4))
    Assert.NotEqual(SimpleSorterModelR.getId original, configured.MakeSorterModelId parent (UMX.tag 3) (UMX.tag 5))

[<Fact>]
let ``SimpleR mutation rejects mismatched underlying models and outer union cases`` () =
    let configured = simpleSorterModelRMutator.create (mutator "Msrs" 1.0) 1.0
    let parent = simpleSorterModelR.create (model "Msce") 2 [|0; 0; 0|]
    Assert.Throws<Exception>(fun () -> configured.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0) |> ignore) |> ignore
    let wrapped = sorterModelMutator.SimpleR configured
    Assert.Throws<Exception>(fun () -> SorterModelMutator.makeMutantSorterModelFromId wrapped (model "Msrs" |> sorterModel.Simple) (UMX.tag (Guid.NewGuid())) |> ignore) |> ignore
