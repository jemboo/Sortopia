# Sorting libraries

`Sortopia.Sorting` is a library for network structure and input datasets. It references only Core and Permutations. It has no serializer, reporting, file-storage or UI dependencies. `Sortopia.Sorting.Serialization` owns the MessagePack DTOs and uses Core.Serialization's explicit per-call options.

## Organization and scope

| Location | Responsibility |
| --- | --- |
| `SortingTypes.fs` | Shared measures and configuration tokens used by the Dispatch dependency chain. |
| `Networks` namespace and folder | Comparators (`ce`), identified networks (`sorter`), network sets, canonical stages, packing, and comparator-use records. Mutable packing builders are internal. |
| `Inputs` namespace and folder | Boolean and integer rows, packed integer datasets, generation, dataset unions and partitions. The historical `sortableTests` name means a dataset of sorting inputs, not a unit-test fixture. |
| `Inputs/Simd` folder | The three column-oriented layouts and their dataset wrappers. These retain the `Inputs` namespace. |
| Serialization `Networks` and `Inputs` namespaces | Explicit wire records and `fromDomain` / `toDomain` conversions. |

The migration follows all 17 production projects reachable from `GeneSort.Dispatch.V1`, including model generation, evaluation, project handling and storage. The original GeneSort solution is unchanged. `GeneSort.SortingOps`, model and evaluation projects are consumers for later migrations. `GeneSort.SortingLib` has migrated separately into [Sortopia.Sorting.Catalog](sorting-catalog.md).

The retained domain was also compiled under its old namespace in a temporary copy of the Dispatch dependency tree. This checks the retained API against its consumers without installing compatibility code into Sortopia. The new serializer is tested directly in Sortopia; that compile check does not claim compatibility with old files.

Omitted code includes the console entry points, `BitonicSorter`, old reflection-counting implementation, unused string parsers and report projection, sorter concatenation and convenience factories, alternate movement/displacement algorithms, scalar distance helpers, unused binary/lattice generators, incomplete unsorted-count helper, partition merge/rotation helpers, and all SIMD lane/golden hashing. The two retained stage-packing entry points are `toStageSequence` and `toStageSequenceWithMoveInfo`.

## Values and buffers

Ordinary equality includes every identifying field. A sorter compares ID, width and ordered comparators consistently through F# equality, boxed equality and the .NET default comparer. A stage sorts its disjoint comparators into canonical order; stage width participates in equality. Stage sequence order matters. Runtime structural hashing replaces handwritten hash formulas and mutable hash caches.

The integer input value comparer deliberately projects only the row values for evaluation deduplication. It uses framework hashing and exact structural equality, independent of symbol-set metadata. Hash codes are collection implementation details and must not be used as persistent identities or proof of equality. When migrating Eval, replace any persisted `sequenceHash` usage that assumes a collision-free or stable identity.

Factories snapshot caller-owned arrays. Array-valued properties return snapshots, including dataset rows, SIMD vectors, stages and sorter sets. Read individual scalar values with `Item` and comparators with `Ce`; obtain a vector-array snapshot once per block when entering an evaluation loop. Sorting operations work on those buffers. The public `CeSorting.sortBy` primitive explicitly sorts its supplied buffer in place.

Network factories check comparator bounds. Stages require disjoint channels, allowing a self-comparator to occupy one channel. Datasets validate widths. Dataset IDs are preserved verbatim, including Guid.Empty for transient datasets used by Dispatch. Sorter sets also permit the transient empty ID; individual network IDs must be nonempty. Empty scalar and SIMD datasets may have width zero; packed datasets require a positive width because their count is derived from the flattened length. An empty sorter set is valid and serializable.

## Input representations

| Dataset case | Layout | Cases per block |
| --- | --- | --- |
| `Bools` | One Boolean array per input | No block limit |
| `Ints` | One integer array per input | No block limit |
| `PackedInts` | Consecutive rows in one integer array | No block limit |
| `Uint8v256` | One `Vector256<byte>` per channel | 32 |
| `Uint8v512` | One `Vector512<byte>` per channel | 64 |
| `Bitv512` | One `Vector512<uint64>` per channel; each bit identifies an input | 512 |

