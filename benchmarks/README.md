# Evaluation performance

`Evaluation.fsx` compares Boolean, Bitv512, Uint8v256 and Uint8v512 evaluation with 1, 2, 4, 8 and 16 workers. It reads the initial SGD project's copied inputs and completed pool result. It measures the 32 starting networks, the 32 final networks at their final CE cutoff, and one network against a larger test set. Run the same script against a preserved CLI build and the new Release build, sequentially on an otherwise idle machine.

```powershell
$env:DOTNET_GCHeapHardLimit = '0x400000000'
$env:DOTNET_PROCESSOR_COUNT = '16'
dotnet fsi --lib:src/Sortopia.Cli/bin/Release/net10.0 benchmarks/Evaluation.fsx -- --project C:\GuiSpace\SorterSgd\Simple_msuf4-cc16-prefix\standard --output artifacts/evaluation-network-final.json --label network
```

The script checks every candidate's full comparator-use array and failure count against scalar evaluation before timing. Timed evaluations exclude these correctness checks, file loading, conversion and network construction. Each configuration is warmed up and measured three times; the JSON records the median elapsed sample, process CPU time and managed bytes allocated per batch. Loading, input conversion, materialization and mutation have separate diagnostic timings. Allocation is total managed allocation, not peak resident memory. These are workload measurements, not universal speedup guarantees; OS scheduling, JIT compilation, GC and CPU frequency affect the results.

## Scheduling and ownership

- Scalar formats distribute independent networks across the supplied worker budget.
- SIMD evaluation uses network ownership when there are at least as many networks as test blocks, or when one worker is requested. Otherwise it retains block parallelism. This conservative rule keeps the existing block strategy for the byte-vector formats' larger block counts; merely having enough networks to occupy all workers did not consistently improve those cases in the measurements.
- Each network owner accumulates ordinary integer counters and reuses its work buffer across blocks. Input vectors are snapshotted once and shared read-only. No atomic counter merge is needed on this path.
- The block fallback retains its existing worker-local counters and atomic final reduction. It remains useful for a single network against many inputs.
- SGD mutations, network materialization and evaluation are separate bounded parallel phases. The sequence of generations and stable output order are unchanged.
- Repeated SGD count-only evaluation prepares sufficiently large Boolean sources as Bitv512 once, preserving IDs, ordering, duplicates and the persisted source format.

## Whole-run validation, 2026-11-17

The same saved cc16 Simple MSUF4 inputs, source combination, 32 seeds and 100 generations were executed in two isolated project copies. Both processes had a 16-logical-processor budget and a 16 GiB managed heap limit. Existing outputs were absent in each copy, so the comparison includes preflight, input loading, evolution and final publication.

| Build | Elapsed |
| --- | ---: |
| Preserved implementation | 89.54 s |
| Optimized implementation | 20.07 s |

This single end-to-end comparison was about 4.46 times faster. The complete saved `SorterPoolSet` file, containing all final models/evaluations and all 101 generation summaries, was byte-for-byte identical in both copies and in the original workspace. Its SHA-256 was `4F43B0325F074B7AD6081EFC806125BB2E74E83916021A062FA084591BD2B933`. All three optimized result files passed the normal integrity check.

The improvement includes more than scheduling: single-file execution reuses the already validated setup snapshot, and bounded MSUF4 materialization stops unfolding when it reaches the requested suffix length. The latter uses a lazy sequence to avoid constructing discarded stages. Initial diagnostics allocated about 8 GB while loading the 10,000-model source, and about 329 MB while materializing the final 32 networks using the previous full-unfold-then-truncate path. These costs justified eliminating redundant work before introducing additional counter pooling. Work buffers already live across all test blocks for a network; no global mutable buffer cache was added.

Function names, versions, serialized formats and cache identities are unchanged. The original workspace's generated files and results were not replaced by the validation runs.

## Worker scaling

Representative median batch timings from the 1/2/4/8/16-worker comparison, excluding correctness checks:

| Workers | Starting 32, original Bitv512 | Starting 32, network-owned Bitv512 | Final 32, original Bitv512 | Final 32, network-owned Bitv512 |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 35.69 ms | 48.14 ms | 2.04 ms | 2.18 ms |
| 2 | 17.16 ms | 20.40 ms | 1.22 ms | 2.17 ms |
| 4 | 15.66 ms | 18.28 ms | 1.00 ms | 4.62 ms |
| 8 | 11.42 ms | 13.24 ms | 1.30 ms | 1.91 ms |
| 16 | 11.04 ms | 9.33 ms | 1.43 ms | 1.30 ms |

At 16 workers, managed allocation for the starting batch fell from 17.54 MiB to 7.06 MiB; the final batch fell from 1.74 MiB to 0.74 MiB. Timing variability was substantial, including between unchanged block-path measurements. These samples support the allocation reduction and the end-to-end improvement, but do not establish a universal optimal worker count. For this workload, avoiding scalar evaluation, redundant deserialization, and unused MSUF4 unfolding produced the larger gains.
