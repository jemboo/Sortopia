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

## Dynamic batch CPU allocation

`PrepareSweeps.fsx` and `BenchmarkSweeps.ps1` compare a preserved CLI build with the dynamic scheduler using isolated copies of saved MSUF4 inputs. The input preparation script retains the model sets, tests, population sizes and mutation parameters. Only copied run identities, generation targets and checkpoint intervals change. The source workspace is read-only throughout.

The three scenarios exercise a fresh source-by-OrthoRate grid, four files with unequal generation targets, and four selected files with three already complete. The last scenario creates its completed results outside the timed trials. Each CLI process uses the requested CPU percentage and memory setting, uses Auto parallel files, and includes startup, preflight, shared preparation, evolution and checkpoint publication in its elapsed time. In the current CLI, `-Memory` selects a soft target; `-HardMemoryCap` optionally adds an explicit cap to the candidate. Older baselines interpret `-Memory` as a hard cap. Job metrics record the actual target and cap separately; only an explicit cap is checked against the measured peak. Trials alternate build order. The harness records process CPU time and samples private memory every 25 ms; this sampling is confined to the benchmark harness. Every persisted result is SHA-256 checked against the first trial of that scenario, including snapshots and summary sets.

Preserve the old Release output before changing the scheduler, then build the candidate in Release. For example, from the repository root:

```powershell
dotnet fsi --lib:artifacts/dynamic-cpu-benchmark/baseline/net10.0 benchmarks/PrepareSweeps.fsx -- `
  'C:\GuiSpace\Evo\Small\Simple\pfx-1\msuf4\cc-16\sym-0.05' `
  artifacts/dynamic-cpu-benchmark/templates20 20

./benchmarks/BenchmarkSweeps.ps1 `
  -Baseline artifacts/dynamic-cpu-benchmark/baseline/net10.0/sortopia.exe `
  -Candidate src/Sortopia.Cli/bin/Release/net10.0/sortopia.exe `
  -Templates artifacts/dynamic-cpu-benchmark/templates20 `
  -Output artifacts/dynamic-cpu-benchmark/measured `
  -CpuPercent 50 -Memory 8GiB -Repetitions 2
```

Both scripts require new output directories and preserve previous measurements. The optional final argument to input preparation selects the fresh grid's generation count (a multiple of 20, default 100). Permanent snapshots are every 20 generations and temporary snapshots every 10. These bounded sweeps exercise the same populations as the source, but their timing should not be extrapolated directly to a full 1,000-generation run. Input preparation and serialization can dominate short runs, and adding workers does not accelerate every phase.

### Measurements, 2026-11-26

The source was `C:\GuiSpace\Evo\Small\Simple\pfx-1\msuf4\cc-16\sym-0.05`: three copied model sets crossed with OrthoRate 1.5, 2.5 and 3.5, retaining two pools of 128 sorters per invocation. The Windows machine exposed 32 logical processors. Both builds used a 50% CPU hard cap, a 16-worker allowance, an 8 GiB committed-memory cap and at most four simultaneous files. No builds or tests ran alongside the measured trials.

Each number below is the mean of two fresh CLI process executions, with build order baseline/dynamic then dynamic/baseline. The nine-file grid ran to generation 20. The uneven batch used targets 20, 20, 20 and 100. The mostly-complete batch reused the first three completed 20-generation files and executed the remaining file to generation 100; its preparation is excluded from timing.

| Sweep | Fixed allocation | Dynamic allocation | Elapsed reduction | Process CPU seconds, fixed → dynamic |
| --- | ---: | ---: | ---: | ---: |
| Fresh nine-file grid | 220.98 s | 200.22 s | 9.4% | 816.60 → 925.28 |
| Uneven four-file batch | 166.52 s | 142.12 s | 14.7% | 537.60 → 628.30 |
| Three complete, one pending | 106.29 s | 87.27 s | 17.9% | 285.23 → 380.35 |

Individual elapsed samples were 223.03/218.93 s versus 210.22/190.22 s for the grid, 169.18/163.86 s versus 142.13/142.12 s for the uneven batch, and 107.97/104.60 s versus 86.89/87.64 s for the mostly-complete batch. Two trials per case establish these observations, not a universal speedup or statistical confidence interval. CPU time increased by 13.3%, 16.9% and 33.4%, respectively: shorter elapsed times came with higher total computational cost.

Every persisted result matched byte-for-byte across both builds and both repetitions: 36 files per grid trial and 24 per trial of each other scenario, including the complete snapshot and summary payloads. The largest sampled private-memory peak was 5.025 GiB, below the 8 GiB hard cap. Initial 4 GiB and 6 GiB pilots encountered allocation or admission-headroom failures and were excluded; the measured comparisons all used 8 GiB.

Raw measurements, per-trial logs, resource samples and result hashes are retained under `artifacts/dynamic-cpu-benchmark/measured`. Aggregate results are in `artifacts/dynamic-cpu-benchmark/summary.json`, and hashes of the preserved baseline and candidate assemblies are in `artifacts/dynamic-cpu-benchmark/builds.json`.

Validation passed: 86 evaluation tests, 164 prepared-execution/resource-allocation tests, the native Windows hard-limit test, and all 1,455 WPF tests. The WPF suite required a serial rerun after one parallel run hit an intermittent XAML-loading failure. The solution and Release CLI build succeeded; the solution rebuild retained the unrelated existing xUnit2031 analyzer warning in `WorkspaceImportFunctionTests.cs`.

## CPU overhead investigation, 2026-11-26

The earlier 13–33% increase measured total process CPU time, including execution, parallel-loop coordination and garbage collection. It did not isolate the allocation coordinator's own cost. The follow-up keeps the dynamic coordinator enabled and changes worker count and GC configuration independently.

`Sortopia.CpuBench` uses the same prepared-file executor and Windows hard-limit implementation as the CLI. Each trial runs in a new Release process with a 50% CPU cap and an 8 GiB committed-memory cap. The source is the prepared mostly-complete scenario above: three completed files and one pending 100-generation MSUF4 invocation, with two pools of 128 sorters. Timings include executor preflight, preparation, evolution and persistence, and exclude process startup, copying the template and hashing results. These timings should therefore be compared within this experiment, rather than directly substituted into the earlier whole-process table.

Two trials per configuration produced these means. The worker sweep used 4/8/16 then 16/8/4 workers. Server GC tests used heap limits 4/8 then 8/4 with 16 computation workers, followed by two trials with 8 computation workers and a heap limit of 8. GC settings were supplied only to the benchmark child processes; production defaults were not changed.

| GC configuration | Computation workers | Elapsed | Total CPU | GC pause time |
| --- | ---: | ---: | ---: | ---: |
| Workstation, current CLI default | 4 | 114.72 s | 278.41 s | 28.12 s |
| Workstation | 8 | 92.43 s | 303.03 s | 29.26 s |
| Workstation | 16 | 86.11 s | 378.11 s | 32.63 s |
| Server, heap limit 4 | 16 | 54.48 s | 354.24 s | 16.11 s |
| Server, heap limit 8 | 16 | 45.09 s | 356.29 s | 8.98 s |
| Server, heap limit 8 | 8 | 50.92 s | 258.06 s | 8.69 s |

All configurations allocated approximately 277 GiB cumulatively during execution; this is allocation volume, not simultaneously live memory. Every trial produced the same 24 persisted result files byte-for-byte. Workstation GC at 16 workers completed about 9,856 thread-pool work items, versus 4,623 at 8 workers, while allocating nearly the same number of bytes. It also spent longer paused for GC. This supports excessive parallel-loop/runtime overhead as a contributor. These counters cannot apportion the remaining CPU difference among user-mode synchronization, cache effects, and GC; a CPU stack profile would be needed for that attribution. GC pause time is elapsed time during suspension, not GC CPU time.

Reducing workers alone is a tradeoff: 8 workers used 19.9% less CPU than 16 but took 7.3% longer on average. The two 8-worker samples were 85.62 and 99.24 s, so the first sample alone would have overstated the case for a worker cap. Combining 8 computation workers with server GC capped at 8 heaps gave 50.93/50.90 s elapsed and 258.50/257.62 CPU-seconds. Relative to the current 16-worker/workstation configuration, that is **40.9% less elapsed time and 31.7% less CPU time**. Server GC with 16 computation workers was faster still, but used more CPU than the 8-worker candidate.

This investigation motivated the automatic policy described below. These results are specific to this machine and workload. Reducing temporary allocations and combining small parallel phases are further candidates; their benefit has not been measured here.

Reproduction, after preparing the sweep templates and completed-file fixture above:

```powershell
dotnet build benchmarks/Sortopia.CpuBench/Sortopia.CpuBench.fsproj -c Release

./benchmarks/CpuWorkerScaling.ps1 `
  -Template artifacts/dynamic-cpu-benchmark/measured/mostly-complete-prepared `
  -Output artifacts/cpu-worker-scaling-processes

./benchmarks/CpuGcScaling.ps1 `
  -Template artifacts/dynamic-cpu-benchmark/measured/mostly-complete-prepared `
  -Output artifacts/cpu-gc-scaling `
  -ExpectedHashes artifacts/cpu-worker-scaling-processes/round-1-workers-16/result-hashes.json

./benchmarks/CpuGcScaling.ps1 `
  -Template artifacts/dynamic-cpu-benchmark/measured/mostly-complete-prepared `
  -Output artifacts/cpu-gc-scaling-workers8 -Workers 8 -HeapLimits 8 `
  -ExpectedHashes artifacts/cpu-worker-scaling-processes/round-1-workers-16/result-hashes.json
