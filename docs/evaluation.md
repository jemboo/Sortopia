# Evaluation contracts and migration notes

`Sortopia.Evaluation` replaces the compiled library code in `GeneSort.SortingOps`. It references Sorting and Reporting, with no model or catalog dependency. `Sortopia.Evaluation.Serialization` replaces `GeneSort.SortingOps.Mp`. Both are .NET 10 libraries. The demo console entry points, commented experiments, unused unsafe/CE-fetch scalar variants and placeholder serialization test remain in GeneSort.

## Evaluate networks

```fsharp
open FSharp.UMX
open Sortopia.Evaluation

let block = CeBlock.fromSorter network
let result = CeBlockOps.evalWithSorterTest inputs ceBlock.Empty block (UMX.tag false)
let swapsPerCe = result.UseCountArray
let failingInputs = result.UnsortedCount
```

`evalWithSorterTests` evaluates an array of networks against the same inputs. The six supported input representations are integer arrays, Boolean arrays, packed integers, byte SIMD with 32 or 64 lanes, and bit SIMD with 512 binary lanes. SIMD paths retain parallel block evaluation. Empty network batches return an empty array.

The declared input width applies even to empty datasets. Each network and any supplied positive-width prefix must match it. A one-channel sorter is valid and has zero failures.

Every CE usage count is the number of actual input swaps, including duplicate inputs. SIMD counts are per valid lane, not per activated vector block. Inactive lanes do not contribute even if their backing vector contains nonzero data. Integer count storage remains Int32; this migration does not introduce streaming or Int64-scale datasets.

The collection flag preserves the legacy distinction:

| Mode | `UnsortedCount` | Residual inputs |
| --- | --- | --- |
| `false` | Number of failing inputs, including duplicates | Not collected |
| `true` | Number of distinct failing outputs | Deduplicated failures |

Collection preserves scalar/packed representation; SIMD collectors return scalar residual inputs. An empty SIMD residual set is represented by `None`; scalar and packed collectors can return `Some` containing an empty dataset. V3 creation falls back to V2 when no residual dataset exists. Do not infer whether testing occurred from the presence of residual inputs, or compare the two counting modes as if they were the same statistic.

The optional prefix describes CEs **already applied** to the supplied inputs, for example by Models.Inputs. Evaluation applies only the suffix block. Prefix CEs remain in result metrics and use `-1` as an unknown usage sentinel. They are never removed based on suffix counts. Last-used indices include the prefix offset; an entirely empty result uses `-1` in all versions.

`CeBlockOpsBitv512.evalCounts` counts all failures and per-CE swaps for one sorter, supports cancellation, and skips tracking the first failing lane. Sampling and Evolution use this counts-only path for SIMD evaluation and likewise skip counterexamples in their scalar evaluator. Prefixed exhaustive runs use the reduced `msasPfx` datasets; prefixed sampled runs collapse duplicate presorted draws. Statistics count each distinct prefix output once, without weighting it by original-input multiplicity. Prefix usage is unmeasured because only the suffix is evaluated.

`CeBlockOpsBitv512.evalWithCounterexampleIndex` remains available when a counterexample is needed. It returns the first failing input's zero-based position in dataset order, including when preceding SIMD blocks are shorter than 512 lanes. Callers that retain original inputs can use this to recover an original counterexample after presorting. `evalWithCounterexample` returns the failing value from the supplied dataset.

## Ownership and validation

Blocks snapshot CE arrays. Evaluation results snapshot mutable usage counters and return copies of exposed arrays. V2/V3 results also snapshot their CE usage arrays. Mutable `ceUseCounts` supports atomic accumulation; finish writers before reading snapshots, equality or hashes, and do not use a changing counter container as a dictionary key.

Serial and atomic increments throw `OverflowException` if the new CE count exceeds `Int32.MaxValue`. The failed increment leaves the stored count intact.

Constructors reject invalid endpoints, negative counts, mismatched dimensions, malformed CE usage shapes and unordered or duplicate original CE indices. Block splitting requires a positive chunk length; suffix offsets must lie within the network. The zero-width empty block remains available as a prefix sentinel. Set evaluations reject duplicate sorter IDs instead of silently discarding entries. Weighted scoring requires finite weights and retains invariant-culture compact strings.

V1 contains summary metrics. V2 includes used CEs with their original indices and counts. V3 adds residual inputs. Merging requires the same sorter ID and channel count and rejects V1 because its missing CE detail prevents recomputing metrics. Counts are summed across partitions; matching prefix sentinels stay `-1`. Conflicting CEs or prefix status at an original index are rejected. V3 residual sets are concatenated by the existing dataset merge; merged counts are sums of partition counts, not a fresh global deduplication. Callers must merge evaluations using the same collection mode and nonoverlapping partitions.

`SorterEval.getIsShortEnough n` accepts a successful sorter if its last used CE lies within the first `n` positions of the original network. A successful sorter with zero used CEs fits a cutoff of zero.

## Stable sequence fingerprint

Persisted `SequenceHash` no longer calls `GetHashCode()`. Version 1 writes little-endian Int32 values for the format version (`1`), channel count, stage count, then each stage's CE count followed by its ordered `Ce.toIndex` values. The fingerprint is the first four SHA-256 bytes interpreted as a little-endian signed Int32. Stage construction uses StageBuilderSequence as before. This is a reproducible fingerprint, not a collision-free identity, and is intentionally different from legacy process-local hashes.

## Serialization

Use DTO `fromDomain`/`toDomain` and `MessagePackSetup.serialize`/`deserialize` with explicit shared options. Records have numeric keys in declaration order; F# union case order is part of the wire contract. No global serializer settings are changed.

`ceDataDto` stores key 0 `CeIndex: int`, key 1 `UseCount: int`, and key 2 `CeInt: int`. This replaces the legacy UInt16 index and Half usage count, preserving large indices and exact counts. `-1` remains the prefix sentinel. Negative indices and usage below `-1` are rejected. A missing `UnsortedCount` is rejected rather than being treated as a successful sort. Domain validation runs when decoding blocks and results.

These are new evaluation wire contracts, not a reader for legacy SortingOps.Mp files. Existing persisted evaluation data needs explicit conversion at the future FileDb migration boundary. No old payload is silently reinterpreted.

## Verification

The consolidated Evaluation.Tests project retains all six substantive legacy tests. Added regression tests cover an independent scalar oracle across SIMD boundaries and partial blocks, dirty padding lanes, usage snapshots, prefix indices, merging, exact large DTO values, invalid inputs, all result versions, sets and culture-independent scoring round trips. Run the full solution tests as described in the README.
