// Benchmark-only what-if: reuse comparator expansion of canonical immutable stages.
// Reports materialization timings, not whole-job speedups. Production is unchanged.
module Sortopia.MeasureStageExpansionCache

open System
open System.IO
open System.Diagnostics
open System.Reflection
open System.Collections.Generic
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.FileDb
open Sortopia.Functions
open Sortopia.Runs

[<EntryPoint>]
let main args =
    let folder, destination = Path.GetFullPath args[0], Path.GetFullPath args[1]
    if File.Exists destination then failwith "Use a new output path."
    let flags = BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic
    let apply = Assembly.Load("sortopia").GetType("Sortopia.Cli.WindowsExecutionLimits", true).GetMethod("apply", flags)
    use limits = apply.Invoke(null, [| box 25; box 0L |]) :?> IDisposable
    let item = RunParameterFiles.listWorkItems (Path.Combine(folder, "runParameters")) CancellationToken.None
               |> Array.find (fun item -> item.Parameters.TryFind ParamKeys.orthoRateKey = Some 2.5)
    let canonical, _ = RunOutputFiles.loadSnapshot folder item 20 CancellationToken.None
                       |> Sortopia.MeasureRetainedPopulation.canonicalize
    let snapshotHash = Sortopia.MeasureRetainedPopulation.verifyPayload folder item canonical
    let models = canonical.Pools |> Array.collect _.Members |> Array.take 512 |> Array.map (fun m ->
        match m.Model with
        | sorterModel.Simple (simpleSorterModel.Msuf4 model) -> model
        | _ -> failwith "Expected MSUF4.")
    let cache = Dictionary<twoOrbitUf4, ce array>(HashIdentity.Reference)
    let allocatedBefore = GC.GetTotalAllocatedBytes(true)
    let buildTimer = Stopwatch.StartNew()
    for model in models do
        for stage in model.TwoOrbitUnfolder4s do
            if not (cache.ContainsKey stage) then
                cache.Add(stage, stage.MakePerm_Si |> PermSi.getTwoOrbits |> Array.map Ce.fromTwoOrbit)
    buildTimer.Stop()
    let buildAllocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore
    // Invoke the unchanged production identity/materialization implementation.
    let materialize = typeof<msuf4>.Assembly.GetType("Sortopia.Models.Sorting.Simple.ModelPrefix", true)
                          .GetMethod("materialize", flags)
                          .CreateDelegate<Func<Guid, int, ce array, ce array, sorter>>()
    let cachedMakeSorter maxLength (model: msuf4) =
        let stages = model.TwoOrbitUnfolder4s
        let expand stage = cache[stage]
        let suffix =
            match maxLength with
            | None -> stages |> Array.collect expand
            | Some length -> seq { for stage in stages do yield! expand stage } |> Seq.truncate (int length) |> Seq.toArray
        materialize.Invoke(%model.Id, int model.ChannelCount, model.Prefix, suffix)
    let adaptive = canonical.Pools |> Array.maxBy _.RawCeLength |> fun p -> int (float p.RawCeLength * 1.1)
    let cases = [| "full", None; "adaptive", Some (UMX.tag<ceLength> adaptive) |]
    let mutable checkedSorters = 0
    for limit in [None; Some 0<ceLength>; Some 1<ceLength>; Some 7<ceLength>; Some 8<ceLength>; Some 9<ceLength>; Some (UMX.tag<ceLength> adaptive)] do
        for model in models do
            let expected, actual = model.MakeSorter limit, cachedMakeSorter limit model
            if expected.SorterId <> actual.SorterId || expected.Ces <> actual.Ces then failwith "Materialized sorter differs."
            checkedSorters <- checkedSorters + 1
    let options = ParallelOptions(MaxDegreeOfParallelism = 8)
    use currentProcess = Process.GetCurrentProcess()
    let rows = ResizeArray<_>()
    for name, limit in cases do
        for round in 1 .. 3 do
            for cached in (if round % 2 = 1 then [false; true] else [true; false]) do
                GC.Collect(2, GCCollectionMode.Forced, true, true)
                let cpu = currentProcess.TotalProcessorTime
                let allocated = GC.GetTotalAllocatedBytes(true)
                let results = Array.zeroCreate<sorter> models.Length
                let clock = Stopwatch.StartNew()
                Parallel.For(0, models.Length, options, fun index ->
                    results[index] <- if cached then cachedMakeSorter limit models[index] else models[index].MakeSorter limit) |> ignore
                clock.Stop()
                let allocation = GC.GetTotalAllocatedBytes(true) - allocated
                let cpuSeconds = (currentProcess.TotalProcessorTime - cpu).TotalSeconds
                rows.Add {| Case = name; Round = round; Cached = cached; Seconds = clock.Elapsed.TotalSeconds
                            AllocatedBytes = allocation; CpuSeconds = cpuSeconds |}
                GC.KeepAlive results
    let result = {| Models = models.Length; StoredStages = 895; Channels = 16; Workers = 8; CpuPercent = 25
                    AdaptiveCeLimit = adaptive; CacheEntries = cache.Count; CacheBuildSeconds = buildTimer.Elapsed.TotalSeconds
                    CacheBuildAllocatedBytes = buildAllocated; IdenticalSorters = checkedSorters
                    ByteIdenticalSnapshotPayloadHash = snapshotHash; Trials = rows.ToArray() |}
    File.WriteAllText(destination, JsonSerializer.Serialize(result, JsonSerializerOptions(WriteIndented = true)))
    printfn "%s" (File.ReadAllText destination)
    0
