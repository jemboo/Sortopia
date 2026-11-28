// Frozen-build comparison of filtered-input preparation and complete MSRS evolution.
// Compile against each build; CANDIDATE selects its streaming packing entry point.
module Sortopia.MeasureMsrsInputs

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
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Evaluation
open Sortopia.Evaluation.Sgd
open Sortopia.Functions
open Sortopia.Runs
open Sortopia.FileDb

let guid name = GuidUtils.guidFromObjs [box name]

// Enumerate distinct outputs of adjacent compare-exchanges without generating
// the full binary universe. The affine permutation is coprime to 3^pairCount.
let filteredSubset width count = seq {
    let size = pown 3UL (width / 2)
    if uint64 count > size then invalidArg "count" "Requested more distinct prefix images than exist."
    let candidateStep = if width = 64 then uint64 (bigint size * 618033988749895I / 1000000000000000I) else 104729UL
    let step = if candidateStep % 3UL = 0UL then candidateStep + 1UL else candidateStep
    let mutable position = 271UL
    for index = 0 to count - 1 do
        let mutable code = position
        position <- (position + step) % size
        let values = Array.zeroCreate<bool> width
        for pair = 0 to width / 2 - 1 do
            let digit = code % 3UL
            code <- code / 3UL
            values[pair * 2] <- digit = 2UL
            values[pair * 2 + 1] <- digit <> 0UL
        yield sortableBoolArray.create(values, UMX.tag width)
}

[<EntryPoint>]
let main args =
    let width, count, workers, population, generations, stages =
        int args[0], int args[1], int args[2], int args[3], int args[4], int args[5]
    let mode, destination = args[6], Path.GetFullPath args[7]
    if Directory.Exists destination then failwith "Use a fresh measurement directory."
    Directory.CreateDirectory destination |> ignore
    let flags = BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic
    let limits = Assembly.Load("sortopia").GetType("Sortopia.Cli.WindowsExecutionLimits", true)
    use job = limits.GetMethod("applyTracked", flags).Invoke(null, [|box 25; box 0L|]) :?> IDisposable
    let readUsage () = job.GetType().GetMethod("ReadUsage", BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic).Invoke(job, [||])
    let fixtureVersion = if width = 64 then 2 else 1
    let caseName = $"msrs-inputs-v{fixtureVersion}-{width}-{count}-{population}-{generations}-{stages}"
    let projectId = UMX.tag<projectId> (guid caseName)
    let keyFor kind = DataKey.create projectId "Results" false "benchmark-msrs-inputs" "1" kind [] (parameterSet.create Map.empty) []
    let prefix = [|for low in 0 .. 2 .. width - 2 -> ce.create low (low + 1)|]
    let id = UMX.tag<sortableTestsId> (guid $"{caseName}-tests")
    let allocations = GC.GetTotalAllocatedBytes true
    let timer = Stopwatch.StartNew()
    let source =
        if width = 24 && count = 531441 then
            SortableBoolArray.getAllPossibleResultsFromCeArray prefix 24<channelCount>
        else filteredSubset width count
    let packed =
      if mode = "load-evolve" then
        let sourceDb = SortopiaFileDb(Path.GetFullPath args[8], projectId) :> ISortopiaDb
        match sourceDb.TryLoad(keyFor (sortopiaDataType.SortableTests "filtered"), CancellationToken.None) with
        | Ok (Some { Value = sortopiaStorageData.SortableTests (sortableTests.Bitv512 values) }) -> values
        | other -> failwith $"Cannot load packed fixture: {other}"
      else
#if CANDIDATE
        SortableBitv512Tests.fromBoolSequence id (UMX.tag width) source
#else
        SortableBitv512Tests.fromBoolArrays id (UMX.tag width) (Seq.toArray source)