```

The scripts require new output folders. Raw metrics and hashes are retained in those three artifact folders; combined means are in `artifacts/cpu-overhead-summary.json`. An earlier interactive F# probe is excluded because its runtime and retained heap state were unsuitable for this comparison. The standalone benchmark build succeeded using cached dependencies, with an NU1900 warning because the NuGet vulnerability service was unavailable.

Runtime interpretation follows Microsoft's documentation on [parallel-loop overhead](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/potential-pitfalls-in-data-and-task-parallelism), [GC configuration and startup requirements](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector), and [GC pause-time metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-runtime).

## Historical worker and GC policy (superseded 2026-11-27)

The measurements below describe the earlier launcher/worker and fixed-heap implementation. The whole-job memory investigation at the end of this document supersedes that runtime policy; these trials remain here as historical evidence.

Bounded terminal execution now starts a worker with its GC settings configured before CLR startup. One Windows Job Object covers both launcher and worker; inherited CPU and memory limits are verified by the worker before it reads inputs. CPU workers and percentage-based memory are resolved once. Launcher memory is reserved from the selected total, with a minimum 64 MiB reserve. The remaining worker allowance supplies both admission control and a 75% managed-heap limit.

The per-file ceiling is `min(8, CPU workers)`. Dynamic sharing still lets simultaneous files use the full batch worker budget. GC uses at most `min(8, floor(CPU workers / 2), floor(worker memory / 512 MiB))` heaps, with a minimum of one. A one-heap configuration uses workstation GC. Thus a 16-worker CPU budget with 7 GiB total memory uses at most eight server heaps and eight evaluation workers per file; an eight-worker budget with 2 GiB total memory uses at most three server heaps after reserving launcher memory. These are conservative workload-based limits, not a universal optimum.

Memory admission and checkpoint headroom checks request an aggressive full collection before refusing work, asking GC to decommit unused regions. This is restricted to memory-pressure paths; ordinary phases do not force collections. The 80% admission threshold and Windows hard caps are unchanged. An initial ordinary-collection implementation incorrectly refused a later file in the nine-file sweep because server GC retained reusable committed memory.

Larger populations also require larger reservations when their source is shared: SGD admission now takes the larger of the unshared-source estimate and `pool count × sorters per pool × (1 + children per parent) × 512 KiB`, with the existing 64 MiB minimum. The per-member allowance is a conservative heuristic calibrated to these 16-channel sweeps, not a bound on arbitrary inputs. At 6 GiB the old 64 MiB shared-source reservation admitted too many large populations at once; reducing GC heaps from eight to four alone still failed. The population-aware reservation fixed the case while preserving the same hard cap. Oversized estimates saturate safely and are rejected without arithmetic wraparound.

`BenchmarkSweeps.ps1 -CandidateJobMetrics` reads the candidate's optional `--execution-metrics` output. Candidate CPU time and peak committed memory include both processes, including a terminated worker. The previous single-process baseline still uses process CPU time and 25 ms memory samples. Neither diagnostics nor benchmark sampling introduces polling into the Results page. Windows job accounting and child inheritance follow [Microsoft's Job Object documentation](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

Integration coverage compares three-file batches against serial execution at 0.25, 0.5 and 2 GiB, with populations of 8, 128 and 256 on eight channels. It verifies identical result bytes, inherited CPU/memory limits, runtime GC mode, aggregate peak memory, and that CPU percentages are not applied twice. Separate checks cover cooperative cancellation, native allocation rejection at the hard memory cap, dynamic CPU leases and memory admission. Low budgets are suitable for correspondingly small inputs; tuning does not guarantee that every existing source pool fits.

Final comparisons on the same 32-logical-processor machine, with 20 generations and two pools per file:

| Workload | CPU / memory cap | Previous elapsed | Tuned elapsed | Previous CPU | Tuned CPU | Tuned peak commit |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Nine files, 128 sorters per pool | 50% / 7 GiB | 221.61 s | 116.91 s | 1,046.80 s | 817.52 s | 5.36 GiB |
| Three files, 512 sorters per pool | 25% / 6 GiB | 230.16 s | 143.99 s | 844.52 s | 764.67 s | 4.55 GiB |

These are single full-batch trials per build/configuration, not statistical performance bounds. The reductions were 47.2%/37.4% in elapsed time and 21.9%/9.5% in aggregate CPU time. All 36 result files from the nine-file batch and all 12 from the larger-population batch matched their baselines by SHA-256. The nine-file baseline is retained in `artifacts/execution-tuning/concurrent7`, its final candidate in `final-concurrent7`; the larger baseline is in `large6`, its final candidate in `large6-reserved`. `artifacts/execution-tuning/summary.json` combines the measurements. The baseline binary was preserved in `artifacts/execution-tuning/baseline` before these changes.

The 4 GiB stress test did not reliably fit the larger workload: the previous implementation failed two files with Auto concurrency; the final tuned implementation completed two files and failed one even with `--jobs 1`. It reported an out-of-memory error with aggregate peak commitment of 3.04 GiB, below the OS cap; its managed-heap allowance was 2.95 GiB. Raising the budget to 6 GiB resumed that copied partial batch, skipped its two complete files, and produced all 12 files byte-for-byte identical to uninterrupted execution (`artifacts/execution-tuning/recovery6`). This recovery is a correctness check, not a timing comparison. The smaller eight-channel integration workloads passed at 0.25–2 GiB.

Failed development trials are retained separately and excluded from the table: an 8 GiB launch was refused when available memory fell below the requested amount; the initial seven-GiB server configuration exposed retained-heap admission failure; large-population six-GiB trials with the old reservation failed at both eight and four GC heaps. Final build and validation: solution and Release CLI builds passed, 180 execution/resource tests, 86 evaluation tests, and 1,455 WPF tests passed. The solution retains one pre-existing xUnit2031 analyzer warning.

Reproduce a paired trial with `BenchmarkSweeps.ps1 -CandidateJobMetrics`, using the preserved baseline and current Release CLI. `-Builds dynamic -ReferenceHashes <baseline/result-hashes.json>` permits reusing an existing completed control without repeating its execution; `-Jobs 1` tests a serial memory profile. Use fresh output directories and run timed trials without concurrent builds/tests. For example:

```powershell
./benchmarks/BenchmarkSweeps.ps1 `
  -Baseline artifacts/execution-tuning/baseline/net10.0/sortopia.exe `
  -Candidate src/Sortopia.Cli/bin/Release/net10.0/sortopia.exe `
  -Templates artifacts/execution-tuning/large-templates `
  -Output artifacts/execution-tuning/repeated-large6 `
  -Scenarios larger-population -CpuPercent 25 -Memory 6GiB `
  -Repetitions 2 -CandidateJobMetrics
```

For the larger saved-workload comparison, `PrepareSweeps.fsx` accepts optional population and `tuning` arguments. This creates three parameter files with 512 sorters per pool, changing both starting and selected population counts and recalculating parameter identities, while leaving the source workspace untouched:

```powershell
dotnet fsi --lib:src/Sortopia.Cli/bin/Release/net10.0 benchmarks/PrepareSweeps.fsx -- `
  'C:\GuiSpace\Evo\Small\Simple\pfx-1\msuf4\cc-16\sym-0.05' `
  artifacts/execution-tuning/large-templates 20 512 tuning
```

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

## SGD batching and immutable model sharing (2026-11-27)

SGD now creates and evaluates children in batches of at most 128. Initial selection and retained-parent reevaluation use the same bound. Every candidate and its complete V2 evaluation still participate in the existing percentile, duplicate and ranking rules. Mutation preserves its random draws and ordering. MSUF4 reuses unchanged immutable unfolding steps and stages; MSUF4 and MSRS mutation avoid copying stage arrays and prefixes through public accessors and constructors. Public constructors/accessors still make defensive copies.

The preserved baseline includes the preceding UMX ID tags and compact histogram MessagePack format. Baseline and candidate CLI binaries, hashes, logs and measurements are in `artifacts/sgd-memory-20261127`. Builds and tests were kept out of timed trials.

### Focused population memory

`MeasureSgdMemory.fsx` isolates one 512-member MSUF4 pool with 895 stored stages, 16 channels, three children per parent, ten generations, and eight workers. Both fresh FSI processes used server GC. A small warmup precedes measurement; input/model creation, seed evaluation and result serialization are excluded from timed allocation. Full collections measure live managed memory, first with starting models retained and then after their preparation frame has returned. Initial population memory was 107.0 MiB in both builds.

| Measurement | Baseline | Optimized | Reduction |
| --- | ---: | ---: | ---: |
| Final population, starting models released | 105.0 MiB | 27.3 MiB | **74.0%** |
| Additional final-population memory with starting models retained | 104.9 MiB | 23.4 MiB | 77.7% |
| Cumulative managed allocation during evolution | 83.0 GiB | 65.0 GiB | **21.7%** |

The allocation row is garbage-collection traffic, not simultaneous RAM use. These are single diagnostic samples per build, retained in `retained-baseline-512.json`, `retained-candidate-512.json`, and `focused-comparison.json`. Elapsed evolution was 59.16/53.93 seconds. Earlier unwarmed probes (`memory-*-512.json`) measured only the extra memory with starting models retained and are not used for the standalone-population claim. Full serialized final populations in all four probes share SHA-256 `624166165D4560A87C871E1CA4DAABEC3E3C6E451A1D61386FE9D968AA778AC2`.

### Complete saved-run measurements

Three saved 16-channel MSUF4 runs, two pools of 512 members per run, 20 generations, 25% CPU, `--memory 6GiB --jobs Auto`. Peak commitment and CPU include the launcher and execution workers through Windows job accounting for **both** builds.

| Trial | Elapsed | Aggregate CPU | Peak committed memory | Outcome |
| --- | ---: | ---: | ---: | --- |
| Baseline 1 | 318.99 s | 2,023.30 s | 4.548 GiB | All three runs completed |
| Optimized 1 | 263.95 s | 1,695.16 s | 4.547 GiB | All three runs completed |
| Optimized 2 | 269.05 s | 1,758.34 s | 4.536 GiB | All three runs completed |
| Baseline 2 | Failed | 1,560.69 s | 4.548 GiB | Two runs threw `OutOfMemoryException` |

Three paired repetitions were requested, alternating build order; the harness stopped on the fourth trial when the repeated baseline failed. The first complete pair was 17.3% faster with 16.2% less CPU, but **whole-job peak memory was essentially unchanged**. These are individual workload observations, not repeated baseline medians or a guarantee of a larger maximum pool size.

Both optimized trials matched all 12 saved MessagePack files from the successful baseline by path and SHA-256. The optimized build also completed a copy of the failed baseline batch at the same 6 GiB budget, skipped its already-complete run, and matched all 12 uninterrupted-control files. That recovery took 198.90 seconds and is a correctness check, not a fresh-run timing comparison (`recovery-6gib-clean`). An earlier recovery launch was refused because a copied diagnostics file already existed; it did not execute work and is excluded.

### Replay and ownership verification

`VerifySgdReplay.fsx` runs the same cases against separately preserved builds: MSCE/MSRS/MSUF4, V1/V2, Random/Best initialization, duplicate filtering and new-mutant priority, two 129-member pools, three children per parent, partial final batches, adaptive CE cutoffs, and checkpoint resume with different worker counts. All **108 generation snapshots and 108 compact summaries** had identical hashes across builds. Retained earlier snapshots remained byte-identical after later generations, and all 18 resumed final populations matched uninterrupted execution. The two JSON manifests share SHA-256 `32F03171562EF42B9A1717C70003FDDC20408621CBEE8306402572E8E5BE1513`.

The evaluation (91), model (195), permutation (91), and prepared-execution/storage (160) tests passed: **537 tests**. Added regression cases cover partial batches, Random/Best initialization, multiple children, resumed snapshots, and mutation/accessor isolation for shared MSUF4/MSRS data. The Release CLI build passed without warnings or errors.

### Reproduction

Run each build sequentially, using fresh output paths:

```powershell
$baseline = 'artifacts/sgd-memory-20261127/baseline/Release/net10.0'
$candidate = 'artifacts/sgd-memory-20261127/candidate/Release/net10.0'
dotnet fsi --lib:$baseline benchmarks/VerifySgdReplay.fsx -- artifacts/replay-control.json
dotnet fsi --lib:$candidate benchmarks/VerifySgdReplay.fsx -- artifacts/replay-optimized.json
dotnet fsi --lib:$baseline benchmarks/MeasureSgdMemory.fsx -- artifacts/memory-control.json 512
dotnet fsi --lib:$candidate benchmarks/MeasureSgdMemory.fsx -- artifacts/memory-optimized.json 512
./benchmarks/BenchmarkSweeps.ps1 `
  -Baseline "$baseline/sortopia.exe" -Candidate "$candidate/sortopia.exe" `
  -Templates artifacts/execution-tuning/large-templates -Output artifacts/repeated-sgd-memory `
  -Scenarios larger-population -CpuPercent 25 -Memory 6GiB -Jobs Auto `
  -Repetitions 3 -BaselineJobMetrics -CandidateJobMetrics
