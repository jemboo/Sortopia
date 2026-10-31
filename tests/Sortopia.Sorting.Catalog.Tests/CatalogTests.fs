module Sortopia.Sorting.Catalog.Tests.Catalogs

open System
open System.Collections.Generic
open FSharp.UMX
open Xunit
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Catalog

let private apply (comparators: ce[]) (input: int[]) =
    let result = Array.copy input
    for comparator in comparators do
        let low, high = int comparator.LowChannel, int comparator.HighChannel
        if result.[low] > result.[high] then
            let temp = result.[low]
            result.[low] <- result.[high]
            result.[high] <- temp
    result

let private validateStages width (stages: ce[][]) =
    Assert.NotEmpty stages
    for stage in stages do
        Assert.NotEmpty stage
        let used = HashSet<int>()
        for comparator in stage do
            let low, high = int comparator.LowChannel, int comparator.HighChannel
            Assert.InRange(low, 0, width - 1)
            Assert.InRange(high, low + 1, width - 1)
            Assert.True(used.Add low && used.Add high, "Stage comparators must use disjoint channels.")

[<Fact>]
let complete_catalog_inventory_and_stage_invariants_are_preserved () =
    let sorters, prefixes = SorterLib.ids (), PrefixLib.ids ()
    Assert.Equal(55, sorters.Length)
    Assert.Equal(10, prefixes.Length)
    Assert.True(sorters |> Array.map (fun id -> int id.ChannelCount) |> Array.distinct = [|2..32|])
    let mutable sorterCes, sorterStages, prefixCes, prefixStages = 0, 0, 0, 0
    for id in sorters do
        let stages = SorterLib.tryGet id |> Option.get |> SorterDataParse.parseTo2dCeArray
        validateStages (int id.ChannelCount) stages
        sorterCes <- sorterCes + (stages |> Array.sumBy Array.length)
        sorterStages <- sorterStages + stages.Length
        Assert.True(Array.concat stages = (SorterDataParse.getCeArrayFromSorterLib id |> Option.get))
    for id in prefixes do
        let stages = PrefixLib.tryGet id |> Option.get |> SorterDataParse.parseTo2dCeArray
        validateStages (int id.ChannelCount) stages
        Assert.Equal(int id.StageLength, stages.Length)
        prefixCes <- prefixCes + (stages |> Array.sumBy Array.length)
        prefixStages <- prefixStages + stages.Length
        Assert.True(Array.concat stages = (SorterDataParse.getCeArrayFromPrefixLib id |> Option.get))
    Assert.Equal((4702, 580, 385, 32), (sorterCes, sorterStages, prefixCes, prefixStages))

[<Fact>]
let networks_up_to_sixteen_channels_sort_every_binary_input () =
    for id in SorterLib.ids () |> Array.filter (fun id -> int id.ChannelCount <= 16) do
        let width = int id.ChannelCount
        let ces = SorterDataParse.getCeArrayFromSorterLib id |> Option.get
        for bits in 0 .. (1 <<< width) - 1 do
            let input = Array.init width (fun i -> (bits >>> i) &&& 1)
            let actual = apply ces input
            Assert.True((actual = Array.sort input), $"{SorterLibId.toString id}: binary input {bits}")

[<Fact>]
let larger_networks_sort_boundary_patterns_and_seeded_samples () =
    let random = Random(8675309)
    for id in SorterLib.ids () |> Array.filter (fun id -> int id.ChannelCount > 16) do
        let width = int id.ChannelCount
        let ces = SorterDataParse.getCeArrayFromSorterLib id |> Option.get
        let check input = Assert.True(apply ces input = Array.sort input, SorterLibId.toString id)
        check [|0 .. width - 1|]
        check [|width - 1 .. -1 .. 0|]
        check (Array.create width Int32.MinValue)
        check (Array.init width (fun i -> if i % 2 = 0 then Int32.MaxValue else Int32.MinValue))
        for boundary in 0 .. width do
            check (Array.init width (fun i -> if i < boundary then 1 else 0))
        for channel in 0 .. width - 1 do
            check (Array.init width (fun i -> if i = channel then 1 else 0))
            check (Array.init width (fun i -> if i = channel then 0 else 1))
        for sample in 1 .. 4096 do
            check (Array.init width (fun _ -> if sample % 2 = 0 then random.Next(2) else random.Next(-width, width + 1)))

[<Fact>]
let shorter_prefixes_preserve_the_first_stages_of_their_longer_variant () =
    let ids = PrefixLib.ids ()
    for shorter in ids do
        for longer in ids do
            if shorter.ChannelCount = longer.ChannelCount && shorter.PrefixLibVariant = longer.PrefixLibVariant
               && shorter.StageLength < longer.StageLength then
                let read id = PrefixLib.tryGet id |> Option.get |> SorterDataParse.parseTo2dCeArray
                Assert.True(read shorter = (read longer |> Array.take (int shorter.StageLength)))

