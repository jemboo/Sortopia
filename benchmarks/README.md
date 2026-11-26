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

The three scenarios exercise a fresh source-by-OrthoRate grid, four files with unequal generation targets, and four selected files with three already complete. The last scenario creates its completed results outside the timed trials. Each CLI process enforces the same CPU percentage and committed-memory cap, uses Auto parallel files, and includes startup, preflight, shared preparation, evolution and checkpoint publication in its elapsed time. Trials alternate build order. The harness records process CPU time and samples private memory every 25 ms; this sampling is confined to the benchmark harness. Every persisted result is SHA-256 checked against the first trial of that scenario, including snapshots and summary sets.

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

## Automatic worker and GC policy

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
