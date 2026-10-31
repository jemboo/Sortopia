module Sortopia.Sorting.Tests.StagePacking

open Xunit
open Sortopia.Sorting
open Sortopia.Sorting.Networks

[<Fact>]
let independent_comparators_pack_into_one_stage () =
    let seq = StageBuilderSequence.toStageSequence 4<channelCount> [|ce.create 0 1; ce.create 2 3|]
    Assert.Equal(1, int seq.StageLength)
    Assert.Equal(2, seq.Stages.[0].Ces.Length)

[<Fact>]
let dependencies_keep_comparators_in_order () =
    let seq = StageBuilderSequence.toStageSequence 4<channelCount> [|ce.create 0 1; ce.create 0 2|]
    Assert.Equal(2, int seq.StageLength)
    Assert.Equal(ce.create 0 1, seq.Stages.[0].Ces.[0])
    Assert.Equal(ce.create 0 2, seq.Stages.[1].Ces.[0])

[<Fact>]
let empty_network_has_no_stages () =
    Assert.Equal(0, int (StageBuilderSequence.toStageSequence 4<channelCount> [||]).StageLength)

[<Fact>]
let later_independent_comparators_move_to_the_first_stage () =
    let seq, moves = StageBuilderSequence.toStageSequenceWithMoveInfo 6<channelCount> [|ce.create 0 1; ce.create 2 3; ce.create 0 2; ce.create 4 5|]
    Assert.Equal(2, int seq.StageLength)
    Assert.Equal(3, seq.Stages.[0].Ces.Length)
    Assert.Equal(1, moves.[0].[2])