```

## Whole-job memory investigation (2026-11-27)

The 4.55 GiB plateau was not the live size of the evolving pools. The previous 6 GiB policy reserved at least 64 MiB for its launcher, gave the worker a 4.453 GiB managed-heap limit, and retained server-GC capacity near that limit. Process overhead brought whole-job commitment to about 4.55 GiB. Its admission/checkpoint ceiling was 80% of worker memory, above the 75% heap limit, so an allocation could fail before the headroom check intervened. The new code derives both ceilings from one 75% calculation.

Tracing also found two avoidable sources of pressure. The executor kept the complete shared source model graph alive after seed selection, despite only selected seeds being needed. The source's uncompressed payload is 470,450,067 bytes (449 MiB); the codec serialized an extra copy merely to count or validate its length. Concurrent checkpoint conversions added overlapping DTO graphs and raw buffers. The baseline already reached about 4.30 GiB before evolution started; its highest peak occurred during later snapshot work.

The runtime policy had become too elaborate. Commit `7bdb47b` changed the earlier default of one file at a time without explicit resource caps into Auto concurrency with hard OS/GC budgets, a launcher/worker protocol and manual GC heuristics. Those changes introduced new ways to fail under a budget; the old serial, uncapped behavior was not an equivalent memory test. In particular, setting `GCHeapCount` disables automatic server-GC adaptation, as [Microsoft's GC architect explains](https://devblogs.microsoft.com/dotnet/preparing-for-dotnet-10-gc/). The batching and immutable-sharing changes still have the measured live-memory/allocation benefits documented above; they are retained.

The replacement uses one CLI process and default workstation GC. It deletes the 155-line worker launcher, child cancellation/environment protocol, launcher-memory subtraction and heap-count heuristic. It keeps the requested OS CPU/memory caps, CPU leases, and conservative admission checks. Preparation reports which source inputs execution no longer needs; a closed batch releases them after all consumers prepare. Random and Best seed selection both use the preparation cache, with selection-specific keys. One small codec lock bounds concurrent model/pool conversion; it does not serialize evolution or small reports. The envelope length is counted from its header and existing payload size, without another payload copy. File formats and result identities are unchanged.

### Controlled whole-job comparisons

All rows use fresh copies of the same three saved 16-channel MSUF4 runs, two 512-member pools per run, 20 generations, 25% CPU and Auto file concurrency. The baseline already includes the preceding batching/sharing optimization. Both builds use Windows job accounting for peak commitment and aggregate CPU. Builds/tests were excluded from timed runs. These are individual diagnostic trials, not medians or universal performance claims.

| Configuration | Budget | Elapsed | Aggregate CPU | Whole-job peak |
| --- | ---: | ---: | ---: | ---: |
| Baseline, fixed server heaps and retained source | 6 GiB | 299.20 s | 2,052.44 s | 4.551 GiB |
| Remove redundant codec copies only | 6 GiB | 172.52 s | 1,087.72 s | 4.531 GiB |
| Workstation GC, source still retained | 6 GiB | 298.90 s | 1,610.66 s | 4.090 GiB |
| Release source, adaptive server GC, one process | 6 GiB | 120.56 s | 859.09 s | 5.011 GiB |
| Release source, default workstation GC, one process | 6 GiB | 144.71 s | 673.81 s | 4.137 GiB |
| Final: also bound model/pool codec conversion | 6 GiB | 155.23 s | 728.59 s | 3.853 GiB |
| Final: repeat without profiler | 6 GiB | 155.45 s | 709.56 s | 4.072 GiB |
| Final: lower-budget test, no profiler | 4 GiB | 144.84 s | 651.48 s | 3.020 GiB |

Every row completed all three runs and matched all **12 saved MessagePack files** by path and SHA-256 against the same uninterrupted control. The final profiled trial reduced peak commitment by **15.3%**, elapsed time by **48.1%**, and aggregate CPU by **64.5%** relative to this baseline. Adaptive server GC was faster but had a higher peak, so it was not selected. The codec lock traded about 7% more elapsed time for another 7% lower peak relative to the unlocked workstation trial. Large full-source decoding and checkpoint buffers still contribute to peak memory; the 74% live-population saving is not a claim of 74% lower whole-job commitment.

Preserved builds, per-trial metrics, logs, output hashes and 250 ms diagnostic traces are under `artifacts/memory-peak-20261127`. `baseline-6gib`, `codec-6gib`, `workstation-6gib`, `adaptive-6gib`, `workstation-released-6gib`, and `serial-codec-6gib` correspond to the table rows. The opt-in `Sortopia.MemoryProbe` startup hook is not referenced or shipped by the application. Its managed-memory samples use `GC.GetTotalMemory(false)` and include uncollected garbage; they are not forced-collection live-object measurements. Whole-job peaks in the table come from Windows, not the sampler.

The unprofiled 6 GiB repeat (`final-6gib-repeat`) also matched all 12 files, taking 155.45 seconds with a 4.072 GiB peak. Across the two final 6 GiB trials, peak reduction was **10.5–15.3%**, and elapsed time was about 48% lower than this single baseline. The codec-lock comparison is diagnostic evidence of a tradeoff, not an isolated repeated estimate of its effect.

The fresh 4 GiB trial (`final-4gib`) completed all three runs without allocation failures and matched the same 12 output hashes. The earlier implementation failed the corresponding 4 GiB workload, including a serial attempt documented above. This demonstrates that the tested workload now fits that budget; it is not a guarantee for arbitrary pool sizes. The tighter budget changes collection/admission behavior, so its timing is not a pure comparison of GC costs.

### Final correctness checks

The final build passed **203** prepared-execution, storage and resource-control tests. These include Random/Best seed preparation for MSUF4/MSRS with source files unavailable after preparation, cancellation and checkpoint resume, serial/parallel result equality, low-budget CLI execution, native OS cap enforcement, and envelope-length/corruption checks. The full solution built successfully with one pre-existing xUnit2031 warning in `WorkspaceImportFunctionTests.cs`.

The final replay manifest matched all **108 snapshots and 108 summaries** against the preserved baseline, including all 18 resumed final populations and unchanged earlier-generation snapshots. Both manifests have SHA-256 `32F03171562EF42B9A1717C70003FDDC20408621CBEE8306402572E8E5BE1513`. The three final whole-job trials (two at 6 GiB and one at 4 GiB) each matched all 12 saved MessagePack files. No original workspace data was modified.

The normal Release CLI was rebuilt without warnings or errors. Its Sortopia assemblies and runtime configuration match the measured candidate by SHA-256; the preserved baseline assemblies are unchanged.

To reproduce against the preserved control, use a fresh output directory:

```powershell
./benchmarks/BenchmarkSweeps.ps1 `
  -Baseline artifacts/memory-peak-20261127/baseline/sortopia.exe `
  -Candidate src/Sortopia.Cli/bin/Release/net10.0/sortopia.exe `
  -Templates artifacts/execution-tuning/large-templates `
  -Output artifacts/recheck-whole-job-memory `
  -Scenarios larger-population -CpuPercent 25 -Memory 6GiB -Jobs Auto `
  -Repetitions 1 -Builds dynamic -CandidateJobMetrics `
  -ReferenceHashes artifacts/sgd-memory-20261127/paired-512/larger-population-1-baseline/result-hashes.json
```

## Larger pools at the same budgets (2026-11-27)

The next comparison increased both `SorterCountPerPool` and `SelectedSorterCountPerPool` to 1,024 and 2,048. Each trial still contains three 16-channel MSUF4 runs, two pools per run, one child per parent, Best initialization, 20 generations, snapshots at generations 0 and 20, a temporary-snapshot interval of 10, 25% CPU, and Auto file concurrency. Thus 2,048 members means 4,096 retained members per run. Setup inputs are byte-identical to the previous 512-member workload. The baseline is the preserved build from before the latest memory/runtime simplification (`memory-peak-20261127/baseline`); the candidate is a frozen copy of the updated Release CLI.

One fresh trial per build/population/budget was run, sequentially and without profiling, builds, or tests during timing. These are individual observations, not medians. Windows job accounting supplies the complete job peak, including the baseline's launcher and worker. Rejected/failed rows report elapsed time and peak **until failure**, not the cost of completing evolution.

| Members per pool | Budget | Build | Outcome | Elapsed | Peak committed memory |
| ---: | ---: | --- | --- | ---: | ---: |
| 1,024 | 6 GiB | Baseline | All 3 completed | 228.73 s | 4.531 GiB |
| 1,024 | 6 GiB | Updated | All 3 completed | 254.38 s | 4.481 GiB |
| 1,024 | 4 GiB | Baseline | All 3 threw `OutOfMemoryException` | 128.37 s | 3.043 GiB |
| 1,024 | 4 GiB | Updated | Admission refused all 3 | 26.41 s | 2.678 GiB |
| 2,048 | 6 GiB | Baseline | Admission refused all 3 | 17.48 s | 4.361 GiB |
| 2,048 | 6 GiB | Updated | Admission refused all 3 | 18.49 s | 2.533 GiB |
| 2,048 | 4 GiB | Baseline | Admission refused all 3 | 14.70 s | 3.043 GiB |
| 2,048 | 4 GiB | Updated | Admission refused all 3 | 16.91 s | 2.681 GiB |

At 1,024 members and 6 GiB, the updated build used **1.1% less peak memory but took 11.2% longer** than this baseline trial. Aggregate CPU fell 6.7% (1,267.28 to 1,181.98 CPU-seconds). The large 512-member speedup does not generalize to this case. Auto admission and GC behavior are part of these whole-job measurements; they do not isolate evaluation throughput.

The admission estimate is `2 pools × members × (1 + 1 child) × 512 KiB`: 2 GiB per 1,024-member work item, or 4 GiB per 2,048-member work item. At 1,024/4 GiB, the updated build reported only **1.96 GiB** available after headroom and refused a 2 GiB reservation. At 2,048/6 GiB it reported **3.46 GiB** available for a 4 GiB reservation (baseline: 3.64 GiB). At 2,048/4 GiB, the reservation alone exceeds either build's admission ceiling. These are heuristic refusals after shared-input preparation, with no generation-0 outputs written. They do not establish the actual evolving-pool memory requirement. No production admission or GC settings were changed for this comparison.

### Saved-output verification

The successful updated 1,024/6 GiB batch matches the baseline's **12 saved MessagePack files byte-for-byte by relative path and SHA-256**: six pool snapshots and six summary files. `VerifyPoolSweep.fsx` also fully validates the output plans and decodes all six snapshots. Each saved generation (0 and 20 for all three runs) contains exactly two pools of 1,024 members.

The failed baseline 1,024/4 GiB batch left three generation-0 snapshots and three generation-0 summary files. These six files also match the successful baseline at 6 GiB; none of its three runs reached completion. The updated 1,024/4 GiB batch and every 2,048-member batch produced no results or snapshots, so there is no output-equivalence claim for those cases.