#endif
    if int packed.SortableCount <> count || packed.Id <> id || int packed.ChannelCount <> width then
        failwith "Input identity or dimensions differ."
    let preparationSeconds = timer.Elapsed.TotalSeconds
    let preparationAllocated = GC.GetTotalAllocatedBytes(true) - allocations
    let tests = sortableTests.Bitv512 packed
    let db = SortopiaFileDb(destination, projectId) :> ISortopiaDb
    let save label kind value =
        let key = keyFor kind
        match db.GetOrCreate(key, (fun () -> Ok value), CancellationToken.None) with
        | Ok _ -> ()
        | Error error -> failwith $"Saving {label}: {error}"
    if mode = "fixture" then
        save "filtered" (sortopiaDataType.SortableTests "filtered") (sortopiaStorageData.SortableTests tests)
    elif mode = "evolve" || mode = "load-evolve" then
        let generator = SimpleSorterModelGen.makeUniform rngFactory.LcgFactory (UMX.tag width) prefix (UMX.tag stages) simpleSorterModelType.Msrs (UMX.tag true)
        let models = Array.init (2 * population) (fun index ->
            SimpleSorterModelGen.makeSorterModelFromId (UMX.tag (guid $"{caseName}-model-{index}")) generator |> sorterModel.Simple)
        let modelSet = sorterModelSet.create (UMX.tag (guid $"{caseName}-models")) models (UMX.tag (width / 2 * stages))
        let mutator = SimpleSorterModelMutator.getMsrsModelMutator rngFactory.LcgFactory (UMX.tag true)
                          (UMX.tag 0.1) (UMX.tag 0.3) (UMX.tag 0.3) (UMX.tag 0.4) mutatorVariant.V2
                      |> sorterModelMutator.Simple
        let settings = {
            PoolCount = 2; SortersPerPool = population; RetainedParentsPerPool = population; ChildrenPerParent = 1
            InitialPoolSeed = 7; InitialPoolSelection = initialPoolSelection.Random; Generations = generations
            MutationMod = 3; AdjustCeLengthFrequency = 1; SortedFraction = 0.8; PrioritizeNewMutants = false
            DistinctSorterHashes = true; SelectionMeasure = SorterEvalMeasure.stageBiased_UnsortedCount }
        let observe (snapshot: sorterPoolSet) =
            let label = $"generation-{snapshot.Generation}"
            save label (sortopiaDataType.SorterPoolSet label) (sortopiaStorageData.SorterPoolSet snapshot)
            let summary = { TestsId = %snapshot.TestsId; Rows = EvolutionReports.summarize settings.SelectionMeasure snapshot }
            save label (sortopiaDataType.SorterPoolSummarySet label) (sortopiaStorageData.SorterPoolSummarySet summary)
        StandardSgd.runObserved observe (ParallelOptions(MaxDegreeOfParallelism = workers)) settings mutator modelSet tests |> ignore
    elif mode <> "prepare" then failwith "Unknown measurement mode."
    timer.Stop()
    let allocated = GC.GetTotalAllocatedBytes(true) - allocations
    let usage = readUsage ()
    let propertyFlags = BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic
    let peak = usage.GetType().GetProperty("PeakCommittedBytes", propertyFlags).GetValue(usage) :?> uint64
    let cpu = usage.GetType().GetProperty("CpuSeconds", propertyFlags).GetValue(usage) :?> float
    use currentProcess = Process.GetCurrentProcess()
    let peakWorking = currentProcess.PeakWorkingSet64
    // Hashing and decoding checks are deliberately outside timing and peak capture.
    use digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
    let countBytes = Array.zeroCreate<byte> 4
    let ones = Array.zeroCreate<int64> width
    for block in packed.SimdSortBlocks do
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(countBytes.AsSpan(), block.SortableCount)
        digest.AppendData(countBytes)
        let vectors = block.Vectors
        digest.AppendData(MemoryMarshal.AsBytes(vectors.AsSpan()))
        for wire = 0 to width - 1 do
            for word = 0 to 7 do
                ones[wire] <- ones[wire] + int64 (System.Numerics.BitOperations.PopCount(System.Runtime.Intrinsics.Vector512.GetElement(vectors[wire], word)))
    if ones |> Array.exists (fun value -> value < int64 count / 5L || value > int64 count * 4L / 5L) then
        failwith "Filtered fixture has inadequate channel coverage."
    let inputHash = Convert.ToHexString(digest.GetHashAndReset())
    let hashes = Directory.GetFiles(destination, "*.msgpack", SearchOption.AllDirectories) |> Array.sort |> Array.map (fun path ->
        {| Path = Path.GetRelativePath(destination, path); Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes path)) |})
    let result = {| Width = width; TestCount = count; Workers = workers; MembersPerPool = population; Pools = 2
                    Generations = generations; StoredStages = stages; Mode = mode; CpuPercent = 25; HardCapBytes = 0L
                    PreparationSeconds = preparationSeconds; Seconds = timer.Elapsed.TotalSeconds; CpuSeconds = cpu
                    PreparationAllocatedBytes = preparationAllocated
                    PeakCommittedBytes = peak; PeakWorkingSetBytes = peakWorking; AllocatedBytes = allocated
                    InputHash = inputHash; InputId = string id; FixtureVersion = fixtureVersion; OnesPerChannel = ones; Files = hashes |}
    File.WriteAllText(Path.Combine(destination, "measurement.json"), JsonSerializer.Serialize(result, JsonSerializerOptions(WriteIndented = true)))
    printfn "%s" (JsonSerializer.Serialize result)
    0
