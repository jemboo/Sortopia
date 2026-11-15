module Sortopia.Models.Tests.SimpleSorterStageCountsTests

open Xunit
open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.SimpleSorterStageCounts
open Sortopia.Models.Sorting.Simple
open Sortopia.Sorting

[<Fact>]
let ``full sort and half merge use independent measurements`` () =
    Assert.Equal(Some 388<stageLength>, tryFullSort99 simpleSorterModelType.Msuf6 24<channelCount>)
    Assert.True(tryFullSort99 simpleSorterModelType.Msuf6 48<channelCount> |> Option.isNone)
    let merge = halfMergeDefault simpleSorterModelType.Msuf6 48<channelCount>
    Assert.Equal(984, int merge.Stages)
    Assert.False(merge.IsEstimate)
    Assert.Equal(Some 46606<stageLength>, tryHalfMerge99 simpleSorterModelType.Msuf6 384<channelCount>)

[<Fact>]
let ``unmeasured widths use the adjacent measurements for the selected method and task`` () =
    let between = halfMergeDefault simpleSorterModelType.Msrs 40<channelCount>
    Assert.True(between.IsEstimate)
    Assert.Equal(483, int between.Stages)
    let above = fullSortDefault simpleSorterModelType.Msuf6 48<channelCount>
    Assert.True(above.IsEstimate)
    Assert.Equal(1906, int above.Stages)
    let below = fullSortDefault simpleSorterModelType.Msce 2<channelCount>
    Assert.True(below.Stages >= 1<stageLength>)
    Assert.True(below.IsEstimate)

[<Fact>]
let ``requested width prefix and model matrix has positive ordered provisional budgets`` () =
    let widths = [4..24] @ [32; 48; 64; 128; 384]
    let models = [simpleSorterModelType.Msce; simpleSorterModelType.Mssi; simpleSorterModelType.Msrs; simpleSorterModelType.Msuf4; simpleSorterModelType.Msuf6]
    for width in widths do
        let cc = width * 1<channelCount>
        for model in models do
            for prefix in [0..5] do
                let p = prefix * 1<stageLength>
                let q50 = SimpleSorterStageLengths.stageLength50 cc p model
                let q90 = SimpleSorterStageLengths.stageLength90 cc p model
                let q99 = SimpleSorterStageLengths.stageLength99 cc p model
                Assert.True(q50 >= 1<stageLength> && q50 <= q90 && q90 <= q99, $"{model}, {width}, prefix {prefix}: {q50}, {q90}, {q99}")
                let info = SimpleSorterStageLengths.recommend 99 cc p model
                Assert.Equal(SimpleSorterModelType.supportsChannelCount width model, info.IsSupported)
                Assert.Equal(q99, info.Stages)
                if prefix > 0 then
                    Assert.True(info.IsEstimate)
                    Assert.Contains("prefix", info.Source)
                    Assert.True(q99 <= SimpleSorterStageLengths.stageLength99 cc (p - 1<stageLength>) model)

[<Fact>]
let ``direct references interpolation and prefix credit have distinct provenance`` () =
    Assert.Equal(231<stageLength>, SimpleSorterStageLengths.stageLength50 24<channelCount> 0<stageLength> simpleSorterModelType.Msuf6)
    Assert.Equal(332<stageLength>, SimpleSorterStageLengths.stageLength90 24<channelCount> 0<stageLength> simpleSorterModelType.Msuf6)
    let direct = SimpleSorterStageLengths.recommend 99 24<channelCount> 0<stageLength> simpleSorterModelType.Msuf6
    Assert.False(direct.IsEstimate)
    Assert.Contains("legacy", direct.Source)
    let prefix = SimpleSorterStageLengths.recommend 99 24<channelCount> 5<stageLength> simpleSorterModelType.Msuf6
    Assert.Equal(383<stageLength>, prefix.Stages)
    Assert.True(prefix.IsEstimate)
    let middle = SimpleSorterStageLengths.recommend 90 10<channelCount> 0<stageLength> simpleSorterModelType.Mssi
    Assert.Equal(48<stageLength>, middle.Stages)
    Assert.Contains("interpolated", middle.Source)
    let invalidWidth = SimpleSorterStageLengths.recommend 50 5<channelCount> 0<stageLength> simpleSorterModelType.Msuf4
    Assert.False(invalidWidth.IsSupported)
    Assert.True(invalidWidth.IsEstimate)
    Assert.Contains("current", (SimpleSorterStageLengths.recommend 99 4<channelCount> 0<stageLength> simpleSorterModelType.Msce).Source)

