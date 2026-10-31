module Sortopia.Sorting.Tests.Invariants

open System
open System.Collections.Generic
open System.Runtime.Intrinsics
open FSharp.UMX
open Xunit
open Sortopia.Sorting.Tests.Samples
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Tests.Samples

[<Fact>]
let sorter_equality_is_consistent_and_handles_different_lengths () =
    let id = Guid.NewGuid() |> UMX.tag<sorterId>
    let a = sorter.create id width [|ce.create 0 1|]
    let b = sorter.create id width [|ce.create 0 1|]
    let shorter = sorter.create id width [||]
    let differentId = newSorter width a.Ces
    for other, expected in [b, true; shorter, false; differentId, false] do
        Assert.Equal(expected, (a = other))
        Assert.Equal(expected, a.Equals(box other))
        Assert.Equal(expected, EqualityComparer<sorter>.Default.Equals(a, other))
    Assert.Equal(a.GetHashCode(), b.GetHashCode())

[<Fact>]
let immutable_networks_stages_and_sets_own_their_arrays () =
    let source = [|ce.create 0 1|]
    let network = newSorter width source
    let stg = stage.create source width
    let stages = [|stg|]
    let sequence = stageSequence.create stages width
    let networks = [|network|]
    let set = newSorterSet networks
    source.[0] <- ce.create 2 3
    network.Ces.[0] <- source.[0]
    stg.Ces.[0] <- source.[0]
    stages.[0] <- stage.create source width
    sequence.Stages.[0] <- stages.[0]
    networks.[0] <- newSorter width source
    set.Sorters.[0] <- networks.[0]
    Assert.Equal(ce.create 0 1, network.Ce 0)
    Assert.Equal(ce.create 0 1, sequence.Stages.[0].Ces.[0])
    Assert.Equal(network, set.Sorters.[0])

[<Fact>]
let stage_equality_canonicalizes_order_and_includes_width () =
    let ces = [|ce.create 0 1; ce.create 2 3|]
    let a = stage.create ces width
    let b = stage.create (Array.rev ces) width
    Assert.Equal(a, b)
    Assert.Equal(a.GetHashCode(), b.GetHashCode())
    Assert.NotEqual(a, stage.create ces 5<channelCount>)
    Assert.Throws<ArgumentException>(fun () -> stage.create [|ce.create 0 1; ce.create 1 2|] width |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> newSorter width [|ce.create 0 4|] |> ignore) |> ignore

[<Fact>]
let packed_stages_preserve_network_results_for_every_binary_input () =
    let ces = [|ce.create 2 3; ce.create 0 1; ce.create 1 2; ce.create 0 1; ce.create 2 3|]
    let stages, moves = StageBuilderSequence.toStageSequenceWithMoveInfo width ces
    let packed = stages.Stages |> Array.collect (fun s -> s.Ces)
    Assert.Equal(ces.Length, packed.Length)
    Assert.True((Array.forall2 (fun (s: stage) (m: int[]) -> s.Ces.Length = m.Length) stages.Stages moves))
    for input in rows 16 do
        Assert.True((CeSorting.sortBy ces (Array.copy input) = CeSorting.sortBy packed (Array.copy input)))

[<Theory>]
[<InlineData(0)>]
[<InlineData(1)>]
[<InlineData(31)>]
[<InlineData(32)>]
[<InlineData(33)>]
[<InlineData(63)>]
[<InlineData(64)>]
[<InlineData(65)>]
[<InlineData(511)>]
[<InlineData(512)>]
[<InlineData(513)>]
let every_representation_preserves_rows_and_partial_blocks count =
    for tests in datasets count do
        Assert.Equal(count, int (SortableTests.getSortableCount tests))
        Assert.True((rows count = values tests))

[<Fact>]
let partitioning_preserves_every_occurrence_in_all_representations () =
    for tests in datasets 67 do
        let random = Random(123)
        let partition = PartitionOfSortableTests.partition random.Next 5<sortableTestsCount> tests
        let parts = partition.SortableTests |> Map.toArray |> Array.map snd
        let lengths = parts |> Array.map (SortableTests.getSortableCount >> int)
        Assert.Equal(5, parts.Length)
        Assert.True((Array.max lengths - Array.min lengths <= 1))
        Assert.True(((parts |> Array.collect values |> Array.sort) = (values tests |> Array.sort)))

[<Fact>]
let partitioning_rejects_invalid_counts_picker_and_mixed_widths () =
    let sample = (datasets 3).[0]
    for count in [0; -1; 4] do
        Assert.Throws<ArgumentException>(fun () -> PartitionOfSortableTests.partition (fun _ -> 0) (UMX.tag count) sample |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> PartitionOfSortableTests.partition id 2<sortableTestsCount> sample |> ignore) |> ignore
    let emptyOther = sortableIntTests.create datasetId 2<channelCount> [||] |> Ints
    let mixed = Map.ofList [UMX.tag<sortableTestsPartitionId> "a", sample; UMX.tag<sortableTestsPartitionId> "b", emptyOther]
    Assert.Throws<ArgumentException>(fun () -> partitionOfSortableTests.create mixed |> ignore) |> ignore