All templates, frozen candidate assemblies, measurements, logs, and verification reports are in `artifacts/pool-scaling-20261127`. `comparison.json` contains all eight outcomes; `verified-1024-6gib.json` records the complete snapshot checks; `verified-baseline-1024-4gib.json` records the partial-output checks. The first no-output trial exposed a reporting-only harness bug (a missing results directory); its measurements were recovered from the already-written resource record without rerunning it. The harness now supports `-ContinueOnFailure`, preserves failure timing/metrics and empty hash manifests, and distinguishes recording a reference from comparing against one.

### Reproduction

Create fresh population-specific templates from the earlier copied workload (the `tuning` mode needs only three parameter files):

```powershell
dotnet fsi --lib:src/Sortopia.Cli/bin/Release/net10.0 benchmarks/PrepareSweeps.fsx -- `
  artifacts/execution-tuning/large-templates/larger-population `
  artifacts/new-1024-templates 20 1024 tuning
./benchmarks/BenchmarkSweeps.ps1 `
  -Baseline artifacts/memory-peak-20261127/baseline/sortopia.exe `
  -Candidate artifacts/pool-scaling-20261127/candidate/sortopia.exe `
  -Templates artifacts/new-1024-templates -Output artifacts/new-1024-6gib `
  -Scenarios larger-population -CpuPercent 25 -Memory 6GiB -Jobs Auto `
  -Repetitions 1 -BaselineJobMetrics -CandidateJobMetrics -ContinueOnFailure
```

Repeat with a fresh output directory for 4 GiB, and fresh templates with population 2,048. Run validation after timing has ended. For completed trials:

```powershell
dotnet fsi --lib:artifacts/pool-scaling-20261127/candidate benchmarks/VerifyPoolSweep.fsx -- `
  artifacts/new-1024-6gib/larger-population-1-dynamic 1024 `
  artifacts/new-1024-6gib/larger-population-1-baseline/result-hashes.json `
  artifacts/new-1024-verification.json
```

Append `partial` to verify only available outputs from an interrupted/failed trial against a complete reference; it still rejects invalid files or differing bytes.

## Soft memory targets and optional caps (2026-11-27)

The CLI now keeps the Windows CPU cap and below-normal priority while treating `--memory` as a soft scheduling target. Estimates control whether another file may overlap active work. A single file may run even when its estimate or the process's committed memory exceeds the target. The default path no longer imposes a managed-heap cap, forces collections, rejects an oversized reservation, or fails a generation for exceeding the target. `--memory-cap` separately opts into a Windows committed-memory cap and its 75% managed-heap allowance. The Run execution page exposes **Memory target** and a blank-by-default **Hard memory cap (optional)** field; existing saved preferences load without a hard cap.

The same three-run workloads above were rerun with two pools per run, 1,024 or 2,048 members per pool, 20 generations, unchanged setup inputs, 25% CPU, and Auto file concurrency. Each row is one fresh full-batch trial, including preparation, evolution, and publication. Trials ran sequentially without builds, tests, or profiling alongside them. Peak is aggregate committed memory from Windows job accounting, not sampled resident RAM. These are individual observations, not medians.

| Members per pool | Soft target | Hard cap | Elapsed | CPU time | Peak committed memory | Outcome |
| ---: | ---: | --- | ---: | ---: | ---: | --- |
| 1,024 | 4 GiB | None | 250.43 s | 1,147.83 s | 5.857 GiB | All 3 completed; 12 identical files |
| 1,024 | 6 GiB | None | 312.41 s | 1,448.25 s | 6.005 GiB | All 3 completed; 12 identical files |
| 2,048 | 4 GiB | None | 563.12 s | 2,619.64 s | 8.605 GiB | All 3 completed; 12 identical files |
| 2,048 | 6 GiB | None | 507.99 s | 2,297.25 s | 8.401 GiB | All 3 completed; 12 identical files |

The previously rejected 1,024/4 GiB and 2,048/4–6 GiB cases now complete by allowing a file to exceed its target. This policy does **not** keep the batch below 4 or 6 GiB, or reserve that much physical RAM for other applications. Peak commitment is higher than the earlier strictly capped trials. The 1,024/6 GiB trial is also slower than the preceding strict-policy trial (312.41 versus 254.38 seconds); the change does not establish a general speed improvement. A larger scheduling target did not consistently improve runtime in this matrix.

### Output and snapshot verification

The 1,024-member trials were compared with the existing successful baseline at 6 GiB. Since neither earlier 2,048-member baseline completed at 4 or 6 GiB, a separate correctness reference was generated with the frozen pre-policy build in `artifacts/pool-scaling-20261127/candidate`, using a 12 GiB hard cap and one file at a time. That reference completed all three runs in 542.94 seconds, using 2,479.72 CPU-seconds and 8.250 GiB peak commitment. It establishes expected bytes; it is not a same-budget performance control. Its frozen assembly hashes were rechecked.

Every trial matched all 12 saved MessagePack files by relative path and SHA-256: **48 file comparisons in total**, including snapshots and summaries. `VerifyPoolSweep.fsx` separately validates output completeness and decodes each permanent snapshot at generations 0 and 20, checking two pools and the requested member count. The deterministic replay matrix also matched the earlier baseline's 108 snapshot hashes and 108 summary hashes, including resume from generation 2 and immutable earlier snapshots. Its manifest SHA-256 remains `32F03171562EF42B9A1717C70003FDDC20408621CBEE8306402572E8E5BE1513`.

Validation passed: the Release solution build, 206 execution/resource tests, and 17 relevant WPF tests. Coverage includes CPU-only execution without an imposed GC limit, optional native memory caps and their inheritance, cancellation while waiting for admission, oversized single-file admission, full-target concurrency, byte-identical CLI output under both policies, and optional-cap preference/command handling. The build retains the existing unrelated xUnit2031 warning in `WorkspaceImportFunctionTests.cs`.

Measurements, native job metrics, logs, saved outputs, frozen candidate assemblies, replay manifest, and snapshot-verification reports are in `artifacts/soft-memory-20261127`. `comparison.json` explicitly records a zero hard cap for all four target trials. The per-trial job metrics distinguish `MemoryTargetBytes` from `HardMemoryLimitBytes`; zero means no explicit cap.

### Reproduction

Reuse the population templates so input identities and result paths remain the same:

```powershell
./benchmarks/BenchmarkSweeps.ps1 `
  -Baseline artifacts/pool-scaling-20261127/candidate/sortopia.exe `
  -Candidate artifacts/soft-memory-20261127/candidate/Release/net10.0/sortopia.exe `
  -Templates artifacts/pool-scaling-20261127/templates-1024 `
  -Output artifacts/new-soft-1024-4gib -Scenarios larger-population `
  -CpuPercent 25 -Memory 4GiB -Jobs Auto -Repetitions 1 `
  -Builds dynamic -CandidateJobMetrics `
  -ReferenceHashes artifacts/pool-scaling-20261127/members-1024-6gib-baseline/larger-population-1-baseline/result-hashes.json