[<Fact>]
let ``wide full sort estimates never claim a smaller unprefixed budget than the half merge reference`` () =
    for model, widths in [simpleSorterModelType.Msce, [32;48;64]; simpleSorterModelType.Mssi, [32;48;64]
                          simpleSorterModelType.Msrs, [32;48;64]; simpleSorterModelType.Msuf4, [32;64]; simpleSorterModelType.Msuf6, [48]] do
        for width in widths do
            let cc = width * 1<channelCount>
            let full = SimpleSorterStageLengths.recommend 99 cc 0<stageLength> model
            Assert.True(full.IsEstimate)
            Assert.True(full.Stages >= (halfMergeDefault model cc).Stages)
            Assert.Equal(full.Stages, (fullSortDefault model cc).Stages)

[<Theory>]
[<InlineData(1, 0, 99)>]
[<InlineData(8, -1, 99)>]
[<InlineData(8, 6, 99)>]
[<InlineData(8, 0, 75)>]
[<InlineData(Int32.MaxValue, 0, 99)>]
let ``invalid estimate requests report argument errors`` cc prefix percent =
    Assert.Throws<ArgumentException>(fun () ->
        SimpleSorterStageLengths.recommend percent (cc * 1<channelCount>) (prefix * 1<stageLength>) simpleSorterModelType.Msuf4 |> ignore) |> ignore

[<Fact>]
let ``four channel recommendations are checked against every input on independent current samples`` () =
    // A deterministic holdout sample, separate from the 10,000 calibration IDs.
    let count = 2000
    let successFraction kind budget =
        let generator = SimpleSorterModelGen.makeUniform rngFactory.SmxFactory 4<channelCount> [||] budget kind (UMX.tag true)
        let succeeds index =
            let id = Guid(index, 77s, 66s, [|9uy;8uy;7uy;6uy;5uy;4uy;3uy;2uy|]) |> UMX.tag<sorterModelId>
            let model = SimpleSorterModelGen.makeSorterModelFromId id generator
            let rows = [|0xAAAAUL; 0xCCCCUL; 0xF0F0UL; 0xFF00UL|]
            for stage in (RandomNetwork.toStagesWithoutPrefix model).Stages do
                for ce in stage.Ces do
                    let lo, hi = int ce.LowChannel, int ce.HighChannel
                    let a, b = rows[lo], rows[hi]
                    rows[lo] <- a &&& b
                    rows[hi] <- a ||| b
            [0..2] |> List.forall (fun i -> rows[i] &&& ~~~rows[i+1] = 0UL)
        [0..count-1] |> List.filter succeeds |> List.length |> float |> fun successes -> successes / float count
    for model in [simpleSorterModelType.Msce; simpleSorterModelType.Mssi; simpleSorterModelType.Msrs; simpleSorterModelType.Msuf4] do
        for percent in [50;90;99] do
            let stages = (SimpleSorterStageLengths.recommend percent 4<channelCount> 0<stageLength> model).Stages
            let actual = successFraction model stages
            Assert.True(actual >= float percent / 100.0 - 0.04, $"{model} at {stages}: requested {percent} percent, observed {actual:P1}")
            let previous = successFraction model (stages - 1<stageLength>)
            Assert.True(previous < float percent / 100.0 + 0.04, $"{model} at {stages - 1<stageLength>}: threshold already exceeded ({previous:P1})")
