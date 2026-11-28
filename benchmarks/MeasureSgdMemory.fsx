// Isolate evolution from loading a much larger source file and from checkpoint serialization.
// Run each build in a fresh FSI process. This measures managed memory and allocation
// traffic, not the OS-job peak measured by BenchmarkSweeps.ps1. GC mode is recorded.
// dotnet fsi --lib:<CLI build> benchmarks/MeasureSgdMemory.fsx -- <new JSON path> [members=512]
#r "MessagePack.dll"
#r "FSharp.UMX.dll"
#r "Sortopia.Core.dll"
#r "Sortopia.Core.Serialization.dll"
#r "Sortopia.Permutations.dll"
#r "Sortopia.Sorting.dll"
#r "Sortopia.Models.Sorting.dll"
#r "Sortopia.Evaluation.dll"
#r "Sortopia.Evaluation.Serialization.dll"

open System
open System.Diagnostics
open System.IO
open System.Runtime.CompilerServices
open System.Security.Cryptography
open System.Text.Json
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

let destination = fsi.CommandLineArgs[1]
if File.Exists destination then failwith "Use a new measurement output path."
let population = if fsi.CommandLineArgs.Length > 2 then int fsi.CommandLineArgs[2] else 512
let guid name = GuidUtils.guidFromObjs [box name]
let prefix = Array.init 8 (fun index -> ce.create (index * 2) (index * 2 + 1))
let arrays = Array.init 65536 (fun mask ->
    let values = Array.init 16 (fun bit -> mask &&& (1 <<< bit) <> 0)
    for ce in prefix do
        let low, high = int ce.LowChannel, int ce.HighChannel
        if values[low] && not values[high] then values[low] <- false; values[high] <- true
    sortableBoolArray.create(values, 16<channelCount>))
let tests = SortableBitv512Tests.fromBoolArrays (UMX.tag (guid "memory-tests")) 16<channelCount> arrays |> sortableTests.Bitv512
let settings = {
    PoolCount = 1; SortersPerPool = population; RetainedParentsPerPool = population; ChildrenPerParent = 3
    InitialPoolSeed = 7; InitialPoolSelection = initialPoolSelection.Random
    Generations = 10; MutationMod = 0; AdjustCeLengthFrequency = 2; SortedFraction = 0.99
    PrioritizeNewMutants = false; DistinctSorterHashes = false
    SelectionMeasure = SorterEvalMeasure.stageBiasedFilterUnsorted }
let mutator = SimpleSorterModelMutator.getMsuf4ModelMutator 16<channelCount> rngFactory.LcgFactory (UMX.tag true)
                  (UMX.tag 0.05) (UMX.tag 0.05) (UMX.tag 0.3) (UMX.tag 0.3) (UMX.tag 0.4) mutatorVariant.V1
              |> sorterModelMutator.Simple
let options = ParallelOptions(MaxDegreeOfParallelism = 8)
let prepare configuration =
    let gen = SimpleSorterModelGen.makeUniform rngFactory.LcgFactory 16<channelCount> prefix 895<stageLength> simpleSorterModelType.Msuf4 (UMX.tag true)
    let models = Array.init configuration.SortersPerPool (fun index -> SimpleSorterModelGen.makeSorterModelFromId (UMX.tag (guid ("memory-" + string index))) gen |> sorterModel.Simple)
                 |> fun models -> sorterModelSet.create (UMX.tag (guid "memory-source")) models 7160<ceLength>
    StandardSgd.prepareInitialPopulation options configuration models tests

// Source generation and seed evaluation are outside this focused measurement.
// Warm the code paths, then release these models before establishing the heap baseline.
do
    let warm = { settings with SortersPerPool = 4; RetainedParentsPerPool = 4; Generations = 1 }
    StandardSgd.runObservedPrepared ignore options warm mutator (prepare warm) tests |> ignore
let fixedMemory = GC.GetTotalMemory(true)
// A separate frame makes releasing the prepared population explicit to the GC.
[<MethodImpl(MethodImplOptions.NoInlining)>]
let measure () =
    let prepared = prepare settings
    let before = GC.GetTotalMemory(true)
    let allocatedBefore = GC.GetTotalAllocatedBytes(true)
    let clock = Stopwatch.StartNew()
    let final = StandardSgd.runObservedPrepared ignore options settings mutator prepared tests
    clock.Stop()
    let allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore
    let additional = GC.GetTotalMemory(true) - before
    GC.KeepAlive prepared
    final, clock.Elapsed.TotalSeconds, allocated, additional, before - fixedMemory
let final, seconds, allocated, additional, initialPopulationBytes = measure ()
// Sharing with retained seeds saves additional memory; report the final population
// separately after releasing the seed root so that saving is not overstated.
let finalPopulationBytes = GC.GetTotalMemory(true) - fixedMemory
// Check full result bytes after stopping the allocation/time measurement.
let saved = final |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
let result = {| Members = population; Stages = 895; Generations = settings.Generations
                ChildrenPerParent = settings.ChildrenPerParent; Workers = 8
                ServerGc = System.Runtime.GCSettings.IsServerGC
                Seconds = seconds; AllocatedBytes = allocated
                InitialPopulationBytes = initialPopulationBytes
                AdditionalResultBytesWithSeedsRetained = additional
                FinalPopulationBytes = finalPopulationBytes
                ResultSha256 = SHA256.HashData(saved) |> Convert.ToHexString |}
File.WriteAllText(destination, JsonSerializer.Serialize(result, JsonSerializerOptions(WriteIndented = true)))
printfn "%s" (File.ReadAllText destination)
GC.KeepAlive final
