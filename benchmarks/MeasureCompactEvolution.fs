// Compare frozen baseline, V1-only, and MSRS-optimized builds using identical
// saved filtered inputs. All semantic/detail checks and heap walks are untimed.
module Sortopia.MeasureCompactEvolution

open System
open System.IO
open System.Diagnostics
open System.Reflection
open System.Security.Cryptography
open System.Runtime.InteropServices
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Core
open Sortopia.Core.Serialization
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Evaluation
open Sortopia.Evaluation.Sgd
open Sortopia.Evaluation.Serialization
open Sortopia.Functions
open Sortopia.Runs
open Sortopia.FileDb
open Sortopia.RetainedBench

let guid name = GuidUtils.guidFromObjs [box name]
let sha bytes = SHA256.HashData(bytes: byte[]) |> Convert.ToHexString
let compact (value: sorterPoolSet) =
    let pools = value.Pools |> Array.map (fun pool ->
        let members = pool.Members |> Array.map (fun m -> { m with Evaluation = SorterEval.toV1 m.Evaluation })
        { pool with Members = members })
    { value with Pools = pools }
let roots (snapshot: sorterPoolSet) =
    let members = snapshot.Pools |> Array.collect _.Members
    members |> Array.map (fun m -> box m.Model), members |> Array.map (fun m -> box m.Evaluation)

[<EntryPoint>]
let main args =
    let width, count, workers, population, generations, stages =
        int args[0], int args[1], int args[2], int args[3], int args[4], int args[5]
    let mode, destination, fixture = args[6], Path.GetFullPath args[7], Path.GetFullPath args[8]
    if Directory.Exists destination then failwith "Use a fresh output directory."
    if mode <> "measure" && mode <> "heap" then failwith "Choose measure or heap."
    Directory.CreateDirectory destination |> ignore
    let flags = BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic
    let limits = Assembly.Load("sortopia").GetType("Sortopia.Cli.WindowsExecutionLimits", true)
    use job = limits.GetMethod("applyTracked", flags).Invoke(null, [|box 25; box 0L|]) :?> IDisposable
    let fixtureVersion = if width = 64 then 2 else 1
    let caseName = $"msrs-inputs-v{fixtureVersion}-{width}-{count}-{population}-{generations}-{stages}"
    let projectId = UMX.tag<projectId> (guid caseName)
    let keyFor kind = DataKey.create projectId "Results" false "benchmark-msrs-inputs" "1" kind [] (parameterSet.create Map.empty) []
    let settings = {
        PoolCount = 2; SortersPerPool = population; RetainedParentsPerPool = population; ChildrenPerParent = 1
        InitialPoolSeed = 7; InitialPoolSelection = initialPoolSelection.Random; Generations = generations
        MutationMod = 3; AdjustCeLengthFrequency = 1; SortedFraction = 0.8; PrioritizeNewMutants = false
        DistinctSorterHashes = true; SelectionMeasure = SorterEvalMeasure.stageBiased_UnsortedCount }
    let allocations = GC.GetTotalAllocatedBytes true
    let timer = Stopwatch.StartNew()
    let sourceDb = SortopiaFileDb(fixture, projectId) :> ISortopiaDb
    let packed =
        match sourceDb.TryLoad(keyFor (sortopiaDataType.SortableTests "filtered"), CancellationToken.None) with
        | Ok (Some { Value = sortopiaStorageData.SortableTests (sortableTests.Bitv512 values) }) -> values
        | other -> failwith $"Cannot load packed fixture: {other}"
    let expectedId = UMX.tag<sortableTestsId> (guid $"{caseName}-tests")
    if packed.Id <> expectedId || int packed.ChannelCount <> width || int packed.SortableCount <> count then
        failwith "Input identity or dimensions differ."
    let preparationSeconds = timer.Elapsed.TotalSeconds
    let tests = sortableTests.Bitv512 packed
    let prefix = [|for low in 0 .. 2 .. width - 2 -> ce.create low (low + 1)|]
    let generator = SimpleSorterModelGen.makeUniform rngFactory.LcgFactory (UMX.tag width) prefix (UMX.tag stages) simpleSorterModelType.Msrs (UMX.tag true)
    let models = Array.init (2 * population) (fun index ->
        SimpleSorterModelGen.makeSorterModelFromId (UMX.tag (guid $"{caseName}-model-{index}")) generator |> sorterModel.Simple)
    let modelSet = sorterModelSet.create (UMX.tag (guid $"{caseName}-models")) models (UMX.tag (width / 2 * stages))
    let mutator = SimpleSorterModelMutator.getMsrsModelMutator rngFactory.LcgFactory (UMX.tag true)
                      (UMX.tag 0.1) (UMX.tag 0.3) (UMX.tag 0.3) (UMX.tag 0.4) mutatorVariant.V2
                  |> sorterModelMutator.Simple
    let db = SortopiaFileDb(destination, projectId) :> ISortopiaDb
    let save kind value =
        match db.GetOrCreate(keyFor kind, (fun () -> Ok value), CancellationToken.None) with
        | Ok _ -> ()
        | Error error -> failwith $"Saving: {error}"
    let mutable initial = Unchecked.defaultof<sorterPoolSet>
    let observe (snapshot: sorterPoolSet) =
        if snapshot.Generation = 0 then initial <- snapshot
        let label = $"generation-{snapshot.Generation}"
        save (sortopiaDataType.SorterPoolSet label) (sortopiaStorageData.SorterPoolSet snapshot)
        let summary = { TestsId = %snapshot.TestsId; Rows = EvolutionReports.summarize settings.SelectionMeasure snapshot }
        save (sortopiaDataType.SorterPoolSummarySet label) (sortopiaStorageData.SorterPoolSummarySet summary)
    let final = StandardSgd.runObserved observe (ParallelOptions(MaxDegreeOfParallelism = workers)) settings mutator modelSet tests
    timer.Stop()
    let allocated = GC.GetTotalAllocatedBytes(true) - allocations
    let instance = BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic
    let usage = job.GetType().GetMethod("ReadUsage", instance).Invoke(job, [||])
    let peak = usage.GetType().GetProperty("PeakCommittedBytes", instance).GetValue(usage) :?> uint64
    let cpu = usage.GetType().GetProperty("CpuSeconds", instance).GetValue(usage) :?> float
    use currentProcess = Process.GetCurrentProcess()
    let working = currentProcess.PeakWorkingSet64
    if mode = "heap" then
        let models, evaluations = roots final
        let seedModels, seedEvaluations = roots initial
        HeapCheckpoint.Pause("evolved", final, models, evaluations, seedModels, seedEvaluations)
        // Allocation-only upper-workload probe for ranking/cutoff buffers. Duplicates
        // are retained to exercise the full second sort at each requested pool size.
        let selection = [|64; 1024; 2048|] |> Array.map (fun size ->
            let members = Array.init (size * 2) (fun i ->
                { final.Pools[0].Members[i % final.Pools[0].Members.Length] with Id = UMX.tag (guid $"selection-{i}") })
            let pool = { final.Pools[0] with Members = members }
            let config = { settings with SortersPerPool = size; RetainedParentsPerPool = size; DistinctSorterHashes = false }
            let choose () =
                let parents = pool.Members |> Array.sortBy _.Id
                let retained = parents |> Array.sortBy (fun m -> float (SorterEvalFunctions.getFunctionForMeasure config.SelectionMeasure m.Evaluation), m.Id)
                                       |> Array.truncate size
                GC.KeepAlive retained
                pool |> StandardSgd.adjustCeLength config.SortedFraction |> StandardSgd.prune config |> ignore
            choose ()
            let before = GC.GetAllocatedBytesForCurrentThread()
            for trial = 1 to 100 do choose ()
            {| MembersPerPool = size; CandidateCount = members.Length; BytesPerPass = (GC.GetAllocatedBytesForCurrentThread() - before) / 100L |})
        File.WriteAllText(Path.Combine(destination, "selection-buffers.json"), JsonSerializer.Serialize(selection))
    // All audits below are outside the measured time, allocation and peak capture.
    use digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
    let countBytes = Array.zeroCreate<byte> 4
    for block in packed.SimdSortBlocks do
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(countBytes.AsSpan(), block.SortableCount)
        digest.AppendData(countBytes)
        digest.AppendData(MemoryMarshal.AsBytes(block.Vectors.AsSpan()))
    let semantic = ResizeArray<_>()
    let details = ResizeArray<_>()
    for generation = 0 to generations do
        let snapshot =
            match db.TryLoad(keyFor (sortopiaDataType.SorterPoolSet $"generation-{generation}"), CancellationToken.None) with
            | Ok (Some { Value = sortopiaStorageData.SorterPoolSet snapshot }) -> snapshot
            | other -> failwith $"Cannot reload saved checkpoint: {other}"
        semantic.Add {| Generation = generation; Hash = snapshot |> compact |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize |> sha |}
        if generation = 0 || generation = generations then
            for poolIndex = 0 to snapshot.Pools.Length - 1 do
                for memberValue in snapshot.Pools[poolIndex].Members |> Array.truncate 2 do
