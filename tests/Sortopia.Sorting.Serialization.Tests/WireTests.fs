module Sortopia.Sorting.Serialization.Tests.WireTests

open System
open FSharp.UMX
open MessagePack
open Xunit
open Sortopia.Sorting.Tests.Samples
open Sortopia.Core.Serialization
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Serialization.Networks
open Sortopia.Sorting.Serialization.Inputs
open Sortopia.Sorting.Tests.Samples

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
let all_six_formats_round_trip_identity_values_and_partial_blocks count =
    for input in datasets count do
        let dto = SortableTestsDto.fromDomain input
        let bytes = MessagePackSetup.serialize dto
        let restored = bytes |> MessagePackSetup.deserialize<sortableTestsDto> |> SortableTestsDto.toDomain
        Assert.Equal(SortableTests.getId input, SortableTests.getId restored)
        Assert.Equal(SortableTests.getChannelCount input, SortableTests.getChannelCount restored)
        Assert.Equal(count, int (SortableTests.getSortableCount restored))
        Assert.True((values input = values restored))
        Assert.True((bytes = (restored |> SortableTestsDto.fromDomain |> MessagePackSetup.serialize)))

[<Fact>]
let sorter_and_empty_sorter_set_round_trip () =
    let id = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00") |> UMX.tag<sorterId>
    let network = sorter.create id width [|ce.create 0 1; ce.create 2 3; ce.create 1 2|]
    let restored = network |> SorterDto.fromDomain |> MessagePackSetup.serialize |> MessagePackSetup.deserialize<SorterDto> |> SorterDto.toDomain
    Assert.Equal(network, restored)
    for networks, setId in [[||], Guid.Empty; [||], Guid.NewGuid(); [|network|], Guid.NewGuid()] do
        let set = sorterSet.create (UMX.tag setId) networks
        let copy = set |> SorterSetDto.fromDomain |> MessagePackSetup.serialize |> MessagePackSetup.deserialize<sorterSetDto> |> SorterSetDto.toDomain
        Assert.Equal(set, copy)

[<Fact>]
let prefixes_round_trip_with_each_representation () =
    let prefix = [|ce.create 0 1; ce.create 2 3|]
    for input in datasets 33 do
        let restored, ces =
            (input, prefix) |> SortableTestsWithPrefixDto.fromDomain |> MessagePackSetup.serialize
            |> MessagePackSetup.deserialize<sortableTestsWithPrefixDto> |> SortableTestsWithPrefixDto.toDomain
        Assert.True((prefix = ces))
        Assert.True((values input = values restored))

[<Fact>]
let transient_dataset_ids_are_preserved () =
    let id = Guid.Empty |> UMX.tag<sortableTestsId>
    let cases =
        [| sortableIntTests.create id width [||] |> Ints
           sortableBinaryTests.create id width [||] |> Bools
           packedSortableIntTests.createWithId id width [||] |> PackedInts
           sortableUint8v256Tests.create id width [||] |> Uint8v256
           sortableUint8v512Tests.create id width [||] |> Uint8v512
           sortableBitv512Tests.create id width [||] |> Bitv512 |]
    for input in cases do
        let restored = input |> SortableTestsDto.fromDomain |> MessagePackSetup.serialize
                       |> MessagePackSetup.deserialize<sortableTestsDto> |> SortableTestsDto.toDomain
        Assert.Equal(id, SortableTests.getId restored)

[<Fact>]
let explicit_keys_and_format_tags_have_fixed_wire_bytes () =
    let comparatorBytes = Convert.FromHexString("9103")
    let comparator = comparatorBytes |> MessagePackSetup.deserialize<ceDto> |> CeDto.toDomain
    Assert.Equal(ce.create 0 2, comparator)
    Assert.True((comparatorBytes = (comparator |> CeDto.fromDomain |> MessagePackSetup.serialize)))
    // [version=1, format="Ints", payload=bin(90)] pins the envelope independently of F# case order.
    let envelopeBytes = Convert.FromHexString("9301A4496E7473C40190")
    let envelope = { Version = 1; Format = "Ints"; Payload = [|0x90uy|] }
    Assert.True((envelopeBytes = MessagePackSetup.serialize envelope))
    Assert.Equal(envelope, MessagePackSetup.deserialize<sortableTestsDto> envelopeBytes)

[<Fact>]
let unknown_versions_and_formats_are_rejected () =
    let dto = SortableTestsDto.fromDomain (datasets 1).[0]
    for invalid in [{dto with Version = 2}; {dto with Format = "future"}; {dto with Format = null}; {dto with Payload = null}] do
        Assert.ThrowsAny<ArgumentException>(fun () -> SortableTestsDto.toDomain invalid |> ignore) |> ignore
    Assert.ThrowsAny<ArgumentException>(fun () -> SortableTestsDto.toDomain Unchecked.defaultof<_> |> ignore) |> ignore

