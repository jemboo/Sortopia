module Sortopia.Models.Tests.PrefixTests

open System
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Core.Serialization
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Models.Sorting.Simple.Si
open Sortopia.Models.Sorting.Simple.Rs
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.Models.Sorting.Simple.Uf6
open Sortopia.Models.Sorting.Serialization
open Sortopia.Models.Sorting.Serialization.Simple.Ce
open Sortopia.Evaluation

let private prefix () = [| ce.create 0 1; ce.create 1 2; ce.create 2 3 |]
let private modelId = UMX.tag<sorterModelId> (Guid.Parse "d6bd1976-b2f3-412d-b439-dca351f059aa")

let private generator kind prefix =
    let width, modelType =
        match kind with
        | "ce" -> 8, simpleSorterModelType.Msce
        | "si" -> 8, simpleSorterModelType.Mssi
        | "rs" -> 8, simpleSorterModelType.Msrs
        | "uf4" -> 8, simpleSorterModelType.Msuf4
        | "uf6" -> 12, simpleSorterModelType.Msuf6
        | _ -> failwith "Unknown test family"
    SimpleSorterModelGen.makeUniform rngFactory.SmxFactory (UMX.tag width) prefix 3<stageLength> modelType (UMX.tag true)

let private model kind prefix =
    generator kind prefix |> SimpleSorterModelGen.makeSorterModelFromId modelId

let private replacePrefix prefix = function
    | simpleSorterModel.Msce m -> msce.create m.Id m.ChannelCount prefix m.CeCodes |> simpleSorterModel.Msce
    | simpleSorterModel.Mssi m -> mssi.create m.Id m.ChannelCount prefix m.Perm_Sis |> simpleSorterModel.Mssi
    | simpleSorterModel.Msrs m -> msrs.create m.Id m.ChannelCount prefix m.Perm_Rss |> simpleSorterModel.Msrs
    | simpleSorterModel.Msuf4 m -> msuf4.create m.Id m.ChannelCount prefix m.TwoOrbitUnfolder4s |> simpleSorterModel.Msuf4
    | simpleSorterModel.Msuf6 m -> msuf6.create m.Id m.ChannelCount prefix m.TwoOrbitUnfolder6s |> simpleSorterModel.Msuf6

let private wrap (m: simpleSorterModel) =
    let length = SimpleSorterModel.getLength m
    [| sorterModel.Simple m
       simpleSorterModelR.create m 1 (Array.init length (fun i -> if i % 2 = 0 then 1 else 0)) |> sorterModel.SimpleR
       simpleSorterModelS.create m (Array.init length (fun i -> i % 2 = 0)) |> sorterModel.SimpleS |]

[<Theory>]
[<InlineData("ce")>]
[<InlineData("si")>]
[<InlineData("rs")>]
[<InlineData("uf4")>]
[<InlineData("uf6")>]
let ``prefix is immutable and included in model and generator equality`` kind =
    let input = prefix ()
    let gen = generator kind input
    let source = model kind [||]
    let m = replacePrefix input source
    input.[0] <- ce.create 0 7
    (SimpleSorterModel.getPrefix m).[1] <- ce.create 0 7
    (SimpleSorterModelGen.getPrefix gen).[1] <- ce.create 0 7
    Assert.Equal<ce>(prefix (), SimpleSorterModel.getPrefix m)
    Assert.Equal<ce>(prefix (), SimpleSorterModelGen.getPrefix gen)
    let same = replacePrefix (prefix ()) source
    let different = replacePrefix (prefix () |> Array.rev) source
    Assert.Equal(m, same)
    Assert.Equal(m.GetHashCode(), same.GetHashCode())
    Assert.NotEqual(m, different)
    Assert.Equal(gen, generator kind (prefix ()))
    Assert.Equal(gen.GetHashCode(), (generator kind (prefix ())).GetHashCode())
    Assert.NotEqual(gen, generator kind (prefix () |> Array.rev))
    Assert.Throws<ArgumentNullException>(fun () -> replacePrefix null source |> ignore) |> ignore
    Assert.Throws<ArgumentNullException>(fun () -> generator kind null |> ignore) |> ignore
    let bad = [|ce.create 0 (int (SimpleSorterModel.getChannelCount source))|]
    Assert.Throws<ArgumentException>(fun () -> replacePrefix bad source |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> generator kind bad |> ignore) |> ignore

