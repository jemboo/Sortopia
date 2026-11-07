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

`NetworksPageViewModel` selects inputs and networks, owns the playback clock, and exposes play/pause, reset, previous/next, seeking, speed and zoom. **Play** uses the selected input, resumes after **Pause**, and replays the same input after completion. **New input** generates another input and rewinds. Only changing the network or input creates a new trace. Playback, seeking, mode changes and zoom reuse that trace. Changing the step mode returns to the initial frame. Playback pauses on navigation or view unloading, and the shell disposes its timer when closed.

`NetworkLayout` computes channel positions, stage bands and comparator columns. Overlapping vertical spans get distinct columns. The playhead stays at a shared column until its comparisons finish, so forward playback only pauses or moves right. `NetworkDiagram` draws those coordinates and interpolates between recorded token positions; it never executes comparisons, updates swap counts or mutates a sortable. For a swap, both animation schemes slide tokens along their original channels to the CE, pause, exchange positions, then continue. Each token retains its original value, color and upright label. Comparisons without a swap leave tokens on their channels. Stage playback animates the recorded comparisons in order within each stage.

`NetworkDiagram.AnimationScheme` selects a reusable scheme from `Rendering/AnimationSchemes`. `CeSliderScheme` is the default: elements move straight up and down the vertical CE axis, passing one another at its midpoint. `CeSpinnerScheme` preserves the half-turn around the CE's midpoint; select it with `diagram.AnimationScheme = CeSpinnerScheme.Instance`. Both derive from `CeAnimationScheme`, which owns the shared timing. Playback layouts reserve the horizontal space required by the selected scheme, independent of the input. Page code-behind handles fitting, view lifetime and optional horizontal following, leaving user-controlled vertical scrolling intact. Small windows can scroll the page to reach every control.

## Archean reference

The implementation considered `archean.controls/ViewModel/Sorter2/StageVmRender.cs`, `SorterVm.cs`, `StageVm.cs` and `SortableVm.cs`, plus `SorterControls/View/Common/StageLayout.cs` in the Archean source tree. It carries forward alternating stage bands, offset comparator spans and colored value tokens. Archean's `SortableVm.UpdateSortableVms` performs value comparisons while advancing presentation state. Sortopia instead reads the domain history and recorded swaps. No Archean assembly dependency or mutable rendering/sorting state machine was imported.

## Verification

Sorting tests cover comparator/stage boundaries, exhaustive four-channel binary inputs, metadata, accumulated counters, duplicate values, independent snapshots, empty stages and invalid arguments. WPF tests cover all 55 entries with reversed and repeated-value inputs, token identity, stage-history equivalence, layout overlap, immutable trace access, playback, seeking and navigation lifecycle. These are Windows tests because the viewer uses WPF.

Build and run all tests with `dotnet build Sortopia.sln -c Release` and `dotnet test --solution Sortopia.sln -c Release --no-build`. The page was also rendered offscreen at normal and small sizes, with WPF binding-error capture and live dispatcher-timer playback.

## Sampling and Evolution prefixes

The Sampling and Evolution pages offer **None** and the catalog prefixes available for the current channel count. Sampling keeps the selected prefix fixed while generating suffixes. Evolution inherits the selected sample's prefix; changing it while stopped retests the existing suffix as a new parent and resets the generation count. Stopping that retest keeps the previous parent and prefix.

Each run prepares its test set once and evaluates only the suffix for every candidate. With a prefix, inputs are reduced to distinct prefix outputs: exhaustive runs use the `msasPfx` scalar or SIMD factory, and sampled runs deduplicate presorted draws. For Prefix1, 8 channels require 81 test inputs, 9 require 162, and 20 require 59,049. Sampling shows this actual reduced count; Setup labels its original count as Inputs before prefix. Failure and suffix CE-use counts refer to the reduced set, with no multiplicity weighting. Compare these statistics within the same prefix and input context. The displayed networks, CE counts, stage counts, and sorter fingerprints include the prefix. Fixed-prefix CEs remain visible in used and compact views, and their tooltips identify their usage as unmeasured during suffix tests.

## Random networks

Choose **Random Networks** from the home page to generate and test a network without changing the catalog page. Select a method, channel count and CE count for `msce`, or stage count for the other methods, then choose **Generate**. The page displays the resulting CE and stage totals and retains its state when navigating back.

