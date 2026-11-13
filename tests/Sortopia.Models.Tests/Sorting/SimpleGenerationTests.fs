module Sortopia.Models.Tests.SimpleGenerationTests

open System
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Models.Sorting.Simple.Si
open Sortopia.Models.Sorting.Simple.Rs
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.Models.Sorting.Simple.Uf6

let private prefix = [| ce.create 0 1; ce.create 2 3 |]
let private id = Guid.Parse "8248c51e-58f9-4d89-8cc8-78bfdcd2431e" |> UMX.tag<sorterModelId>
let private ops = OpsGenerationProbabilities.fromWeights
let private para4Seed = Seed6GenerationProbabilities.fromWeights 0. 0. 0. 0. 0. 1. 0.

[<Theory>]
[<InlineData(4, 0)>]
[<InlineData(8, 1)>]
[<InlineData(16, 2)>]
let ``UF4 initializer describes and accepts only its unfolding levels`` (width: int) levels =
    let widths = Msuf4RandGen.unfoldingWidths width
    Assert.Equal(levels, widths.Length)
    let generator = Msuf4RandGen.create rngFactory.SmxFactory (UMX.tag width) prefix 5<stageLength>
                        (ops 0. 0. 1.) (widths |> List.map (fun _ -> ops 1. 0. 0.))
    Assert.Equal(5, (generator.MakeSorterModelFromId id).TwoOrbitUnfolder4s.Length)

[<Theory>]
[<InlineData(6, 0)>]
[<InlineData(12, 1)>]
[<InlineData(24, 2)>]
let ``UF6 initializer describes and accepts only its unfolding levels`` (width: int) levels =
    let widths = Msuf6RandGen.unfoldingWidths width
    Assert.Equal(levels, widths.Length)
    let generator = Msuf6RandGen.create rngFactory.SmxFactory (UMX.tag width) prefix 5<stageLength>
                        para4Seed (widths |> List.map (fun _ -> ops 1. 0. 0.))
    Assert.Equal(5, (generator.MakeSorterModelFromId id).TwoOrbitUnfolder6s.Length)

[<Fact>]
let ``UF6 reuses the typed seed and unfolding configuration in every stage`` () =
    let gen = Msuf6RandGen.create rngFactory.SmxFactory 24<channelCount> prefix 5<stageLength> para4Seed [ops 0. 3. 0.; ops 7. 0. 0.]
    let model = gen.MakeSorterModelFromId id
    Assert.Equal(5, model.TwoOrbitUnfolder6s.Length)
    for stage in model.TwoOrbitUnfolder6s do
        Assert.Equal(twoOrbitTripleType.Para4, stage.Seed6TwoOrbitType)
        Assert.All(stage.TwoOrbitUnfolderSteps[0].TwoOrbitPairTypes, fun pair -> Assert.Equal(twoOrbitPairType.Para, pair))
        Assert.All(stage.TwoOrbitUnfolderSteps[1].TwoOrbitPairTypes, fun pair -> Assert.Equal(twoOrbitPairType.Ortho, pair))
    Assert.Equal<ce>(prefix, model.Prefix)
    Assert.Equal<ce>(prefix, (model.MakeSorterWithPrefix (Some 0<ceLength>)).Ces)

[<Fact>]
let ``UF4 and reflection symmetric generation honor zero weight exclusions`` () =
    let uf4 = Msuf4RandGen.create rngFactory.SmxFactory 16<channelCount> prefix 5<stageLength> (ops 0. 0. 1.) [ops 1. 0. 0.; ops 0. 1. 0.]
    for stage in (uf4.MakeSorterModelFromId id).TwoOrbitUnfolder4s do
        Assert.Equal(twoOrbitPairType.SelfRefl, stage.SeedTwoOrbitPairType)
        Assert.All(stage.TwoOrbitUnfolderSteps[0].TwoOrbitPairTypes, fun pair -> Assert.Equal(twoOrbitPairType.Ortho, pair))
        Assert.All(stage.TwoOrbitUnfolderSteps[1].TwoOrbitPairTypes, fun pair -> Assert.Equal(twoOrbitPairType.Para, pair))
    let rs = MsrsRandGen.create rngFactory.SmxFactory 8<channelCount> prefix 5<stageLength> (ops 0. 0. 1.)
    Assert.All((rs.MakeSorterModelFromId id).Perm_Rss, fun stage -> Assert.True(PermSi.isReflectionSymmetric stage.PermSi))

