# Execution history and the network viewer

The Sorting networks page renders every entry exposed by `SorterLib.ids()` and `SorterLib.tryGet()`. `AllNetworksList` remains private catalog data. Choose **Sorting networks** in the home navigation; **Back to Sortopia** appears when its navigation group has hover or keyboard focus.

## Domain contracts

`Sortopia.Sorting.Networks.CeSorting` owns execution. It has no WPF, drawing, timer or animation dependencies.

| API | Result |
| --- | --- |
| `sortByWithHistoryAndUses ces counters values` | Generic array snapshots: the initial input, then one snapshot after each comparator. Length is `ces.Length + 1`. |
| `sortByStagesWithHistoryAndUses sequence counters values` | Generic array snapshots after each stage. Length is `sequence.StageLength`; the initial input is excluded. |
| `sortableIntArray.SortByCesWithHistory ces counters` | The comparator history as immutable sortables with the original width and symbol-set metadata. |
| `sortableIntArray.SortByStagesWithHistory sequence counters` | The stage history as immutable sortables with the same metadata. |

Both operations leave the input untouched and return independently owned arrays. `counters` must have exactly one slot per comparator and accumulates actual swaps, including when it starts with nonzero counts. Stage counters follow the concatenation of `sequence.Stages`' canonical comparator arrays. Equal values and self-comparators do not increment counters. Arguments and channel bounds are validated before counters are changed.

An empty comparator sequence returns a single initial snapshot. An empty stage sequence returns no snapshots. An empty stage produces one unchanged snapshot. Stage execution copies only at stage boundaries, without allocating all intermediate comparator snapshots.

```fsharp
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs

let stages =
    [| stage.create [|ce.create 0 1; ce.create 2 3|] 4<channelCount>
       stage.create [|ce.create 0 2; ce.create 1 3|] 4<channelCount>
       stage.create [|ce.create 1 2|] 4<channelCount> |]
let sequence = stageSequence.create stages 4<channelCount>
let ces = stages |> Array.collect (fun stage -> stage.Ces)
let input = sortableIntArray.create([|3; 2; 1; 0|], 4<channelCount>, 4<symbolSetSize>)
let comparatorHistory = input.SortByCesWithHistory ces (Array.zeroCreate ces.Length) // 6 snapshots
let stageHistory = input.SortByStagesWithHistory sequence (Array.zeroCreate ces.Length) // 3 snapshots
```

## Separation of execution and presentation

`CatalogNetwork` reads catalog entries and constructs validated domain stages. Original stage boundaries are preserved; comparators within a stage use the domain's canonical order. This affects only the order of independent comparisons inside a stage.

`NetworkTrace.Create` requests `SortByCesWithHistory` once for the selected input. Its read-only data contains values after every comparator, actual swap counts, stage boundaries and token channels. Token identities move according to recorded swaps, so repeated values remain distinguishable without comparing values again. Stage playback selects the corresponding comparator-history boundaries. Tests compare those frames with the independent stage-history API for every catalog entry.

`NetworksPageViewModel` selects inputs and networks, owns the playback clock, and exposes play/pause, reset, previous/next, seeking, speed and zoom. Only changing the network or input creates a new trace. Playback, seeking, mode changes and zoom reuse that trace. Changing the step mode returns to the initial frame. Playback pauses on navigation or view unloading, and the shell disposes its timer when closed.

`NetworkLayout` computes channel positions, stage bands and comparator columns. Overlapping vertical spans get distinct columns. `NetworkDiagram` draws those coordinates and interpolates between recorded token positions; it never executes comparisons, updates swap counts or mutates a sortable. Small arcs separate crossing tokens. Page code-behind handles view lifetime and optional horizontal following, leaving user-controlled vertical scrolling intact. Small windows can scroll the page to reach every control.

## Archean reference

The implementation considered `archean.controls/ViewModel/Sorter2/StageVmRender.cs`, `SorterVm.cs`, `StageVm.cs` and `SortableVm.cs`, plus `SorterControls/View/Common/StageLayout.cs` in the Archean source tree. It carries forward alternating stage bands, offset comparator spans and colored value tokens. Archean's `SortableVm.UpdateSortableVms` performs value comparisons while advancing presentation state. Sortopia instead reads the domain history and recorded swaps. No Archean assembly dependency or mutable rendering/sorting state machine was imported.

## Verification

Sorting tests cover comparator/stage boundaries, exhaustive four-channel binary inputs, metadata, accumulated counters, duplicate values, independent snapshots, empty stages and invalid arguments. WPF tests cover all 55 entries with reversed and repeated-value inputs, token identity, stage-history equivalence, layout overlap, immutable trace access, playback, seeking and navigation lifecycle. These are Windows tests because the viewer uses WPF.

Build and run all tests with `dotnet build Sortopia.sln -c Release` and `dotnet test --solution Sortopia.sln -c Release --no-build`. The page was also rendered offscreen at normal and small sizes, with WPF binding-error capture and live dispatcher-timer playback.