[<Theory>]
[<InlineData("ce")>]
[<InlineData("si")>]
[<InlineData("rs")>]
[<InlineData("uf4")>]
[<InlineData("uf6")>]
let ``both sorter forms limit only the suffix and have distinct materialized identities`` kind =
    for m in model kind (prefix ()) |> wrap do
        let all = (SorterModel.makeSorterWithoutPrefix m None).Ces
        for limit in [None; Some 0<ceLength>; Some 1<ceLength>; Some 1000<ceLength>] do
            let suffix = SorterModel.makeSorterWithoutPrefix m limit
            let full = SorterModel.makeSorterWithPrefix m limit
            let expectedSuffix = match limit with None -> all | Some n -> Array.truncate (int n) all
            Assert.Equal<ce>(expectedSuffix, suffix.Ces)
            Assert.Equal<ce>(Array.append (prefix ()) expectedSuffix, full.Ces)
            Assert.NotEqual(suffix.SorterId, full.SorterId)
            Assert.Equal(full.SorterId, (SorterModel.makeSorterWithPrefix m limit).SorterId)
        Assert.NotEqual((SorterModel.makeSorterWithPrefix m None).SorterId,
                        (SorterModel.makeSorterWithPrefix m (Some 0<ceLength>)).SorterId)
        Assert.Throws<ArgumentException>(fun () -> SorterModel.makeSorterWithoutPrefix m (Some -1<ceLength>) |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> SorterModel.makeSorterWithPrefix m (Some -1<ceLength>) |> ignore) |> ignore
    let plain = model kind [||] |> sorterModel.Simple
    Assert.Equal(SorterModel.makeSorterWithoutPrefix plain None, SorterModel.makeSorterWithPrefix plain None)
    let source = model kind (prefix ())
    let inactiveR = simpleSorterModelR.create source 0 (Array.zeroCreate (SimpleSorterModel.getLength source))
    let inactiveS = simpleSorterModelS.create source (Array.create (SimpleSorterModel.getLength source) false)
    Assert.Empty((inactiveR.MakeSorterWithoutPrefix None).Ces)
    Assert.Empty((inactiveS.MakeSorterWithoutPrefix None).Ces)
    Assert.Equal<ce>(prefix (), (inactiveR.MakeSorterWithPrefix None).Ces)
    Assert.Equal<ce>(prefix (), (inactiveS.MakeSorterWithPrefix None).Ces)

[<Theory>]
[<InlineData("ce")>]
[<InlineData("si")>]
[<InlineData("rs")>]
[<InlineData("uf4")>]
[<InlineData("uf6")>]
let ``generator identity includes prefix order and all generation paths preserve it`` kind =
    let a = generator kind (prefix ())
    let b = generator kind (prefix () |> Array.rev)
    Assert.NotEqual(SimpleSorterModelGen.getId a, SimpleSorterModelGen.getId b)
    let indexedA = SimpleSorterModelGen.makeSorterModelFromIndex (UMX.tag 7) a
    let indexedB = SimpleSorterModelGen.makeSorterModelFromIndex (UMX.tag 7) b
    Assert.NotEqual(SimpleSorterModel.getId indexedA, SimpleSorterModel.getId indexedB)
    Assert.Equal<ce>(prefix (), SimpleSorterModel.getPrefix indexedA)
    for gen in [| sorterModelGen.Simple a
                  simpleSorterModelRGen.create a 2 |> sorterModelGen.SimpleR
                  simpleSorterModelSGen.create a 0.5 |> sorterModelGen.SimpleS |] do
        let restored = gen |> SorterModelGenDto.fromDomain |> MessagePackSetup.serialize
                       |> MessagePackSetup.deserialize<sorterModelGenDto> |> SorterModelGenDto.toDomain
        Assert.Equal(SorterModelGen.getId gen, SorterModelGen.getId restored)
        Assert.Equal<ce>(prefix (), SorterModelGen.getPrefix restored)
        for make in [ (fun g -> SorterModelGen.makeSorterModelFromId modelId g)
                      (fun g -> SorterModelGen.makeSorterModelFromIndex (UMX.tag 4) g) ] do
            let first, replay = make gen, make restored
            Assert.Equal<ce>(prefix (), SorterModel.getPrefix replay)
            Assert.Equal(SorterModel.makeSorterWithPrefix first None, SorterModel.makeSorterWithPrefix replay None)