#if COMPACT
                    let detail = StandardSgd.evaluateMemberDetails (ParallelOptions(MaxDegreeOfParallelism = workers)) tests snapshot poolIndex memberValue.Id
#else
                    let detail = memberValue.Evaluation
#endif
                    details.Add {| Generation = generation; Pool = poolIndex; Member = string memberValue.Id
                                   Hash = detail |> SorterEvalDto.fromDomain |> MessagePackSetup.serialize |> sha |}
    let files = Directory.GetFiles(destination, "*.msgpack", SearchOption.AllDirectories) |> Array.sort |> Array.map (fun path ->
        {| Path = Path.GetRelativePath(destination, path); Bytes = FileInfo(path).Length; Hash = File.ReadAllBytes path |> sha |})
    let snapshotBytes = files |> Array.filter (fun file -> file.Path.Contains("SorterPoolSet")) |> Array.sumBy _.Bytes
    let result = {| Width = width; TestCount = count; Workers = workers; MembersPerPool = population; Pools = 2; Generations = generations
                    StoredStages = stages; Mode = mode; CpuPercent = 25; HardCapBytes = 0L; PreparationSeconds = preparationSeconds
                    Seconds = timer.Elapsed.TotalSeconds; CpuSeconds = cpu; PeakCommittedBytes = peak; PeakWorkingSetBytes = working
                    AllocatedBytes = allocated; SnapshotBytes = snapshotBytes; InputId = string packed.Id; InputHash = Convert.ToHexString(digest.GetHashAndReset())
                    SemanticSnapshots = semantic.ToArray(); Details = details.ToArray(); Files = files |}
    File.WriteAllText(Path.Combine(destination, "measurement.json"), JsonSerializer.Serialize(result, JsonSerializerOptions(WriteIndented = true)))
    printfn "MEASURED %.3fs; peak commit %.1f MiB; resident %.1f MiB; snapshots %d bytes" result.Seconds (float peak / 1048576.0) (float working / 1048576.0) snapshotBytes
    GC.KeepAlive initial
    0