SIMD blocks record their valid case count, including partial and empty blocks. Conversions and serialization preserve that count. Byte packing rejects values outside 0–255 and uses zero padding. Expanding a byte block produces integer rows with symbol-set size 256; the block does not store the original scalar metadata. Binary blocks expand with symbol-set size 2.

Binary enumeration with 64-bit counters accepts widths 0–63. Eager integer-array enumeration accepts 0–30 and remains limited by available memory. These checks prevent integer or shift overflow; they do not make exhaustive enumeration of large widths practical. Partitioning shuffles individual cases, preserving duplicate occurrences and balancing partition sizes across all six layouts.

The `BitVector256` configuration token remains because the current Dispatch model layer explicitly diagnoses it as unsupported. There is no corresponding input dataset or serializer format.

## Execution history

Comparator and stage histories are now available in `CeSorting` and on `sortableIntArray`. Comparator history includes the initial input; stage history has exactly one result after each stage. Both preserve the input, provide independent snapshot storage and accumulate actual swap counts. See [history contracts and playback](network-viewer.md).

## Serialization contract

All records use explicit numeric MessagePack keys. The following field order is the wire contract; changing an F# record or module name does not change it.

| DTO | Keys in order, starting at 0 |
| --- | --- |
| `ceDto` | `Index` (nonnegative triangular comparator code) |
| `SorterDto` | `SorterId`, `Width`, `Ces` |
| `sorterSetDto` | `SorterSetId`, `Sorters` |
| `sortableBoolArrayDto` | `Values`, `ChannelCount` |
| `sortableIntArrayDto` | `Values`, `ChannelCount`, `SymbolSetSize` |
| Scalar dataset DTOs | `Id`, `ChannelCount`, `SortableArrays` |
| `packedSortableIntTestsDto` | `Id`, `ChannelCount`, `Values` |
| SIMD dataset DTOs | `Id`, `ChannelCount`, `Blocks` |
| SIMD block DTOs | `RawVectors`, `SortableCount` |
| `sortableTestsDto` | `Version`, `Format`, `Payload` |
| `sortableTestsWithPrefixDto` | `Tests`, `Prefix` |

Dataset envelopes use version `1`. The stable format strings are `Ints`, `Bools`, `PackedInts`, `Uint8v256`, `Uint8v512` and `Bitv512`. `Payload` is a MessagePack binary value containing the corresponding dataset DTO encoded with the same explicit options. This keeps persisted tags independent of F# union case order. Unknown versions and formats are rejected.

Byte vectors require exactly 32 or 64 bytes. Bit vectors require exactly eight unsigned 64-bit elements. Readers validate vector lengths, valid case counts, dataset widths, packed lengths, comparator bounds and prefix bounds. `MessagePackSecurity.UntrustedData` is supplied per call. No migration code changes `MessagePackSerializer.DefaultOptions`.

```fsharp
open Sortopia.Core.Serialization
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Serialization.Inputs

let inputs = packedSortableIntTests.createFromPackedValues 4<channelCount> [|3; 1; 2; 0|] |> PackedInts
let bytes = inputs |> SortableTestsDto.fromDomain |> MessagePackSetup.serialize
let restored = bytes |> MessagePackSetup.deserialize<sortableTestsDto> |> SortableTestsDto.toDomain
```

This is a new sorting wire contract. Legacy sorting DTO readers and aliases are not included. The two sorter conversion functions are now named `SorterDto.fromDomain` and `SorterDto.toDomain`. Update storage boundaries when their owning projects migrate.

## Verification

The tests retain useful GeneSort comparator, prefix, input and stage cases and add equality-contract, buffer-ownership and validation checks. Packing is compared with scalar network execution over every four-channel binary input. Dataset conversion and serialization run at 0, 1, 31, 32, 33, 63, 64, 65, 511, 512 and 513 cases. Further checks cover all-format partitioning, exhaustive small binary spaces, full Int32 comparator-code boundaries, fixed wire bytes, malformed payloads and unchanged global serializer options.

Run `dotnet build Sortopia.sln -c Release`, then `dotnet test --solution Sortopia.sln -c Release --no-build` from the solution directory.