[<Theory>]
[<InlineData(0)>]
[<InlineData(1)>]
[<InlineData(8)>]
[<InlineData(9)>]
[<InlineData(10)>]
let bit_generation_covers_each_binary_input_once (widthValue: int) =
    let inputs =
        GrayVectorGenerator.getAllSortBlockBitv512ForChannelCount (UMX.tag widthValue)
        |> Seq.collect SortBlockBitv512.toSortableBoolArrays
        |> Seq.map (fun a -> a.Values) |> Seq.toArray
    Assert.Equal(1 <<< widthValue, inputs.Length)
    Assert.Equal(inputs.Length, inputs |> Array.distinct |> Array.length)

[<Fact>]
let generation_rejects_shift_overflow () =
    for widthValue in [-1; 64] do
        Assert.Throws<ArgumentException>(fun () -> GrayVectorGenerator.getAllSortBlockBitv512ForChannelCount (UMX.tag widthValue) |> Seq.head |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> SortableBoolArray.getAllSortableBoolArrays (UMX.tag widthValue) |> Seq.head |> ignore) |> ignore

[<Fact>]
let random_comparators_include_every_channel_and_self_pair () =
    let mutable bound = 0
    let last n = bound <- n; n - 1
    let lastCe = Ce.generateCeCodes last 4 |> Seq.head |> Ce.fromIndex
    Assert.Equal(10, bound)
    Assert.Equal(ce.create 3 3, lastCe)

[<Theory>]
[<InlineData(0)>]
[<InlineData(1)>]
[<InlineData(1073741824)>]
[<InlineData(2147483647)>]
let comparator_codes_round_trip_across_the_full_int_range (code: int) =
    Assert.Equal(code, code |> Ce.fromIndex |> Ce.toIndex)

[<Fact>]
let simd_and_packed_factories_reject_invalid_shapes_and_ranges () =
    for count in [-1;33] do
        Assert.Throws<ArgumentException>(fun () -> sortBlockUint8v256.createFromVectors [||] count |> ignore) |> ignore
    for count in [-1;65] do
        Assert.Throws<ArgumentException>(fun () -> sortBlockUint8v512.createFromVectors [||] count |> ignore) |> ignore
    for count in [-1;513] do
        Assert.Throws<ArgumentException>(fun () -> sortBlockBitv512.createFromVectors [||] count |> ignore) |> ignore
    for value in [-1;256] do
        let a = sortableIntArray.create([|value|], 1<channelCount>, 257<symbolSetSize>)
        Assert.Throws<ArgumentException>(fun () -> sortBlockUint8v256.createFromIntArrays 1<channelCount> [|a|] |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> sortBlockUint8v512.createFromIntArrays 1<channelCount> [|a|] |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> packedSortableIntTests.createFromPackedValues 0<channelCount> [||] |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> packedSortableIntTests.createFromPackedValues width [|1|] |> ignore) |> ignore

[<Fact>]
let datasets_and_simd_blocks_snapshot_input_and_output_arrays () =
    let original = Vector256.Create(7uy)
    let vectors = [|original|]
    let block = sortBlockUint8v256.createFromVectors vectors 1
    let blocks = [|block|]
    let tests = sortableUint8v256Tests.create datasetId 1<channelCount> blocks
    vectors.[0] <- Vector256<byte>.Zero
    block.Vectors.[0] <- Vector256<byte>.Zero
    blocks.[0] <- sortBlockUint8v256.createFromVectors [|Vector256<byte>.Zero|] 1
    tests.SimdSortBlocks.[0] <- blocks.[0]
    Assert.Equal(7uy, tests.SimdSortBlocks.[0].Vectors.[0].GetElement(0))
    let ints = [|sortableIntArray.create([|1;0;1;0|], width, 2<symbolSetSize>)|]
    let scalar = sortableIntTests.create datasetId width ints
    let packed = packedSortableIntTests.create width ints
    ints.[0] <- sortableIntArray.create([|0;0;0;0|], width, 2<symbolSetSize>)
    scalar.SortableIntArrays.[0] <- ints.[0]
    packed.PackedValues.[0] <- 0
    Assert.Equal(1, scalar.SortableIntArrays.[0].[0])
    Assert.Equal(1, packed.PackedValues.[0])

[<Fact>]
let input_value_comparer_deduplicates_values_independently_of_metadata () =
    let a = sortableIntArray.create([|1;0|], 2<channelCount>, 2<symbolSetSize>)
    let b = sortableIntArray.create([|1;0|], 2<channelCount>, 256<symbolSetSize>)
    let comparer = SortableIntArray.SortableIntArrayValueComparer() :> IEqualityComparer<sortableIntArray>
    Assert.True((comparer.Equals(a,b)))
    Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b))
    Assert.Single(SortableIntArray.removeDuplicates [|a;b|]) |> ignore