[<Theory>]
[<InlineData("Msce", 8)>]
[<InlineData("Mssi", 9)>]
[<InlineData("Msrs", 8)>]
[<InlineData("Msuf4", 16)>]
[<InlineData("Msuf6", 24)>]
let ``uniform adapter retains concrete generator identities and seeded output`` name (width: int) =
    let kind = SimpleSorterModelType.fromString name
    let channels = UMX.tag<channelCount> width
    for rng in [ rngFactory.SmxFactory; rngFactory.NetFactory; rngFactory.LcgFactory ] do
        let previous =
            match kind with
            | simpleSorterModelType.Msce -> msceRandGen.create rng channels prefix (UMX.tag true) (StageLength.toCeLength channels 5<stageLength>) |> SmmMsceRandGen
            | simpleSorterModelType.Mssi -> mssiRandGen.create rng channels prefix 5<stageLength> |> SmmMssiRandGen
            | simpleSorterModelType.Msrs -> msrsRandGen.create rng channels prefix (opsGenRates.createUniform()) 5<stageLength> |> SmmMsrsRandGen
            | simpleSorterModelType.Msuf4 -> msuf4RandGen.create rng channels prefix 5<stageLength> (Uf4GenRates.makeUniform width) |> SmmMsuf4RandGen
            | simpleSorterModelType.Msuf6 -> msuf6RandGen.create rng channels prefix 5<stageLength> (Uf6GenRates.makeUniform width) |> SmmMsuf6RandGen
        let current = SimpleSorterModelGen.makeUniform rng channels prefix 5<stageLength> kind (UMX.tag true)
        Assert.Equal(SimpleSorterModelGen.getId previous, SimpleSorterModelGen.getId current)
        let draw gen = SimpleSorterModelGen.makeSorterModelFromId id gen |> SimpleSorterModel.makeSorterWithPrefix None
        Assert.Equal(draw previous, draw current)

[<Fact>]
let ``relative weights normalize independently and handle large finite values`` () =
    let gen seed unfolding = Msuf4RandGen.create rngFactory.SmxFactory 8<channelCount> prefix 5<stageLength> seed [unfolding]
    let first = gen (ops 1. 2. 1.) (ops 0. 1. 3.)
    let scaled = gen (ops 2. 4. 2.) (ops 0. 8. 24.)
    Assert.Equal(first.Id, scaled.Id)
    Assert.NotEqual(first.Id, (gen (ops 1. 2. 1.) (ops 0. 3. 1.)).Id)
    let huge = ops Double.MaxValue Double.MaxValue 0.
    Assert.Equal(0.5, huge.OrthoRate); Assert.Equal(0.5, huge.ParaRate); Assert.Equal(0., huge.SelfReflRate)

[<Fact>]
let ``typed initializers reject invalid rates dimensions and unfolding counts`` () =
    for bad in [-1.; Double.NaN; Double.PositiveInfinity] do
        Assert.Throws<ArgumentException>(fun () -> ops bad 1. 1. |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> Seed6GenerationProbabilities.fromWeights 1. 1. 1. bad 1. 1. 1. |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> ops 0. 0. 0. |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Seed6GenerationProbabilities.fromWeights 0. 0. 0. 0. 0. 0. 0. |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Msuf4RandGen.create rngFactory.SmxFactory 16<channelCount> prefix 5<stageLength> (ops 1. 1. 1.) [ops 1. 1. 1.] |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Msuf6RandGen.create rngFactory.SmxFactory 12<channelCount> prefix 5<stageLength> para4Seed [] |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Msuf6RandGen.create rngFactory.SmxFactory 18<channelCount> prefix 5<stageLength> para4Seed [ops 1. 1. 1.] |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> MsrsRandGen.create rngFactory.SmxFactory 9<channelCount> prefix 5<stageLength> (ops 1. 1. 1.) |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> MsrsRandGen.create rngFactory.SmxFactory 8<channelCount> prefix 5<stageLength> Unchecked.defaultof<opsGenRates> |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Msuf6RandGen.create rngFactory.SmxFactory 12<channelCount> prefix 5<stageLength> Unchecked.defaultof<seed6GenRates> [ops 1. 1. 1.] |> ignore) |> ignore
    for stages in [0; Int32.MaxValue] do
        Assert.Throws<ArgumentException>(fun () -> MssiRandGen.create rngFactory.SmxFactory 8<channelCount> prefix (UMX.tag stages) |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> MsceRandGen.create rngFactory.SmxFactory 8<channelCount> prefix (UMX.tag stages) (UMX.tag true) |> ignore) |> ignore

[<Fact>]
let ``configured base generators also support activated models`` () =
    let baseGen = Msuf6RandGen.create rngFactory.SmxFactory 12<channelCount> prefix 5<stageLength> para4Seed [ops 1. 0. 0.] |> SmmMsuf6RandGen
    let r = (simpleSorterModelRGen.create baseGen 0).MakeSorterModelFromIndex (UMX.tag 0)
    let s = (simpleSorterModelSGen.create baseGen 0.0).MakeSorterModelFromIndex (UMX.tag 0)
    Assert.Equal<ce>(prefix, (r.MakeSorterWithPrefix None).Ces)
    Assert.Equal<ce>(prefix, (s.MakeSorterWithPrefix None).Ces)