[<Theory>]
[<InlineData("ce")>]
[<InlineData("si")>]
[<InlineData("rs")>]
[<InlineData("uf4")>]
[<InlineData("uf6")>]
let ``mutation and serialization retain the fixed prefix for simple and activated models`` kind =
    let source = model kind (prefix ())
    let mutator = RandomNetwork.createMutator source 1.0
    let r = simpleSorterModelR.create source 1 (Array.create (SimpleSorterModel.getLength source) 1)
    let s = simpleSorterModelS.create source (Array.create (SimpleSorterModel.getLength source) true)
    for index in 0 .. 3 do
        let rMutator = simpleSorterModelRMutator.create mutator 1.0
        let sMutator = simpleSorterModelSMutator.create mutator 1.0
        let children =
            [| SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod mutator source (UMX.tag index) (UMX.tag 0) |> sorterModel.Simple
               rMutator.MakeSorterModelFromIndexAndMod r (UMX.tag index) (UMX.tag 0) |> sorterModel.SimpleR
               sMutator.MakeSorterModelFromIndexAndMod s (UMX.tag index) (UMX.tag 0) |> sorterModel.SimpleS |]
        for child in children do
            let restored = child |> SorterModelDto.fromDomain |> MessagePackSetup.serialize
                           |> MessagePackSetup.deserialize<sorterModelDto> |> SorterModelDto.toDomain
            Assert.Equal<ce>(prefix (), SorterModel.getPrefix child)
            Assert.Equal<ce>(prefix (), SorterModel.getPrefix restored)
            Assert.Equal(SorterModel.makeSorterWithPrefix child None, SorterModel.makeSorterWithPrefix restored None)
    Assert.Equal<ce>(prefix (), SimpleSorterModel.getPrefix source)

[<Fact>]
let ``CE insertion and deletion never change the prefix including activated mutation branches`` () =
    let source = model "ce" (prefix ())
    for rates in [Sortopia.Permutations.indelRates.create (0., 1., 0.); Sortopia.Permutations.indelRates.create (0., 0., 1.)] do
        let mutator = msceRandMutate.create rngFactory.SmxFactory rates (UMX.tag true) mutatorVariant.V1 |> simpleSorterModelMutator.SmmMsceRandMutate
        let r = simpleSorterModelR.create source 1 (Array.create (SimpleSorterModel.getLength source) 1)
        let s = simpleSorterModelS.create source (Array.create (SimpleSorterModel.getLength source) true)
        let children =
            [| SimpleSorterModelMutator.makeMutantSorterModelFromId mutator source modelId |> sorterModel.Simple
               (simpleSorterModelRMutator.create mutator 0.5).MakeSorterModelFromId r modelId |> sorterModel.SimpleR
               (simpleSorterModelSMutator.create mutator 0.5).MakeSorterModelFromId s modelId |> sorterModel.SimpleS |]
        for child in children do Assert.Equal<ce>(prefix (), SorterModel.getPrefix child)

// This is the original three-field MessagePack record shape, before prefixes were introduced.
type legacyMsceDto = { id: Guid; channelCount: int; ceCodes: int array }
type legacyMsceGenDto =
    { channelCount: int; rngFactoryDto: Sortopia.Core.Serialization.rngFactoryDto; ceLength: int; excludeSelfCe: bool }

[<Fact>]
let ``old MessagePack model and generator records default to an empty prefix`` () =
    let oldModel : legacyMsceDto = { id = UMX.untag modelId; channelCount = 8; ceCodes = [|1; 3|] }
    let restored = oldModel |> MessagePackSetup.serialize |> MessagePackSetup.deserialize<msceDto> |> MsceDto.toDomain
    Assert.Empty(restored.Prefix)
    Assert.Equal(modelId, restored.Id)
    let oldGen : legacyMsceGenDto =
        { channelCount = 8; rngFactoryDto = RngFactoryDto.fromDomain rngFactory.SmxFactory; ceLength = 8; excludeSelfCe = true }
    let gen = oldGen |> MessagePackSetup.serialize |> MessagePackSetup.deserialize<msceRandGenDto> |> MsceRandGenDto.toDomain
    Assert.Empty(gen.Prefix)
    Assert.Equal((msceRandGen.create rngFactory.SmxFactory 8<channelCount> [||] (UMX.tag true) 8<ceLength>).Id, gen.Id)

let private apply (ces: ce array) (input: int array) =
    let values = Array.copy input
    for ce in ces do
        let lo, hi = int ce.LowChannel, int ce.HighChannel
        if values.[lo] > values.[hi] then
            let value = values.[lo]
            values.[lo] <- values.[hi]
            values.[hi] <- value
    values