```

Repeat with a fresh output directory for 6 GiB. For 2,048 members, use `templates-2048` and the reference manifest at `artifacts/soft-memory-20261127/baseline-2048-12gib/larger-population-1-baseline/result-hashes.json`. Add `-HardMemoryCap <GiB|percent%>` only when deliberately measuring the candidate with an explicit hard cap. Run `VerifyPoolSweep.fsx` after timing, as shown above.

## Repeated 1,024-member Auto versus one-job comparison (2026-11-27)

The earlier 312.41-second Auto result at a 6 GiB target did not reproduce as a consistent penalty. Three fresh trials were run for each combination of **4/6 GiB soft target × Auto/one job**, using the unchanged frozen Release build from `soft-memory-20261127/candidate/Release/net10.0`. Each trial executes the same three work items, two 1,024-member pools per work item, 20 generations, 25% CPU, eight available workers, and no hard memory cap. The executable and dependencies were hash-checked before and after the investigation.

Conditions were interleaved in three blocks: `4-Auto, 6-one, 6-Auto, 4-one`; then the reverse order; then `6-Auto, 4-Auto, 4-one, 6-one`. Each trial starts with fresh inputs and absent outputs. Copying and SHA-256 comparison occur outside the timer. No builds, tests, or profiling ran alongside these twelve measurements. An external tool-session interruption stopped one incomplete 6 GiB/one-job attempt; its partial artifacts were preserved and excluded, and a fresh retry supplied the completed measurement. These are three observations per condition, not evidence of a universal speed ranking.

| Target | Jobs | Three elapsed times, in round order | Median elapsed | Median CPU time | Median / maximum peak commitment |
| ---: | --- | --- | ---: | ---: | ---: |
| 4 GiB | Auto | 263.62, 251.27, 277.16 s | 263.62 s | 1,213.06 s | 5.487 / 6.014 GiB |
| 4 GiB | One | 242.80, 256.65, 275.83 s | 256.65 s | 1,158.67 s | 6.021 / 6.068 GiB |
| 6 GiB | Auto | 255.21, 245.09, 228.80 s | 245.09 s | 1,143.30 s | 5.910 / 6.037 GiB |
| 6 GiB | One | 280.05, 254.57, 273.79 s | 273.79 s | 1,241.86 s | 6.049 / 6.052 GiB |

One job was 2.6% faster by median at 4 GiB and 11.7% slower at 6 GiB. Auto's target comparison reversed the original result: its 6 GiB median was 7.0% faster than its 4 GiB median. Within-condition ranges span 25–34 seconds. For this workload, these results do not justify forcing one job or lowering the soft target as a speed fix. The execution policy was left unchanged.

### What actually ran concurrently

Saved generation-0 publication times were paired with their generation-20 files and CLI completion events, using paths derived from the saved parameter/output plans. **Every original trial, repeated trial, and diagnostic trial showed one evolution at a time.** Each next generation-0 snapshot was published after the preceding work item completed. Generation zero is saved synchronously before the generation loop, so this bounds overlap of the evolution itself; it is not a claim that all runtime/helper threads were idle. The UI's dispatched/Running status is not used as an admission event.

The source explains why Auto can still behave serially. Its estimate is 2 GiB per work item (`2 pools × 1,024 parents × 2 parent/child slots × 512 KiB`). The soft gate also accounts for existing process commitment; the preparation/heap footprint leaves insufficient estimated space for two items at these targets. Waiting files register no CPU allocation, so the admitted file retains the same eight-worker allowance. With one job, the gate always admits the next item alone, independently of whether the target is 4 or 6 GiB. Neither soft target sets a Windows memory cap or changes the managed-heap limit.

In the original pair, 59.68 seconds of the 61.98-second difference occurred between generation-0 and generation-20 publication, including evolution, intermediate checkpoints, and the final snapshot save. Post-snapshot completion/validation differed by only 1.62 seconds. The slowdown therefore was not an extra minute spent waiting for admission or preparing shared inputs. Original work-item orders differed, but the repeats include different Auto orders without a consistent slowdown, and fixed-order one-job trials also vary substantially.

### Separate GC diagnostics

After the timing matrix, two additional Auto trials used the opt-in `Sortopia.MemoryProbe` startup hook, sampled every 250 ms. These trials are excluded from the medians above. The benchmark-only hook now also records collection counts, total GC pause duration, ThreadPool activity, process CPU, GC-reported system memory load, and system CPU counters. It is not referenced or shipped by the application.

| Soft target | Elapsed | CPU time | Peak commitment | Managed allocation traffic | GC pauses | Gen-0 / Gen-1 / Gen-2 counts |
| ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 4 GiB | 280.25 s | 1,255.88 s | 5.157 GiB | 1,272.82 GiB | 63.435 s (22.7%) | 81,353 / 4,420 / 62 |
| 6 GiB | 267.11 s | 1,201.27 s | 5.375 GiB | 1,272.86 GiB | 56.761 s (21.3%) | 81,353 / 4,454 / 61 |

Both traces report the same 31.213 GiB runtime heap limit, no explicit hard cap, and one evolution at a time. Allocation traffic differs by about 0.003%; the workload performed essentially the same allocation work. The 13.15-second elapsed difference in this diagnostic pair includes a 6.674-second difference in GC pauses. GC is a material contributor to runtime and its variation, but these traces cannot retrospectively assign the original 61.98-second difference to GC, CPU scheduling, clock frequency, or another system condition. The supported conclusion is **run-to-run variation, with no reproduced causal 6 GiB regression or concurrent-evolution penalty**. The exact cause of the old untraced outlier remains unresolved.

Allocation traffic is cumulative managed allocation, approximately 1.24 TiB recycled over the batch, not live RAM or peak commitment; native allocation is excluded by [`GC.GetTotalAllocatedBytes`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes?view=net-10.0). [`GC.GetTotalPauseDuration`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalpauseduration?view=net-10.0) supplies process-lifetime pause time. Total system CPU activity averaged 17.25% and 16.91%, with roughly 1.03 and 0.91 CPU cores of activity outside the measured process. GC-reported system memory load peaked at 14.67 and 15.61 GiB, below its reported 28.09 GiB high-load threshold. Thus these diagnostic runs did not show whole-machine CPU saturation or high-memory-load pressure. System CPU calculations subtract idle time from kernel-plus-user counters because [Windows includes idle time in its kernel counter](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes).

### Identity checks and reproduction

All twelve unprofiled batches completed and matched **144 saved MessagePack files** against the existing baseline by path and SHA-256. Both diagnostic batches added another 24 matching files: **168 identical files total**, comprising 84 permanent snapshots and 84 summary files across 42 completed work-item executions. The snapshots retain their existing generation-0/20 identities and contents. No algorithm, serialization, scheduling, or resource-policy code was changed during this investigation. The modified probe built without warnings and was exercised by both successful diagnostic runs.

All artifacts are in `artifacts/auto-serial-1024-20261127`: `measurements.json` and `summary.json` contain the twelve primary observations; `timelines.json` and `supplemental-timelines.json` record ordering and observed overlap; `diagnostics-summary.json` and `trace-*-auto` hold the GC/system counters; `verification-summary.json` records the output checks. `RunMatrix.ps1`, `SnapshotMap.fsx`, `AnalyzeMatrix.ps1`, `RunDiagnostics.ps1`, and `AnalyzeDiagnostics.ps1` preserve the orchestration and analysis. `interruption-note.txt` identifies the discarded partial attempt.

To reproduce an individual fresh trial, use the command from the preceding section with the same frozen candidate and `templates-1024`, select `-Memory 4GiB` or `6GiB` and `-Jobs Auto` or `1`, and choose a new output directory. Interleave three repetitions of the four conditions; keep diagnostic startup hooks unset for primary timing.

## Removing MSUF4 allocation churn (2026-11-27)

The roughly 1.24 TiB above is cumulative managed allocation traffic. Allocation stacks identify repeated MSUF4 stage expansion as the largest source, rather than the retained pool itself. A 30-second `dotnet-trace gc-verbose` capture of the unchanged 1,024-member workload collected 1,292,516 allocation samples representing 132.53 GiB, with no lost events or missing stacks. This is a startup/early-evolution sample, not an exact attribution of the whole batch:

| Sampled allocation path/type | Share |
| --- | ---: |
| MSUF4 stage expansion, inclusive | 81.78% |
| `twoOrbit.IsDisjoint`, included in expansion | 26.40% |
| Temporary integer lists, across all stacks | 39.52% |
| Integer-list enumerators | 7.60% |
| Integer set-tree nodes | 6.95% |

The inclusive paths overlap; the rows must not be added together. A separate complete 64-member evolution trace also placed 75.87% of its allocation samples in stage expansion and 24.36% in the disjoint check. The checks built lists and persistent sets to compare four integers, and unfolding repeatedly constructed two-element lists, copied transition-rate arrays, and zipped arrays into temporary tuples.

The production changes are deliberately local:

- Construct two-orbits directly from two integer endpoints, retaining the list-based API and the same validation/canonical ordering.
- Check overlap with four integer comparisons and bounds with the canonical upper endpoint.
- Use direct endpoint construction during reflection/unfolding and reuse the immutable original orbit for an unchanged Ortho branch.
- Use `Array.map2` instead of allocating a zipped array and tuples before mapping.
- Read immutable transition rates through their existing indexed accessor instead of copying the entire rates array for every stored stage.

No new cache, pool, memory-management policy, or production profiling dependency is introduced. Random draws, traversal order, evaluation rules, IDs, and persisted formats are preserved.

The isolated 64-member measurement (`MeasureSgdMemory.fsx`, compiled into a benchmark-only executable and run with each frozen build's assemblies) allocates 8,671,518,664 bytes before and 4,375,457,280 after, a 49.54% reduction. Both final snapshots have SHA-256 `13FF9C16D397A1821B68E299A2D54DDC9DF4CA993723542DD800522DB321DD1A`. Retained population memory is effectively unchanged. This small workload is useful for attribution; use the complete-job trials for performance conclusions.

### Complete-job measurements

Three alternating baseline/candidate pairs used the same three work items, two 1,024-member pools per item, 20 generations, 25% CPU, a 4 GiB soft target, Auto jobs, and no hard cap. Each fresh batch was timed alone, without the profiler, diagnostic hook, builds, or tests running alongside it.

| Build | Median elapsed (range) | Median CPU time | Median whole-job peak (range) |
| --- | ---: | ---: | ---: |
| Before | 218.03 s (211.58–220.45) | 956.72 s | 6.067 GiB (6.035–6.071) |
| After | 164.66 s (159.93–170.17) | 678.42 s | 5.892 GiB (5.360–6.046) |

Median elapsed time improved **24.48%**, and CPU time improved **29.09%**. Peak commitment improved only slightly at the median and still reached about 6 GiB. Reducing cumulative allocation traffic did not halve the resident population or the job's peak RAM requirement.

A separate complete candidate run with the existing 250 ms memory probe allocated **614.40 GiB**, compared with **1,272.82 GiB** in the preceding unchanged-build 4 GiB diagnostic: **51.73% less allocation traffic** (about 1.24 TiB down to 0.60 TiB). GC pauses were 34.189 seconds versus 63.435 seconds, and gen-0 collections were 39,074 versus 81,353. This diagnostic completed in 170.92 seconds and peaked at 6.050 GiB; its runtime is excluded from the paired medians.

### Correctness

All six timed batches and the complete candidate diagnostic matched all saved paths and SHA-256 hashes against the existing baseline: **84 file comparisons**, comprising 42 snapshots and 42 summaries across 21 completed work-item executions. `VerifyPoolSweep.fsx` also decoded all six permanent snapshots from a candidate batch, checked generations 0/20, and verified two pools with 1,024 members each.

The replay matrix still matches **108 snapshots and 108 summaries**, including checkpoint resume and unchanged earlier generations. Its manifest SHA-256 remains `32F03171562EF42B9A1717C70003FDDC20408621CBEE8306402572E8E5BE1513`. All **379** permutation, model, and evaluation tests pass, including an exhaustive comparison of the new disjoint check with set intersection over all pairs on sixteen channels. Original workspace data was untouched.

The normal Release solution build passes with only the existing unrelated xUnit2031 warning. All 29 runtime files checked in each of the CLI and WPF output directories match their frozen candidate counterparts by SHA-256. The measured baseline and candidate assemblies are unchanged; `verification-summary.json` and `release-assemblies.json` record these checks.

Whole-job artifacts are under `artifacts/allocation-churn-20261127`. `RunPaired.ps1` orchestrates the paired trials using `templates-1024`; `Analyze.ps1` produces `summary.json`. The baseline is the preceding soft-memory build; the candidate is frozen under `candidate/Release/net10.0`. Separate GC counters are under `diagnostic-trace`. Raw allocation traces, their analyzer source, stack/type summaries, assembly manifests, logs, and per-file hashes are preserved alongside them. The intentionally limited 30-second allocation capture is incomplete and excluded from timing and completed-output counts.

## Retained model versus V2 evaluation memory (2026-11-27)

This investigation adds benchmark-only heap inspection and two what-if prototypes. It does not change production algorithms, caches, serialization, or binaries. The reference is the frozen allocation-churn candidate above.

### Retained-memory measurement

`MeasureRetainedPopulation.fs` resumes the saved generation-zero population with the production evolution algorithm through generation 20. One representative work item at each size uses two pools, 16 channels, 895 stored stages per model, and OrthoRate 2.5. Seed models and evaluations from generation zero remain rooted to represent the contents retained by the shared preparation cache. This avoids reranking the 5,000-model source while reproducing the saved final population exactly.

`InspectRetainedHeap.cs` attaches to its own suspended child after a full diagnostic GC. It follows actual GC references and sums CLR object sizes, counting each address once; it does not call model getters or copy arrays while inspecting them. Models, evaluations, population metadata, and additional seed contents are counted in that order. These are unique reachable object graphs, not exclusive dominator sizes or process peak commitment. The approach follows the [ClrMD object-size example](https://github.com/microsoft/clrmd/blob/main/doc/GettingStarted.md).

| Members per pool (two pools) | Evolved model structures | V2 evaluations | Member/pool metadata | Total evolved population |
| ---: | ---: | ---: | ---: | ---: |
| 1,024 | 63.91 MiB | 5.82 MiB | 0.22 MiB | 69.95 MiB |
| 2,048 | 128.91 MiB | 11.76 MiB | 0.44 MiB | 141.11 MiB |

Models account for approximately **91.4%** and V2 evaluations **8.3%** of the evolved population. Existing parent/child sharing already helps substantially. Reloading the same generation-20 snapshots creates separate stage/step objects again: the model graphs alone grow to 419.94 and 839.88 MiB. This is loss of in-memory sharing during deserialization, not a change in model values.

The saved generation-zero roots approximate prepared seed contents; cache tuple/container wrappers and diagnostic root arrays are excluded. Separately deserialized subset-name strings add less than 0.15 MiB compared with sharing one name. Other benchmark-held populations can remain alive, so the reported `ProcessManagedBytes` must not be interpreted as the footprint of a proposed optimized application. The category graphs are the measurements used here.

### Prototype: share equal immutable stages

The prototype replaces equal `twoOrbitUf4` stage values with a shared immutable representative. It preserves model IDs, stage order, prefixes, member metadata, and every V2 evaluation. This extends existing sharing of unchanged ancestral objects to independently allocated objects with equal values. No packed model representation is introduced.

There are 1,832,960 / 3,665,920 stage positions in the two measured populations, but only **1,250 / 1,365 distinct stage values**. Including seed models raises the unique counts to just 1,570 / 1,716. A conventionally formed 16-channel stage has seven ternary choices (one seed, two choices in the first unfolding step, four in the second), giving at most **3^7 = 2,187 values**. This permits a bounded table rather than an unbounded cache of whole models.

| Retained graph | 1,024 per pool: current → shared | 2,048 per pool: current → shared | Reduction |
| --- | ---: | ---: | ---: |
| Evolved generation-20 population | 69.95 → 20.68 MiB | 141.11 → 41.25 MiB | 70–71% |
| Population reloaded from its snapshot | 426.05 → 20.78 MiB | 852.21 → 41.42 MiB | 95% |
| Evolved population plus retained seed contents | 482.65 → 42.40 MiB | 966.18 → 85.09 MiB | 91% |

The combined row deduplicates references shared between seeds and survivors. Applying sharing only after evolution would miss the large seed footprint. A production version should cover model loading and mutation as well as retained seeds. It should preserve the current path for unsupported shapes and larger channel counts: the analogous 32-channel space has 3^15 values, so eagerly extending this table to every width is inappropriate.

### Execution-time measurement

`MeasureStageExpansionCache.fs` also caches the ordered comparator sequence produced by each canonical stage, using the unchanged production expansion routine to populate the table. It then calls the existing materialization/identity implementation. This directly avoids repeatedly unfolding and validating the same stage values.

Each measurement materializes 512 models selected from the relevant saved population, with eight workers and the same 25% CPU cap. Three alternating baseline/prototype trials are summarized by medians. No other benchmark, build, or profiler ran alongside them. Cache construction, population loading, value deduplication, evaluation, and serialization are outside these timers. Public array-getter copies in the prototype remain included.

| Source pool size | Materialization | Current → cached median | Speedup | Managed allocations per trial |
| ---: | --- | ---: | ---: | ---: |
| 1,024 | Full suffix | 0.288 → 0.052 s | 5.53× | 2,089.4 → 257.0 MiB |
| 1,024 | Adaptive cutoff | 0.431 → 0.070 s | 6.16× | 1,722.8 → 259.6 MiB |
| 2,048 | Full suffix | 0.340 → 0.054 s | 6.28× | 2,089.3 → 257.0 MiB |
| 2,048 | Adaptive cutoff | 0.235 → 0.063 s | 3.70× | 1,727.3 → 260.1 MiB |

Adaptive cutoffs are 5,716 and 5,735 raw comparators. Cache construction took 0.035 / 0.044 seconds for 970 / 1,036 stage entries used by the selected 512 models. Its 7.9 / 8.2 million allocated bytes are construction traffic, not retained cache size. These short component trials show a promising direction, not a whole-job speedup estimate. The 85–88% allocation reduction applies only to sorter materialization. No new complete-job peak or runtime was measured in this investigation.

### Results and snapshot identity

At both population sizes, the unchanged evolution resumed from generation zero produces the expected generation-20 compressed snapshot payload exactly. Canonical evolved, canonical evolved-plus-seeds, loaded, and canonical loaded populations all produce that same payload. The complete saved snapshot envelope was also reconstructed and compared byte-for-byte for the canonical loaded generation-20 snapshot at both sizes; all five 1,024-member variants received this additional full-file check. These checks preserve every serialized member, model, and evaluation field.

The expansion prototype compares sorter IDs and ordered comparator arrays for 512 models at seven cutoffs: full, 0, 1, 7, 8, 9, and the adaptive cutoff. **All 7,168 comparisons across the two population sizes match.** This verifies the prototype's materialized inputs; production integration still needs the full replay/saved-summary suite and complete-job benchmarks before claiming an application improvement.

The benchmark projects built with no warnings or errors. Final SHA-256 checks confirm that all 119 frozen reference files and the 29 previously recorded runtime files in each normal CLI/WPF output remain unchanged. `final-verification.json` records these checks. Only benchmark source and documentation were added or edited for this investigation.

| Members per pool | Identical generation-20 compressed payload SHA-256 |
| ---: | --- |
| 1,024 | `B450DB9F23A554B5A05FDA5DAEF31FEB6BAC9E9A9F2B854368565125B0B7F791` |
| 2,048 | `097250F43E97E96D67175CD8D7E79902099C1C06C7B1E87178A357DA5A516A44` |

### Recommended order

1. **Share canonical 16-channel stages and reuse their comparator expansions.** This targets the largest measured retained graph and the already-profiled expansion hot path together. Use a bounded, private immutable table; retain RNG draws, stage ordering, identity calculation, and existing DTO serialization. Start with this localized change before redesigning models or selection.
2. **Keep all V2 detail.** Removing V2 would save at most 5.82 / 11.76 MiB from these evolved populations before accounting for replacement statistics. Reconstructing the detail for identical snapshots adds work and complexity. A smaller optional follow-up is making `ceUse` a value record: its four integer fields would occupy 16 inline bytes, versus a measured 32-byte object plus an 8-byte array reference. The projected saving is **3.34 / 6.76 MiB** per current two-pool population. That estimate is not an implemented or timed change; preserve the existing DTO representation and verify it separately.
3. **Defer winner reconstruction, wholesale flattening, and general buffer pooling.** The two outer member arrays together occupy only 16.05 / 32.05 KiB. Pooling those arrays cannot materially address this footprint. Reconstructing winners would trade computation and more selection-sensitive code for memory that stage sharing addresses more directly.

The graph reductions above are not predictions that the previous roughly 6 GiB whole-job peak will fall by 91%. Decoded DTO graphs, serialization buffers, evaluation scratch, uncollected garbage, and native/runtime memory are outside these retained-population figures. Integrate the bounded stage change first, then rerun full jobs to measure the resulting peak and elapsed time.

### Artifacts and reproduction

Reports are in `artifacts/retained-memory-20261127`: `retained-summary.json`, `expansion-summary.json`, the per-size category/type censuses and verification files, and the raw expansion trials. The `roots`, `target`, `inspector`, and `expansion` subdirectories contain benchmark-only project files and executables referencing frozen production assemblies. ClrMD is supplied by a local Microsoft `dotnet-dump` 10.0.750501 installation; it is not a production dependency.

After building those artifact projects in Release, run the inspector against a completed project copy, keeping profiling separate from timing:

```powershell
& artifacts/retained-memory-20261127/inspector/bin/Release/net10.0/Inspect.exe `
  C:/source/Sortopia/artifacts/retained-memory-20261127/target/bin/Release/net10.0/RetainedTarget.exe `
  C:/source/Sortopia/artifacts/allocation-churn-20261127/paired-4gib/larger-population-1-dynamic `
  C:/source/Sortopia/artifacts/new-retained-1024

& artifacts/retained-memory-20261127/expansion/bin/Release/net10.0/Expansion.exe `
  C:/source/Sortopia/artifacts/allocation-churn-20261127/paired-4gib/larger-population-1-dynamic `
  C:/source/Sortopia/artifacts/new-expansion-1024.json
```