[<Fact>]
let truncated_payload_is_rejected () =
    let dto = SortableTestsDto.fromDomain (datasets 1).[0]
    Assert.Throws<MessagePackSerializationException>(fun () -> SortableTestsDto.toDomain {dto with Payload = dto.Payload.[0..2]} |> ignore) |> ignore

[<Theory>]
[<InlineData(-1)>]
[<InlineData(33)>]
let invalid_256_lane_counts_are_rejected count =
    let dto : sortableUint8v256TestsDto =
        { Id = %datasetId; ChannelCount = 1; Blocks = [| {RawVectors = [|Array.zeroCreate 32|]; SortableCount = count} |] }
    Assert.Throws<ArgumentException>(fun () -> SortableUint8v256TestsDto.toDomain dto |> ignore) |> ignore

[<Theory>]
[<InlineData(0)>]
[<InlineData(31)>]
[<InlineData(33)>]
let simd_vectors_require_exact_byte_lengths length =
    let dto : sortableUint8v256TestsDto =
        { Id = %datasetId; ChannelCount = 1; Blocks = [| {RawVectors = [|Array.zeroCreate length|]; SortableCount = 1} |] }
    Assert.Throws<ArgumentException>(fun () -> SortableUint8v256TestsDto.toDomain dto |> ignore) |> ignore

[<Fact>]
let malformed_512_payloads_and_widths_are_rejected () =
    let byteDto : sortableUint8v512TestsDto =
        { Id = %datasetId; ChannelCount = 1; Blocks = [| {RawVectors = [|Array.zeroCreate 64|]; SortableCount = 1} |] }
    for length in [63;65] do
        let invalid = {byteDto with Blocks = [|{byteDto.Blocks.[0] with RawVectors = [|Array.zeroCreate length|]}|]}
        Assert.Throws<ArgumentException>(fun () -> SortableUint8v512TestsDto.toDomain invalid |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> SortableUint8v512TestsDto.toDomain {byteDto with ChannelCount = 2} |> ignore) |> ignore
    let bitDto : sortableBitv512TestsDto =
        { Id = %datasetId; ChannelCount = 1; Blocks = [| {RawVectors = [|Array.zeroCreate 8|]; SortableCount = 1} |] }
    for count in [-1;513] do
        let invalid = {bitDto with Blocks = [|{bitDto.Blocks.[0] with SortableCount = count}|]}
        Assert.Throws<ArgumentException>(fun () -> SortableBitv512TestsDto.toDomain invalid |> ignore) |> ignore
    let invalidBits = {bitDto with Blocks = [|{bitDto.Blocks.[0] with RawVectors = [|Array.zeroCreate 9|]}|]}
    Assert.Throws<ArgumentException>(fun () -> SortableBitv512TestsDto.toDomain invalidBits |> ignore) |> ignore

[<Fact>]
let invalid_scalar_packed_network_and_prefix_payloads_are_rejected () =
    let scalar : sortableIntTestsDto =
        { Id = %datasetId; ChannelCount = 4; SortableArrays = [|{Values = [|1|]; ChannelCount = 1; SymbolSetSize = 2}|] }
    Assert.Throws<ArgumentException>(fun () -> SortableIntTestsDto.toDomain scalar |> ignore) |> ignore
    let packed : packedSortableIntTestsDto = {Id = %datasetId; ChannelCount = 4; Values = [|1|]}
    Assert.Throws<ArgumentException>(fun () -> PackedSortableIntTestsDto.toDomain packed |> ignore) |> ignore
    let network : SorterDto = {SorterId = Guid.NewGuid(); Width = 4; Ces = [|CeDto.fromDomain (ce.create 0 4)|]}
    Assert.Throws<ArgumentException>(fun () -> SorterDto.toDomain network |> ignore) |> ignore
    let prefix = {Tests = SortableTestsDto.fromDomain (datasets 1).[0]; Prefix = network.Ces}
    Assert.Throws<ArgumentException>(fun () -> SortableTestsWithPrefixDto.toDomain prefix |> ignore) |> ignore

[<Fact>]
let serialization_does_not_change_global_options () =
    let defaults = MessagePackSerializer.DefaultOptions
    for input in datasets 1 do
        input |> SortableTestsDto.fromDomain |> MessagePackSetup.serialize
        |> MessagePackSetup.deserialize<sortableTestsDto> |> SortableTestsDto.toDomain |> ignore
    Assert.Same(defaults, MessagePackSerializer.DefaultOptions)