[<Fact>]
let merge_choices_are_complete_and_sort_each_contiguous_segment () =
    for width in [2; 24; 28; 32; 48; 64; 768] do
        let choices = MergeLibId.availableForChannelCount (UMX.tag width)
        let expected = SorterLib.ids () |> Array.filter (fun id -> width % int id.ChannelCount = 0)
        Assert.Equal(expected.Length, choices.Length)
        Assert.True((choices = (choices |> Array.sortBy (fun id -> id.MergeDimension, id.SorterLibVariant))))
        for key in choices do
            let factor = int key.FactorChannelCount
            let sorterKey = sorterLibId.create key.FactorChannelCount key.SorterLibVariant
            Assert.Contains(sorterKey, expected)
            let source = SorterLib.tryGet sorterKey |> Option.get |> SorterDataParse.parseTo2dCeArray
            let merged = SorterDataParse.getCeArrayFromMergeLib key |> Option.get
            validateStages width merged
            Assert.Equal(source.Length, merged.Length)
            for index in 0 .. source.Length - 1 do
                let expectedStage =
                    [| for segment in 0 .. int key.MergeDimension - 1 do
                        for comparator in source.[index] do
                            yield ce.create (int comparator.LowChannel + segment * factor) (int comparator.HighChannel + segment * factor) |]
                Assert.True((expectedStage = merged.[index]))
            let input = [|width .. -1 .. 1|]
            let expectedOutput = input |> Array.chunkBySize factor |> Array.collect Array.sort
            Assert.True((expectedOutput = apply (Array.concat merged) input))
    for width in [-1; 0; 1; 37] do
        Assert.Empty(MergeLibId.availableForChannelCount (UMX.tag width))

[<Fact>]
let valid_unavailable_keys_return_none () =
    Assert.True(SorterDataParse.getCeArrayFromSorterLib (sorterLibId.create 100<channelCount> VariantA) |> Option.isNone)
    Assert.True(SorterDataParse.getCeArrayFromPrefixLib (prefixLibId.create 24<channelCount> 3<stageLength> PrefixC) |> Option.isNone)
    Assert.True(SorterDataParse.getCeArrayFromMergeLib (mergeLibId.create 768<channelCount> 2<mergeDimension> VariantA) |> Option.isNone)

[<Fact>]
let lookups_and_id_enumerations_do_not_share_mutable_arrays () =
    let sorterId = sorterLibId.create 8<channelCount> VariantA
    let prefixId = prefixLibId.create 8<channelCount> 3<stageLength> PrefixA
    for read in [ (fun () -> SorterDataParse.getCeArrayFromSorterLib sorterId |> Option.get)
                  (fun () -> SorterDataParse.getCeArrayFromPrefixLib prefixId |> Option.get) ] do
        let original = read ()
        let first = original.[0]
        original.[0] <- ce.create 0 0
        Assert.Equal(first, (read ()).[0])
    for dimension in [1; 2] do
        let key = mergeLibId.create 16<channelCount> (UMX.tag dimension) VariantA
        let read () = SorterDataParse.getCeArrayFromMergeLib key |> Option.get
        let original, expected = read (), read ()
        original.[0].[0] <- ce.create 0 0
        original.[1] <- [||]
        Assert.True((expected = read ()))
    let ids = SorterLib.ids ()
    ids.[0] <- sorterLibId.create 999<channelCount> VariantA
    Assert.DoesNotContain(ids.[0], SorterLib.ids ())
    let prefixes = PrefixLib.ids ()
    prefixes.[0] <- prefixLibId.create 999<channelCount> 1<stageLength> PrefixA
    Assert.DoesNotContain(prefixes.[0], PrefixLib.ids ())

[<Fact>]
let network_parser_preserves_stage_and_comparator_order () =
    let actual = SorterDataParse.parseTo2dCeArray " \n [( 0 , 3 ), (1, 2)]\t[] [(0,1)] "
    let expected = [| [|ce.create 0 3; ce.create 1 2|]; [||]; [|ce.create 0 1|] |]
    Assert.True((expected = actual))
    Assert.True(Array.concat expected = SorterDataParse.parseToFlatCeArray "[(0,3),(1,2)][][(0,1)]")
    Assert.Empty(SorterDataParse.parseTo2dCeArray " \n ")
    Assert.Empty(SorterDataParse.parseToFlatCeArray "")
    Assert.Throws<ArgumentNullException>(fun () -> SorterDataParse.parseTo2dCeArray null |> ignore) |> ignore
    Assert.Throws<OverflowException>(fun () -> SorterDataParse.parseTo2dCeArray "[(0,2147483648)]" |> ignore) |> ignore

[<Theory>]
[<InlineData("garbage")>]
[<InlineData("(0,1)")>]
[<InlineData("[(0,1)]garbage")>]
[<InlineData("[(-1,2)]")>]
[<InlineData("[(0,1.5)]")>]
[<InlineData("[(0)]")>]
[<InlineData("[(0,1,2)]")>]
[<InlineData("[(0,1)")>]
[<InlineData("[(0,1),]")>]
[<InlineData("[(0,1)(2,3)]")>]
[<InlineData("[[(0,1)]]")>]
[<InlineData("[(1,0)]")>]
[<InlineData("[(1,1)]")>]
let network_parser_rejects_malformed_input_without_salvaging_digits text =
    Assert.Throws<FormatException>(fun () -> SorterDataParse.parseTo2dCeArray text |> ignore) |> ignore
    Assert.Throws<FormatException>(fun () -> SorterDataParse.parseToFlatCeArray text |> ignore) |> ignore

[<Theory>]
[<InlineData(8, 19, 24, 6)>]
[<InlineData(16, 63, 80, 10)>]
[<InlineData(32, 191, 240, 15)>]
let batcher_networks_have_expected_sizes (width: int) (oddEvenCount: int) (bitonicCount: int) (depth: int) =
    for variant, count in [Batcher, oddEvenCount; BatcherBitonic, bitonicCount] do
        let id = sorterLibId.create (UMX.tag width) variant
        let stages = SorterLib.tryGet id |> Option.get |> SorterDataParse.parseTo2dCeArray
        Assert.Equal(depth, stages.Length)
        Assert.Equal(count, stages |> Array.sumBy Array.length)
        validateStages width stages