For 2,048 members, use the completed project at `artifacts/soft-memory-20261127/members-2048-4gib/larger-population-1-dynamic` and fresh report destinations. Input snapshots are read-only. Full collections and heap-attachment pauses belong only to these diagnostics.

### Target-workload clarification: MSRS at 24, 32, and 64 channels

The intended future workload is MSRS at these larger widths. The preceding measurements describe 16-channel MSUF4; neither its 91% retained-memory reduction nor its small finite stage table is a forecast for MSRS. The MSUF4-specific table is therefore no longer the first recommended production change for that workload.

Source inspection identifies simpler MSRS opportunities to measure first:

- `Msrs.MakeSuffix` currently expands every stored stage with `Array.collect` and only then truncates to the requested CE cutoff. Stop expansion once the requested suffix is complete, preserving exact comparator order and the full fixed prefix. This targets allocation traffic and execution time, with savings dependent on the actual cutoff.
- `PermRsOps.mutatePerm_Rs` currently constructs a new permutation even when the selected action matches the existing pair type and leaves the mapping unchanged. Return the existing immutable stage in those cases, after consuming exactly the same random draws. `MsrsRandMutate` already shares stages for explicit `NoAction`; extend that benefit to these unchanged mutations. This can reduce both retained duplication and allocation traffic, without a stage cache.
- The mutation path uses `Array.copy pRs.Array` although the getter already returns a copy. `findRsPoints` takes another full-array copy to read a few indices. Remove the redundant copy and use existing indexed reads where possible, while preserving mutation selection and random consumption. Comparator expansion also passes through a visited array, lists, and two-orbit objects; direct ordered CE output is a further local candidate.

Keep V2 detail. The proposed `ceUse` value-record optimization applies to MSRS too, but its share of retained memory must be measured again at these widths. If retained permutation arrays remain dominant after the local changes, compact endpoint storage is worth a separate prototype; its wider representation/API impact makes it a later step. Do not extrapolate the MSUF4 model/evaluation split to MSRS.

These are code-backed opportunities, not newly measured MSRS savings. Follow-up benchmarks should use representative MSRS parameters and inputs at 24/32/64 channels, compare retained models against V2 evaluations, and verify complete results and snapshot bytes against an unchanged baseline. No production changes were made during this clarification.

### Large test sets are a separate benchmark dimension

The intended 32/64-channel workloads cannot use exhaustive full binary input sets, and even prefix-filtered sets can be large. The retained-population census above excluded test inputs and evaluation scratch. Its percentages must not be treated as a whole-job RAM breakdown or as evidence that model changes dominate larger-input runtimes.

Measure input count, channel count, representation, source sharing, candidate batch size, and active workers independently of pool size. BitVector512 payload storage is approximately `ceil(testCount / 512) * channelCount * 64` bytes before block metadata and array headers. For example, 100 million 64-channel inputs require about 763 MiB of packed vector payload alone. Boolean arrays, DTOs, conversion buffers, and additional evaluation views can be much larger. This is a size calculation, not a benchmark or a claim that the current file format can hold that example.

The source audit found:

- Candidate batching at 128 limits expanded networks, not resident test-set size. Typed test datasets and the current codec still load entire datasets into arrays.
- With large BitVector512 inputs and multiple workers, evaluation normally parallelizes over 512-input blocks. Each active loop state reuses one width-sized vector buffer (4 KiB at 64 channels) and keeps CE-use counters for the candidate batch. Counter payload scales approximately as `4 * sum(candidateCeCounts)` bytes per loop state. This path does not allocate a whole-dataset mutable buffer for each candidate or worker.
- The alternate network-parallel path takes a defensive copy of every input block's vectors per evaluation invocation. It is selected for one worker even on a large dataset. That is one additional dataset-sized copy shared by the invocation, not one copy per worker. The block-parallel path instead creates a source-vector copy for each processed block, which contributes roughly a dataset's vector payload in allocation traffic per candidate batch despite bounded live block scratch.
- Boolean inputs are packed during both seed preparation and evolution initialization when SIMD is available. The packed evaluation view is not held in the shared preparation cache, so multiple work items can prepare separate copies while the original shared Boolean dataset remains rooted.
- `MsasPfx.MakeSortableBitv512Test` first materializes all prefix-result Boolean arrays, then packs them. This creates an avoidable large Boolean intermediate before evaluation even starts.
- Scheduling estimates include observed process commitment and a serialized-size hint for unshared inputs, but the population allowance remains calibrated to 16-channel sweeps. It does not explicitly model converted input views, evaluator-path copies, or per-worker CE counters. The memory target remains soft.
- The current storage codec limits each payload to 1 GiB uncompressed and the compressed envelope to 256 MiB. Materializing DTOs and payload buffers adds a separate loading/saving peak. Reducing evaluation scratch does not remove these storage limits.

For the intended workload, prioritize generating/packing prefix inputs in bounded chunks, sharing a single immutable prepared evaluation view where lifetimes permit, and removing dataset-sized defensive copies without exposing mutable input storage. Keep worker scratch bounded and account for input representation and CE-counter memory when choosing concurrency. If the packed dataset itself cannot fit, chunked input storage and evaluation need a separate design; candidate batching alone cannot solve that case. Such a design must preserve test identities, order and multiplicity, exact accumulated CE/failure counts, model selection, and snapshot bytes. Existing count fields are 32-bit integers, so input counts beyond their range also require an explicit compatibility decision.

Future benchmark pairs must use the same fixed prefix-filtered or explicitly sampled test fixtures, including duplicates, at each selected input count. Do not generate exhaustive 32/64-channel datasets or silently downsample an existing run. Record preparation/load peak, resident input and population graphs, evaluation peak, allocation traffic, elapsed time, and complete saved-output identity. Larger-input evaluation time may dominate sorter materialization; the earlier materialization-only speedups cannot predict that workload's elapsed-time gains.

## Bounded MSRS input preparation and copy removal (2026-11-27)

The production implementation now packs an input sequence while retaining at most 512 Boolean rows. `MsasPfx.MakeSortableBitv512Test` streams into that packer instead of materializing all Boolean rows first. Independent prefix components are combined in their existing first-occurrence order without sorting the complete Cartesian product. Each component's local image/deduplication data remains resident; this is not a constant-memory algorithm for arbitrary connected prefixes.

SGD prepares one packed evaluation view for a direct run. Batch execution shares that view through the existing preparation cache for common input references, and can release the original shared Boolean representation after preparation for a fixed batch. Distinct datasets in a sweep are not all retained in that cache. The existing live-queue input-retention policy is unchanged.

The evaluator copies input vectors directly into reusable worker scratch. The network-parallel path no longer clones the entire packed test set, and the count-only block path reuses a source buffer and a work buffer rather than allocating a source vector array per block. Those two buffers occupy 8 KiB per active worker at 64 channels, excluding comparator counters. Public defensive-copy getters remain intact. The original dynamic scheduling is retained. An intermediate fixed-partition experiment reduced counter-allocation traffic but produced slower trials; it was removed rather than introducing another scheduling policy.

The final packed dataset still resides in memory. These changes do not implement chunked on-disk evaluation, remove codec loading buffers, change the existing 1 GiB raw / 256 MiB compressed payload limits, or alter count-field ranges. No memory-cap policy, model representation, selection rule, random sequence, evaluation detail, or persistence schema changed in this implementation.

### Workload and measurement scope

`MeasureMsrsInputs.fs` is compiled against separately frozen baseline and candidate assemblies. Each run uses MSRS, two pools of 64 members, three evolved generations plus generation zero, 128 stored suffix stages, one child per parent, Random initial selection with seed 7, LCG/V2 mutation, and eight evaluation workers. Mutation rate is 0.1 with operation weights 0.3/0.3/0.4; duplicate-hash filtering and per-generation cutoff adjustment are enabled. Both builds use a 25% Windows CPU cap, server GC with `DOTNET_PROCESSOR_COUNT=8`, and no hard memory cap. These are input-size benchmarks, not repetitions of the earlier 1,024-/2,048-member experiments.

All fixtures use an adjacent-pair prefix. At 24 channels, all 531,441 distinct prefix images are generated through the production prefix enumerator. At 32 and 64 channels, the fixture contains 4,194,307 distinct, deterministically selected prefix images. A coprime stride through the ternary pair-state space distributes values across every channel; an untimed coverage check verifies each channel has between 20% and 80% ones. The 64-channel fixture uses version 2, which corrects a biased early pilot. Neither larger-width fixture is an exhaustive filtered universe or a full binary test set. No user dataset was downsampled. All three counts include a partial final SIMD block.

The primary timer covers input generation/packing, model generation, evolution, and saving actual FileDb snapshots and summaries. It excludes the later input/output hash audit. These are production-core measurements, not a full CLI workspace/preflight benchmark. A separate saved-input comparison includes production FileDb decoding before the same evolution and output saving. No other benchmark, build, test, or profiler launched for this task ran alongside timed trials.

### Generated-input results

Two alternating baseline/candidate pairs per width, summarized by medians (the midpoint for two trials). Peak working set measures resident RAM; peak committed memory is a separate Windows job measurement. Each entry is **baseline → final implementation**.

| Channels | Filtered inputs | Runtime median | Runtime range | Peak resident RAM | Peak committed memory |
| ---: | ---: | ---: | --- | ---: | ---: |
| 24 | 531,441 | 4.93 → 4.71 s | 4.74–5.13 → 4.50–4.92 s | 234.4 → 136.2 MiB | 233.0 → 114.3 MiB |
| 32 | 4,194,307 | 34.96 → 33.67 s | 33.79–36.13 → 33.12–34.21 s | 818.5 → 208.0 MiB | 903.2 → 193.5 MiB |
| 64 | 4,194,307 | 101.83 → 91.12 s | 67.98–135.67 → 71.07–111.16 s | 1,097.4 → 350.6 MiB | 1,181.9 → 351.0 MiB |

Median peak resident RAM fell by approximately **42%, 75%, and 68%**. Maximum observed candidate commitment was 114.7 / 193.8 / 361.6 MiB respectively. Median preparation time fell from 0.64 / 1.23 / 1.72 seconds to 0.49 / 0.80 / 1.10 seconds. Most elapsed time remains evaluation and model work, so preparation improvements alone cannot imply a large end-to-end speedup.

Runtime was highly variable at 64 channels, including a twofold variation in the unchanged baseline. Machine-wide conditions were not controlled, and no cause for that variation was established. The lower median is not evidence of a dependable 10.5% speedup. The consistent finding is lower peak memory. Median managed allocation traffic was 1.44 → 1.49 GiB at 24 channels, 3.39 → 2.79 GiB at 32, and 6.57 → 6.07 GiB at 64: lower live memory does not necessarily mean lower total allocation in every case.

### Loading saved packed inputs

One additional baseline/candidate pair per width loads the same baseline-generated packed MessagePack input file, then runs the same model generation, evolution, and saving. These single pairs are supplemental measurements, not stable runtime estimates.

| Channels | Runtime | Peak resident RAM | Peak committed memory |
| ---: | ---: | ---: | ---: |
| 24 | 6.52 → 6.59 s | 135.3 → 138.4 MiB | 113.1 → 125.8 MiB |
| 32 | 60.39 → 62.49 s | 237.7 → 226.5 MiB | 229.1 → 217.4 MiB |
| 64 | 118.81 → 116.11 s | 472.4 → 407.2 MiB | 502.5 → 418.3 MiB |

The large generated-input savings must not be presented as the saving for an existing packed-input run. Here resident memory is essentially unchanged at 24 channels and falls by about 5% / 14% at 32 / 64. Timings differ by only approximately 1–3.5%, with both increases and decreases. Loading saved Boolean datasets was not separately benchmarked; decoding their original Boolean object graph can still dominate a job's peak before conversion. Sharing prepared inputs across multiple work items is covered by execution tests but is not quantified by this single-job harness.

### Identity and validation

The 12 primary runs and six loaded-input runs produced 144 actual MessagePack snapshot/summary files. All were rehashed after benchmarking. Comparing every run to its width's first baseline gives **120 identical complete-file comparisons**, preserving the envelope as well as the payload. The three independently generated saved packed-input files also match byte-for-byte. Input IDs, ordered vector contents, active-lane counts, and full-file hashes are recorded per run.

`VerifySgdReplay.fsx` additionally compares 108 snapshots and 108 summaries across MSCE/MSRS/MSUF4, both mutator variants, selection/duplicate settings, worker counts, checkpoint resume, and immutability of earlier generations. The baseline and final replay manifests have identical SHA-256 `32F03171562EF42B9A1717C70003FDDC20408621CBEE8306402572E8E5BE1513`.

All 83 Sorting, 91 Evaluation, and 250 FileDb tests pass. New coverage checks streaming pack order/duplicates/partial blocks/disposal, randomized prefix order against brute force, lazy consumption of a huge 62-channel prefix image, and execution after prepared source files become unavailable. The WPF suite passed 1,467 tests and exposed one stale assertion for the earlier soft-memory-policy log wording; that assertion was updated and its terminal-handoff test passed on rerun. Release solution and benchmark builds succeeded; the solution retains one unrelated xUnit2031 warning in `WorkspaceImportFunctionTests.cs`.

Final manifests verify the frozen baseline and candidate files, all 82 normal CLI output files against the measured candidate, and 19 shared WPF assemblies against that candidate. The report is `artifacts/msrs-inputs-20261127/final-verification.json`. Original `C:/GuiSpace` data was not modified.

### Artifacts and reproduction

The final measurements are `artifacts/msrs-inputs-20261127/final-dynamic-pairs/measurements.json` and `final-loaded-pairs/measurements.json`; their summaries are `final-dynamic-summary.json` and `final-loaded-summary.json`. Frozen builds, harness projects, packed fixtures, raw stdout/stderr, saved outputs, and replay manifests are retained in the same artifact root. `measurement-notes.txt` identifies excluded pilots and superseded scheduling experiments. Earlier `final-paired-8-workers` and `loaded-pairs` folders measured the discarded fixed-partition version and must not be substituted for the final results.

With the frozen harness projects built in Release, use fresh destinations:

```powershell
./benchmarks/BenchmarkMsrsInputs.ps1 -BuildRoot artifacts/msrs-inputs-20261127 `
  -Output artifacts/msrs-inputs-new-generated -Repetitions 2

./benchmarks/BenchmarkMsrsInputs.ps1 -BuildRoot artifacts/msrs-inputs-20261127 `
  -Output artifacts/msrs-inputs-new-loaded -Repetitions 1 -Mode load-evolve `
  -InputFixtures artifacts/msrs-inputs-20261127/fixtures

./benchmarks/AnalyzeMsrsInputs.ps1 `
  -Measurements artifacts/msrs-inputs-new-generated/measurements.json `
  -Output artifacts/msrs-inputs-new-generated/summary.json