The channel selector supports 2–64 channels, filtered by the generator: `msrs` uses even widths starting at 4 (the model library minimum), `msuf4` uses 4, 8, 16, 32 or 64, and `msuf6` uses 12, 24 or 48. `mssi` also supports odd widths, with fixed points treated as idle channels. Generation accepts up to 1,000,000 CEs for msce or 10,000 stages for the stage generators. Networks above 4,096 CEs or 512 stages show and animate a bounded prefix ending at a whole-stage boundary. A marker and message at the right edge identify the cutoff and the displayed versus full totals. Testing and edits still use the entire network; playback stops at the cutoff and labels those values as intermediate. Removing unused CEs and compacting can bring the whole network into view; usage colors stay attached to the remaining CEs.

**Test** accepts `all` through 20 channels (up to 1,048,576 binary combinations), or an explicit count of 1–1,000,000 random samples at any width. Above 20 channels, `all` is disabled by validation and an existing `all` setting becomes 4,096 samples when a larger network is generated. Sampling uses reproducible independent binary draws with replacement; passing a sample is explicitly not a proof. Testing finishes the whole requested set even after failures, reports the total failure count, and loads the first counterexample into binary playback with white 0 balls and black 1 balls. The progress label reports inputs checked. Tests can be cancelled, and leaving the page cancels an active test. Shuffle, stepping, playback, zoom and follow remain available.

`RandomNetwork.generate` adapts the existing model generators to stage sequences. Its supplied GUID seeds reproducible generation; the page converts its nonnegative 32-bit seed into that GUID. The same seed and settings reproduce the same network. The refresh icon inside **Random seed** fills a fresh seed; **Generate** uses the displayed seed. The generated-network label records the seed used. SplitMix64 floating-point sampling now correctly uses 53 random bits scaled into [0, 1); this fixes formerly degenerate branch choices in msrs/msuf4/msuf6 and changes their previously generated sequences. Stage generators preserve their requested stage count. For `msce`, consecutive disjoint CEs share a stage while overlapping CEs begin another stage, preserving execution dependencies. The catalog and random pages share `NetworkPlaybackViewModel`; the catalog view and its controls retain their existing behavior. `NetworkCheck` runs independently of rendering on a background task.


The Random Networks diagram can color CEs by test usage: actual swaps divided by tested inputs. Gray means unused. Eleven additional colors use approximately logarithmic upper thresholds of 0.001%, 0.003%, 0.01%, 0.03%, 0.1%, 0.3%, 1%, 3%, 10%, 30% and 100%, making small usage differences visible in later stages. Hover over a CE for its index, channels, swap count and percentage. These colors take precedence over playback colors while enabled; turn off **Color CEs by test usage** to see ordinary playback highlights. New generation or a new test clears the previous usage results. Cancelled tests do not publish partial counts. The catalog page retains its existing playback colors.

After a completed test, **Remove unused** removes each individual comparator that never swapped on any tested input, including those in partially used stages. Remaining CEs retain their stage grouping; empty stages are discarded. Use **Compact stages** separately to repack the remaining CEs. For a sampled test, the tooltip and result message distinguish sample-based removal from exhaustive evidence. **Compact stages** calls `StageBuilderSequence.toStageSequence` to pack comparisons into earlier stages without crossing channel dependencies. Both actions preserve the current playback input, reset playback, update the CE/stage counts, and preserve CE usage counts and colors from the completed test. Counts follow individual comparator occurrences through removal and compaction, including repeated endpoint pairs. Sample-based results retain their original scope; a new generation or test clears them. Use **Test → Remove unused → Compact stages**. Both edits require a completed test, and compaction additionally requires running Remove unused. If there are no unused CEs, Remove unused records that check and enables compaction without changing the network. Compaction disables after use, including when already compact; removing unused CEs re-enables it. Generating another network disables both edit buttons until that network has been tested.

The Random Networks controls use dark teal input fields and compact rows. Channel restrictions and test limits are available in input tooltips; validation errors appear only when present. Status stays above the diagram, with the full message on hover, and the usage legend and generation details are in a collapsed expander below the diagram.

The visible legend is a row of colored blocks labeled with their inclusive upper percentage bounds. Hovering a block explains its interval; hovering a CE gives its exact swap count and percentage. The legend and diagram share one palette. Testing skips the remaining comparators once an input is sorted (all remaining usage is zero), and checks cancellation within long comparator loops.

Compaction animates CE positions for 600 ms with an ease-in/ease-out transition. Usage colors follow each comparator occurrence, including repeated endpoints. Stage bands and labels crossfade while channel lines contract; balls resume at the preserved input when the transition ends. The animation uses only the bounded before/after display prefixes, fading CEs across the cutoff. Generation, testing, editing and playback controls are disabled during the transition. Leaving the page ends the animation and retains the already committed compacted network. Windows' disabled client-area animations setting skips the transition.
