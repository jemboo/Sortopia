# Sorting catalog

`Sortopia.Sorting.Catalog` replaces the retained portion of `GeneSort.SortingLib`. It is an F# library referencing Core and Sorting. All public types live in `Sortopia.Sorting.Catalog`. Catalog data belongs here because it selects specific networks; general network operations stay in `Sortopia.Sorting.Networks`.

## Contents and use

The library preserves all 49 complete networks, covering widths 2–32, and all 10 prefixes from GeneSort. Dispatch's editor enumerates these catalogs dynamically, so the migration includes every entry. The catalog also includes Batcher odd–even mergesort (variant `Batcher`) and bitonic sort (variant `BatcherBitonic`) at 8, 16 and 32 channels, for 55 networks total. There are 4,702 comparators in the complete networks and 385 in the prefixes. The embedded tables preserve the original comparator order and stage boundaries.

| File | Responsibility |
| --- | --- |
| `Identifiers.fs` | Validated immutable keys, explicit variant names, sorter/prefix text codecs and stable identity encoding. |
| `SorterLib.fs`, `PrefixLib.fs` | Embedded catalog data, `ids()` enumeration and optional text lookup. |
| `MergeLib.fs` | Available merge-prefix choices and their identifier text codec. |
| `NetworkText.fs` | Internal parser for the complete bracketed-stage grammar. |
| `SorterDataParse.fs` | Parsing entry points and typed comparator lookups. |

```fsharp
open Sortopia.Sorting
open Sortopia.Sorting.Catalog

let key = sorterLibId.create 16<channelCount> VariantA
let comparators = SorterDataParse.getCeArrayFromSorterLib key
let text = SorterLibId.toString key
let restored = SorterLibId.fromString text

let mergeKey = mergeLibId.create 32<channelCount> 4<mergeDimension> VariantA
let stages = SorterDataParse.getCeArrayFromMergeLib mergeKey
```

The last lookup stacks four copies of the eight-channel network. It sorts each contiguous group of eight independently, preparing input segments for a subsequent merge. It does not merge the four segments. A dimension of one returns the component network. Every returned comparator array, stage array and identifier array is owned by the caller.

Keys require positive channel counts. Prefix stage counts and merge dimensions must also be positive, and a merge dimension must divide the total width. Key validity is independent of catalog availability: unsupported widths or variants can be represented and round-tripped, and lookups return `None`. `availableForChannelCount` lists only available constructions, ordered by dimension then variant; nonpositive widths return no choices.

## Text and identity contracts

The canonical text forms are:

```text
SorterLibId(channelCount=24, Variant=VariantB)
PrefixLibId(channelCount=24, stageLength=4, Variant=PrefixB)
MergeLibId(channelCount=24, mergeDimension=3, Variant=VariantB)
```

Numbers use invariant culture. Readers accept surrounding whitespace and whitespace after commas, require the complete labeled form, and validate the resulting key. Unknown names or malformed syntax raise `FormatException`; nonpositive or indivisible dimensions raise `ArgumentException`; null identifiers raise `ArgumentNullException`; integer overflow raises `OverflowException`. The explicit variant names are persisted tokens, independent of F# case numbering or display formatting.

`IStableSerializable` writes a BinaryWriter string type tag, ordered little-endian Int32 fields, then the explicit variant string. The tags are `Sortopia.Sorting.Catalog.SorterLibId/v1`, `Sortopia.Sorting.Catalog.PrefixLibId/v1` and `Sortopia.Sorting.Catalog.MergeLibId/v1`. The integer fields are respectively width; width and stage count; width and merge dimension. Use Core's `GuidUtils.guidFromObjs` for SHA-256-based identities. These tagged encodings deliberately produce new persistent IDs; legacy ID compatibility is not provided. Ordinary dictionary equality and hashing use the F# structural implementation.

These text codecs are the catalog's persistence boundary; it does not require a separate MessagePack project. A containing model can persist the canonical identifier string in its own DTO. Network text is a sequence of stages such as `[(0,1),(2,3)] [(1,2)]`. Parsing consumes the entire input, rejects negative, equal or reversed endpoints and stray characters, and preserves stage order. Empty text has no stages, and `[]` is an empty stage. Parsing alone does not establish stage disjointness, channel bounds or sorting correctness; the embedded catalogs are checked by tests.

## Migration and verification

Update the project reference and replace `open GeneSort.SortingLib.Sorter` with `open Sortopia.Sorting.Catalog`. The console demo, unused two-dimensional sorter lookup and unvalidated `MergeLibId.create` wrapper were omitted. Construct merge keys with the validated `mergeLibId.create`. There are no legacy aliases, new package dependencies or custom collection hash functions.

Catalog tests cover complete inventory and stage invariants, exact identifier round-trips, fixed persistent-ID fixtures, malformed input, unavailable keys, buffer ownership, prefix truncation and every available merge construction at representative widths. Networks up to 16 channels are verified against every binary input. Larger networks are checked against boundary patterns and 4,096 seeded samples per network; these sampled checks are not an exhaustive proof.

Run `dotnet test --project tests/Sortopia.Sorting.Catalog.Tests/Sortopia.Sorting.Catalog.Tests.fsproj -c Release` from the solution directory. The migrated source is also compiled under the original namespace in a temporary Dispatch dependency tree to check its consumers without adding compatibility code to Sortopia.

Batcher variants have 6, 10 and 15 stages at widths 8, 16 and 32 respectively. Odd–even networks have 19, 63 and 191 CEs; bitonic networks have 24, 80 and 240 CEs. Bitonic networks use ascending comparisons throughout, with mirrored pairs at each merge boundary.
