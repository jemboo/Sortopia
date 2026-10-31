module Sortopia.Sorting.Tests.History

open System
open Xunit
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs

let private sequence =
    [| [|ce.create 0 1; ce.create 2 3|]
       [|ce.create 0 2; ce.create 1 3|]
       [|ce.create 1 2|] |]
    |> Array.map (fun ces -> stage.create ces 4<channelCount>)
    |> fun stages -> stageSequence.create stages 4<channelCount>

[<Fact>]
let comparator_history_includes_input_and_counts_only_actual_swaps () =
    let sortable = sortableIntArray.create([|0; 2; 1|], 3<channelCount>, 7<symbolSetSize>)
    let ces = [|ce.create 1 2; ce.create 0 1; ce.create 0 1|]
    let uses = [|4; 5; 6|]
    let history = sortable.SortByCesWithHistory ces uses
    Assert.Equal(4, history.Length)
    Assert.True(history.[0].Values = [|0; 2; 1|])
    for snapshot in history.[1..] do
        Assert.True(snapshot.Values = [|0; 1; 2|])
    Assert.True((uses = [|5; 5; 6|]))
    Assert.True(sortable.Values = [|0; 2; 1|])
    Assert.All(history, fun snapshot -> Assert.Equal(7<symbolSetSize>, snapshot.SymbolSetSize))

[<Fact>]
let stage_history_matches_comparator_boundaries_for_every_four_channel_binary_input () =
    let stages = sequence.Stages
    let ces = stages |> Array.collect (fun stage -> stage.Ces)
    for bits in 0..15 do
        let input = Array.init 4 (fun i -> (bits >>> i) &&& 1)
        let sortable = sortableIntArray.create(input, 4<channelCount>, 2<symbolSetSize>)
        let ceUses, stageUses = Array.zeroCreate ces.Length, Array.zeroCreate ces.Length
        let comparatorHistory = sortable.SortByCesWithHistory ces ceUses
        let stageHistory = sortable.SortByStagesWithHistory sequence stageUses
        Assert.Equal(stages.Length, stageHistory.Length)
        let mutable boundary = 0
        for index in 0..stages.Length - 1 do
            boundary <- boundary + stages.[index].Ces.Length
            Assert.Equal<sortableIntArray>(comparatorHistory.[boundary], stageHistory.[index])
        Assert.True((ceUses = stageUses))
        Assert.True(stageHistory.[stageHistory.Length - 1].IsSorted)
        Assert.True(sortable.Values = input)

[<Fact>]
let generic_histories_support_bool_values_and_have_independent_storage () =
    let input = [|true; false; true; false|]
    let ces = sequence.Stages |> Array.collect (fun stage -> stage.Ces)
    let history = CeSorting.sortByWithHistoryAndUses ces (Array.zeroCreate ces.Length) input
    let stageHistory = CeSorting.sortByStagesWithHistoryAndUses sequence (Array.zeroCreate ces.Length) input
    Assert.True(history.[ces.Length] = [|false; false; true; true|])
    input.[0] <- false
    Assert.True(history.[0].[0])
    history.[0].[1] <- true
    Assert.False(history.[1].[0])
    stageHistory.[0].[0] <- true
    Assert.False(stageHistory.[1].[0])

[<Fact>]
let empty_and_noop_stages_have_explicit_history_lengths () =
    let input = [|1; 1; 1; 1|]
    let empty = stageSequence.create [||] 4<channelCount>
    Assert.Empty(CeSorting.sortByStagesWithHistoryAndUses empty [||] input)
    let initial = CeSorting.sortByWithHistoryAndUses [||] [||] input
    Assert.Single(initial) |> ignore
    Assert.False(obj.ReferenceEquals(input, initial.[0]))
    let stages = stageSequence.create [|stage.create [||] 4<channelCount>; sequence.Stages.[0]|] 4<channelCount>
    let uses = [|0; 0|]
    let results = CeSorting.sortByStagesWithHistoryAndUses stages uses input
    Assert.Equal(2, results.Length)
    Assert.True(results.[0] = input && results.[1] = input)
    Assert.False(obj.ReferenceEquals(results.[0], results.[1]))
    Assert.True((uses = [|0; 0|]))

[<Fact>]
let invalid_history_arguments_do_not_partially_update_counters () =
    let uses = [|10; 20|]
    let input = [|3; 2; 1; 0|]
    Assert.Throws<ArgumentException>(fun () ->
        CeSorting.sortByWithHistoryAndUses [|ce.create 0 1; ce.create 2 4|] uses input |> ignore) |> ignore
    Assert.True((uses = [|10; 20|]))
    Assert.True((input = [|3; 2; 1; 0|]))
    Assert.Throws<ArgumentException>(fun () -> CeSorting.sortByWithHistoryAndUses [|ce.create 0 1|] uses input |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> CeSorting.sortByStagesWithHistoryAndUses sequence uses input |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> CeSorting.sortByStagesWithHistoryAndUses sequence [||] [|1|] |> ignore) |> ignore
    Assert.Throws<ArgumentNullException>(fun () -> CeSorting.sortByWithHistoryAndUses null uses input |> ignore) |> ignore
    Assert.Throws<ArgumentNullException>(fun () -> CeSorting.sortByWithHistoryAndUses [||] null input |> ignore) |> ignore
    Assert.Throws<ArgumentNullException>(fun () -> CeSorting.sortByWithHistoryAndUses [||] [||] (null: int[]) |> ignore) |> ignore
    Assert.Throws<ArgumentNullException>(fun () -> CeSorting.sortByStagesWithHistoryAndUses Unchecked.defaultof<stageSequence> [||] input |> ignore) |> ignore
