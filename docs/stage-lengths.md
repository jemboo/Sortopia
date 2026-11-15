# Provisional sorter stage lengths

`SimpleSorterStageLengths` in `src/Sortopia.Models.Sorting/Simple/SimpleSorterStageLengths.fs` provides:

```fsharp
stageLength50 (cc: int<channelCount>) (prefixStageLength: int<stageLength>) (smt: simpleSorterModelType) : int<stageLength>
stageLength90 (cc: int<channelCount>) (prefixStageLength: int<stageLength>) (smt: simpleSorterModelType) : int<stageLength>
stageLength99 (cc: int<channelCount>) (prefixStageLength: int<stageLength>) (smt: simpleSorterModelType) : int<stageLength>
```

The percentage targets the fraction of generated networks that sort **every input**. It is not the fraction of inputs sorted by one network. The return value is the **additional random suffix budget**, excluding the supplied prefix. These are provisional starting sizes, not calibrated guarantees. They assume uniform Simple generation, enabled entries, and exclusion of self-comparisons for Msce. Different weights, RNGs, activation settings, or prefix contents can change the success rate.

For Msce, a stage is a generation budget of `floor(channelCount * stageLength / 2)` comparators. It is not the packed depth of the generated CE sequence. Other methods use their native generator stages.

```fsharp
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Sorting

let suffix = SimpleSorterStageLengths.stageLength90 24<channelCount> 3<stageLength> simpleSorterModelType.Msuf6
// 329 suffix stages: reference 332 minus a provisional three-stage prefix credit.
let info = SimpleSorterStageLengths.recommend 90 24<channelCount> 3<stageLength> simpleSorterModelType.Msuf6
// info.Stages, info.IsEstimate, info.IsSupported, info.Source
```

## Evidence and consolidation

The full-sort and half-merge tables come from GeneSort commit `79fbce09fe6875c5a4ab1e2bf75712aa94f4f4b1`, `GeneSort.Project/Params/StageLength.fs`. All three requested percentile rows are retained. Its probability-named implementation functions returned identical record-sorter lengths and were placeholders; those bodies are not used. The current GeneSort checkout's `GeneSort.Project.V1/Catalogs/Evolution/SeedSorterModelGeneration.fs` has a separate coarse seed-size table without probability or prefix calibration, so it is not treated as measured percentile evidence.

Sortopia previously copied only the 99% rows into `SimpleSorterStageCounts.fs`. That module now delegates to this single implementation, including the existing full-sort and half-merge helpers. No second copy of the tables remains. Random-network and SGD UI defaults still request an unprefixed 99% budget and are labeled as references or estimates. Workspace model-generation setup and results functions instead accept the typed `SorterSuccessFraction` parameter (50%, 90%, or 99%, default 99%) and derive their suffix budget from channel count, prefix depth, and the algorithm in the selected function. The same helper supplies the live workspace suggestion. Their version-2 contracts and query projections use this target instead of `StageLength`.

The historical tables are unverified against current generators. A fresh four-channel check of 10,000 deterministic networks per supported model, with all 16 binary inputs, produced the replacement reference rows below. In particular, the old Msce median of two stages meant four CEs and was inconsistent with full sorting. No claim is made that the legacy rows at other widths have been validated by this check.

| Model | 50% | 90% | 99% |
| --- | ---: | ---: | ---: |
| Msce | 7 | 12 | 18 |
| Mssi | 5 | 9 | 15 |
| Msrs | 5 | 9 | 14 |
| Msuf4 | 5 | 9 | 15 |

Run `dotnet build tests/Sortopia.Models.Tests -c Release` and then `dotnet fsi tools/CheckStageLengths.fsx` to reproduce the four-channel calibration. This uses SplitMix64 and explicit model IDs to keep the random sequence independent of the tested budget. The model tests separately check the recommended budgets against a deterministic holdout sample of 2,000 networks per model and target. Sampling and integer rounding introduce uncertainty, especially at 99%.

## Provisional completion policy

- Every combination of channels `4..24`, `32`, `48`, `64`, prefixes `0..5`, and the five model types has a numeric planning value.
- Within a model's reference widths, interpolate linearly and round upward. Beyond the largest full-sort reference, extend the final log-log slope, at least linearly. This allows unfolding estimates to grow faster than a straight-line extension. Below the smallest reference, use the first linear segment and clamp to one. Higher-percentile budgets are bounded below by lower-percentile budgets so independently extrapolated curves cannot reverse their order.
- For larger unprefixed full sorts, the separate half-merge curve supplies a lower bound where its reference widths begin. Sorting every input cannot be easier than sorting only inputs whose halves are already sorted. This does not turn a half-merge measurement into a full-sort measurement.
- There are no calibrated prefix-depth rows. Provisionally subtract one suffix stage per prefix stage, with a minimum of one. Prefix depth alone cannot describe a prefix's actual benefit, and the helper cannot detect a prefix that already sorts everything. No exponential speedup is assumed.
- `IsEstimate = false` means the unadjusted result came directly from a reference row; it does **not** certify the probability for current generators. `Source` distinguishes the current four-channel sample from historical references and estimated values.
- Inspect `IsSupported` before constructing a generator. Msce and Mssi support all requested widths; Msrs requires even widths; Msuf4 requires powers of two of at least four; Msuf6 requires six times a power of two. Numeric values for unsupported pairs are extrapolations for planning, not newly supported generator sizes.

Inputs below two channels, prefix lengths outside `0..5`, percentages other than 50/90/99, and estimates exceeding `Int32` are rejected. The estimator is deterministic and does no sampling at runtime. Extend the reference data with reproducible current-generator measurements as they become available, especially at 16–64 channels and for actual prefix variants.
