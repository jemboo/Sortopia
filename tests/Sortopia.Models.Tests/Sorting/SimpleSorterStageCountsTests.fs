module Sortopia.Models.Tests.SimpleSorterStageCountsTests

open Xunit
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.SimpleSorterStageCounts
open Sortopia.Sorting

[<Fact>]
let ``full sort and half merge use independent measurements`` () =
    Assert.Equal(Some 388<stageLength>, tryFullSort99 Msuf6 24<channelCount>)
    Assert.True(tryFullSort99 Msuf6 48<channelCount> |> Option.isNone)
    let merge = halfMergeDefault Msuf6 48<channelCount>
    Assert.Equal(984, int merge.Stages)
    Assert.False(merge.IsEstimate)
    Assert.Equal(Some 46606<stageLength>, tryHalfMerge99 Msuf6 384<channelCount>)

[<Fact>]
let ``unmeasured widths use the adjacent measurements for the selected method and task`` () =
    let between = halfMergeDefault Msrs 40<channelCount>
    Assert.True(between.IsEstimate)
    Assert.Equal(483, int between.Stages)
    let above = fullSortDefault Msuf6 48<channelCount>
    Assert.True(above.IsEstimate)
    Assert.Equal(1006, int above.Stages)
    let below = fullSortDefault Msce 2<channelCount>
    Assert.Equal(1, int below.Stages)