[<Theory>]
[<InlineData("ce")>]
[<InlineData("si")>]
[<InlineData("rs")>]
[<InlineData("uf4")>]
[<InlineData("uf6")>]
let ``presorted suffix evaluation matches full sorter execution`` kind =
    for m in model kind (prefix ()) |> wrap do
        let width = SorterModel.getChannelCount m
        let inputs = Array.init (1 <<< int width) (fun bits -> Array.init (int width) (fun channel -> (bits >>> channel) &&& 1))
        for limit in [None; Some 0<ceLength>; Some 1<ceLength>] do
            let full = SorterModel.makeSorterWithPrefix m limit
            let suffix = SorterModel.makeSorterWithoutPrefix m limit
            let presorted = inputs |> Array.map (apply (prefix ()))
            Array.iter2 (fun input presorted ->
                let expected = apply full.Ces input
                let actual = apply suffix.Ces presorted
                Assert.Equal<int>(expected :> seq<int>, actual :> seq<int>)) inputs presorted
            let datasets values =
                let ints = values |> Array.map (fun input -> sortableIntArray.create(input, width, 2<symbolSetSize>))
                let bools = values |> Array.map (fun input -> sortableBoolArray.create(Array.map ((<>) 0) input, width))
                [| sortableIntTests.create (UMX.tag (Guid.NewGuid())) width ints |> sortableTests.Ints
                   SortableBitv512Tests.fromBoolArrays (UMX.tag (Guid.NewGuid())) width bools |> sortableTests.Bitv512 |]
            let block ces = ceBlock.create (UMX.tag (Guid.NewGuid())) width ces
            for original, presorted in Array.zip (datasets inputs) (datasets presorted) do
                let fullResult = CeBlockOps.evalWithSorterTest original ceBlock.Empty (block full.Ces) (UMX.tag false)
                let suffixResult = CeBlockOps.evalWithSorterTest presorted (block (prefix ())) (block suffix.Ces) (UMX.tag false)
                Assert.Equal(fullResult.UnsortedCount, suffixResult.UnsortedCount)
                let expectedUsage = fullResult.UseCountArray |> Array.skip (prefix ()).Length
                Assert.Equal<int>(expectedUsage :> seq<int>, suffixResult.UseCountArray :> seq<int>)
                Assert.Equal<ce>(prefix (), suffixResult.UsedCes |> Array.take (prefix ()).Length)
                Assert.All(suffixResult.extractCeUseArray |> Array.take (prefix ()).Length, fun usage -> Assert.Equal(-1, usage.UseCount))

[<Fact>]
let ``odd width adjacent prefix leaves the last channel untouched`` () =
    let prefix = [|for low in 0 .. 2 .. 6 -> ce.create low (low + 1)|]
    let source = msce.create modelId 9<channelCount> prefix [|Ce.toIndex (ce.create 7 8)|]
    let values = [|8; 7; 6; 5; 4; 3; 2; 1; 0|]
    Assert.Equal(0, (apply source.Prefix values).[8])
    let expected = apply (source.MakeSorterWithPrefix None).Ces values
    let actual = apply (source.MakeSorterWithoutPrefix None).Ces (apply source.Prefix values)
    Assert.Equal<int>(expected :> seq<int>, actual :> seq<int>)

[<Theory>]
[<InlineData("ce")>]
[<InlineData("si")>]
[<InlineData("rs")>]
[<InlineData("uf4")>]
[<InlineData("uf6")>]
let ``stage conversion and model sets include the prefix in full sorter form`` kind =
    let source = model kind (prefix ())
    let stages = RandomNetwork.toStagesWithPrefix source
    let flattened = stages.Stages |> Array.collect (fun stage -> stage.Ces)
    Assert.Equal<ce>(prefix (), flattened |> Array.take (prefix ()).Length)
    Assert.Equal((SimpleSorterModel.makeSorterWithPrefix None source).Ces.Length, flattened.Length)
    let models = sorterModelSet.create (UMX.tag (Guid.NewGuid())) [|sorterModel.Simple source|] 100<ceLength>
    let full = SorterModelSet.makeSorterSetWithPrefix (UMX.tag (Guid.NewGuid())) (Some 0<ceLength>) models
    let suffix = SorterModelSet.makeSorterSetWithoutPrefix (UMX.tag (Guid.NewGuid())) (Some 0<ceLength>) models
    Assert.Equal<ce>(prefix (), full.Sorters.[0].Ces)
    Assert.Empty(suffix.Sorters.[0].Ces)