```

`MeasureMsrsInputs.fs` also has a `fixture` mode for generating the saved inputs. Its arguments are channel count, test count, workers, members per pool, generations, suffix stages, mode, and a fresh output directory. A loaded-input invocation adds the fixture directory as its final argument. Use the identical workload arguments when generating and loading a fixture, since they form its deterministic identity.

## Compact evolution evaluations and MSRS stages (2026-11-27)

Evolution now constructs and retains V1 evaluations directly, including prefix-aware metrics. It does not construct a temporary V2 evaluation and then discard its comparator-use records. The evaluator still needs temporary integer usage counters to calculate exact used-comparator statistics. Seed preparation, children, parent reevaluation, and resumed populations all use V1. New `SorterPoolSet` checkpoints contain V1; loading an old V2 checkpoint converts its evaluations in memory. Existing checkpoint files are not rewritten automatically. The DTO version remains 2 because V1 was already a supported evaluation variant.

`StandardSgd.evaluateMemberDetails` explicitly reevaluates a selected member against the original test inputs and returns V2 detail without attaching it to the population. It reconstructs through the saved last-used comparator, preserving the original evaluated-sorter identity even if the pool cutoff subsequently changed, and rejects a mismatch in any saved V1 field. This is a library API, not an automatically invoked inspector action. On a large test set, requesting detail still costs an evaluation.

MSRS expansion now stops at the requested suffix cutoff. Mutation avoids a redundant permutation-array copy, reads mapping entries without copying an entire permutation, and shares an unchanged immutable stage after consuming the same random choices. Public defensive-copy boundaries remain intact. These changes preserve comparator order, random consumption, model/member IDs, mutation cursors, selection ordering, and cutoff behavior. No flat model representation, reconstruction of winning children, new scheduler, or general-purpose buffer pool was added.

### Benchmark scope and separate build comparisons

`MeasureCompactEvolution.fs` compares three frozen builds: **baseline** is the implementation after the preceding bounded-input work, **V1** adds only compact evolution/checkpoints and explicit detail reconstruction, and **combined** also adds the MSRS changes. Each width has two trials per build, ordered baseline/V1/combined and then combined/V1/baseline. Trials are sequential; no build, test, other benchmark, or profiler launched for this task ran alongside them.

The workload loads the existing packed fixtures described above: 531,441 filtered inputs at 24 channels and 4,194,307 at both 32 and 64. The larger fixtures are fixed subsets of prefix images, not exhaustive filtered universes. All channels are exercised, and all counts include a partial final SIMD block. Each trial has two pools of 64 members, 128 stored suffix stages, three evolved generations plus generation zero, one child per parent, and eight workers. The mutation/selection settings match the preceding input benchmark. All builds use server GC with eight reported processors, a 25% Windows CPU cap, and no hard memory cap. Generation-zero seed contents are explicitly kept alive in all builds.

Timing covers FileDb input loading, model generation, evolution, and four actual checkpoint/summary saves. Hash audits, selected-member detail reconstruction, and heap inspection are outside the measured interval. These are production-core measurements with 128 total members, not a full CLI sweep or a rerun of the earlier 1,024-/2,048-member experiments. All values below are medians, with the midpoint used for two trials.

| Channels | Build | Runtime (range), seconds | Peak resident MiB | Peak committed MiB | Allocated GiB | Four checkpoints, MiB |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 24 | Baseline | 4.03 (4.03–4.04) | 146.8 | 126.4 | 1.10 | 2.182 |
| 24 | V1 | 4.42 (4.19–4.65) | 133.4 | 107.7 | 0.85 | 1.423 |
| 24 | Combined | 3.72 (3.66–3.77) | 124.6 | 101.0 | 0.94 | 1.423 |
| 32 | Baseline | 35.59 (31.86–39.33) | 243.9 | 229.0 | 1.84 | 3.405 |
| 32 | V1 | 32.55 (31.96–33.14) | 225.1 | 204.0 | 1.75 | 1.978 |
| 32 | Combined | 34.10 (33.27–34.92) | 220.8 | 200.4 | 1.77 | 1.978 |
| 64 | Baseline | 65.02 (63.58–66.47) | 461.7 | 450.8 | 5.22 | 8.316 |
| 64 | V1 | 73.09 (66.43–79.75) | 556.3 | 545.8 | 4.89 | 4.291 |
| 64 | Combined | 65.82 (63.76–67.87) | 461.0 | 449.1 | 4.64 | 4.291 |

Combined median peak resident RAM fell 15.2% at 24 channels and 9.5% at 32; at 64 it was essentially unchanged. The V1-only build actually had a higher whole-process peak at 64, despite smaller live evaluations. This illustrates why retained-object savings cannot be used as a prediction of process peak RAM. The combined build reduced median allocation traffic by 14.2%, 3.8%, and 11.2%, but allocation traffic varied between identical trials, particularly at larger input sizes. Dynamic parallel evaluator state and GC behavior remain in place; this experiment does not isolate the cause of that variation.

The 24-channel combined runtime was lower in both trials. At 32 and 64 the baseline and combined ranges overlap, and there is no dependable end-to-end speedup established by these two-trial comparisons. The 64-channel median was 1.2% slower. The V1-only 64-channel result was also slower; the combined build returned it to approximately baseline runtime. No claim of a universal speedup is warranted. Checkpoint savings are deterministic: **34.8%, 41.9%, and 48.4%** at 24, 32, and 64 channels respectively.

### Retained models versus evaluations

Six separate runs use an external ClrMD heap walk at the final live generation, after collection, deduplicating shared object addresses. These runs are excluded from the timing table. The table compares baseline with the combined implementation. Model/evaluation columns cover only the final population; the last column also includes additional contents retained by generation-zero seeds, without counting shared objects twice. Input data, evaluator scratch, serializer buffers, and other process objects are outside these population totals.

| Channels | Final model graph, MiB | Final evaluations, MiB | Entire final population, MiB | Population plus seed contents, MiB |
| ---: | ---: | ---: | ---: | ---: |
| 24 | 1.004 → 0.928 | 1.043 → 0.0127 | 2.060 → 0.955 | 5.503 → 3.358 |
| 32 | 1.236 → 1.144 | 1.665 → 0.0127 | 2.915 → 1.170 | 7.330 → 3.896 |
| 64 | 1.731 → 1.586 | 4.056 → 0.0127 | 5.801 → 1.612 | 14.462 → 6.044 |

For these 128-member MSRS populations, V2 evaluations occupied about 51%, 57%, and 70% of the retained final graph. V1 evaluation storage is 13,348 bytes at each width, a 98.8–99.7% reduction in that component. The whole retained final population falls by approximately 54%, 60%, and 72%. Unchanged-stage sharing reduces the final model graph by about 7–8%. These are measurements for this population, mutation history, and model length, not a claim that every larger pool or longer run will have the same proportions. They also do not imply equivalent reductions in whole-job peak RAM: the 64-channel timing experiment demonstrates that distinction.

### Selection buffers and remaining work

The heap harness separately runs an allocation-only probe over ranking, retention, cutoff adjustment, and pruning, using repeated final models with unique member IDs. It retains duplicates to exercise a full second sort and averages 100 passes. At 64 members per pool / 128 candidates it allocates approximately 0.31–0.32 MiB per pass across the three widths. Synthetic 1,024-member and 2,048-member probes allocate about 8.9–9.7 MiB and 20.1–21.8 MiB respectively. These totals include sorting keys/tuples and temporary records, not just array backing storage. They are not full large-population benchmarks or measured savings from pooling.

At the measured 64-member pool size, even six such selection passes represent only roughly 2 MiB against whole-run allocation traffic of 0.94–4.64 GiB. This does not justify introducing reusable reference-buffer ownership and clearing rules now. General buffer pooling is deferred; revisit it if a larger-population profile makes ranking a material cost. Candidate generation/evaluation remains bounded in batches of 128, while surviving and losing candidate models remain available through selection. Reconstructing only winning children or flattening all model structures remains a separate, more complex optimization. The retained-model graph is now the main population component, but the packed test set and execution scratch still need consideration before choosing job concurrency.

### Identity, resume, and tests

All 18 primary runs and six heap runs agree on input identity and ordered contents, every saved checkpoint after normalizing evaluation variants to V1, byte-identical summary files, and full V2 detail reconstructed for selected members at generations zero and three. The primary comparison covers 60 normalized snapshot comparisons, 60 complete summary-file comparisons, and 120 selected-detail comparisons against each width's first baseline. All 192 saved checkpoint/summary files and three existing fixture files were rehashed after the runs. V1-only and combined checkpoint files are byte-identical to each other. They intentionally differ from old V2 checkpoint bytes by omitting detailed comparator usage.

`VerifyCompactSgdReplay.fsx` additionally checks 108 snapshots and 108 summaries across MSCE, MSRS, and MSUF4, both mutation variants, selection/duplicate settings, worker counts, preservation of earlier generations, and checkpoint resume. Both candidate builds also resume the baseline's actual V2 checkpoint files. The three replay manifests have identical SHA-256 `8C7A287DE8D81B69CE10E394D7D43B61F38ABAADBF68EB96F70D83EADE1D549A`.

Release solution build succeeded. All 199 Models, 95 Evaluation, 93 Permutations, 250 FileDb, and 1,468 WPF tests pass (2,105 total). New tests compare direct V1 with V2-to-V1 metrics for scalar/SIMD inputs, prefixes, cutoffs, duplicate inputs, and a partial SIMD block; verify detail reconstruction and old checkpoint conversion; preserve MSRS comparator order at 24/32/64 channels; and verify unchanged-stage reuse consumes the original random choices. The solution retains one unrelated xUnit2031 warning in `WorkspaceImportFunctionTests.cs`.

The final audit verifies 82 files in each of the three frozen builds, 82 normal CLI output files against the combined build, and 19 shared WPF assemblies against it. Original `C:/GuiSpace` data was not modified.

### Artifacts and reproduction

`artifacts/v1-msrs-20261127` contains the three frozen CLI builds and harness projects, `paired/measurements.json`, `paired-summary.json`, `heap-summary.json`, individual heap censuses and selection probes, actual saved outputs, replay manifests/old checkpoints, test logs, and `final-verification.json`. `verify-final.ps1` rechecks all file/build hashes and semantic comparison manifests. The previous task's packed fixtures remain under `artifacts/msrs-inputs-20261127/fixtures`.

With the three frozen harnesses built, use a fresh output directory:

```powershell
./benchmarks/BenchmarkCompactEvolution.ps1 `
  -BuildRoot artifacts/v1-msrs-20261127 `
  -Fixtures artifacts/msrs-inputs-20261127/fixtures `
  -Output artifacts/compact-evolution-new -Repetitions 2

./benchmarks/AnalyzeCompactEvolution.ps1 `
  -Measurements artifacts/compact-evolution-new/measurements.json `
  -Output artifacts/compact-evolution-new/summary.json
```

The harness accepts width, input count, workers, members per pool, generations, stored stages, mode (`measure` or `heap`), fresh output directory, and fixture directory. Workload arguments must match the fixture identity. `InspectRetainedHeap.cs` accepts an optional fifth argument naming a JSON string-array file of child arguments; omitting it preserves its earlier invocation format. Heap mode requires that inspector and is intentionally separate from timed measurements.
