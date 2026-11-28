// Run separately against baseline and candidate builds; compare the resulting JSON.
// dotnet fsi --lib:<CLI build directory> benchmarks/VerifySgdReplay.fsx -- <new JSON path>
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
open System.IO
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
if File.Exists destination then failwith "Use a new verification output path."
let guid name = GuidUtils.guidFromObjs [box name]
let prefix = [|ce.create 0 1; ce.create 2 3; ce.create 4 5; ce.create 6 7|]
let inputs = Array.init 256 (fun mask ->
    let values = Array.init 8 (fun bit -> mask &&& (1 <<< bit) <> 0)
    for ce in prefix do
        let low, high = int ce.LowChannel, int ce.HighChannel
        if values[low] && not values[high] then values[low] <- false; values[high] <- true
    sortableBoolArray.create(values, 8<channelCount>))
let tests = sortableBinaryTests.create (UMX.tag (guid "replay-tests")) 8<channelCount> inputs |> sortableTests.Bools
let bytes value = value |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
let hash (value: byte[]) = SHA256.HashData value |> Convert.ToHexString
let rows = ResizeArray<_>()

for kind in [simpleSorterModelType.Msce; simpleSorterModelType.Msrs; simpleSorterModelType.Msuf4] do
    let gen = SimpleSorterModelGen.makeUniform rngFactory.LcgFactory 8<channelCount> prefix 24<stageLength> kind (UMX.tag true)
    let models = Array.init 267 (fun index -> SimpleSorterModelGen.makeSorterModelFromId (UMX.tag (guid (string kind + string index))) gen |> sorterModel.Simple)
                 |> fun models -> sorterModelSet.create (UMX.tag (guid (string kind))) models 96<ceLength>
    for variant in [mutatorVariant.V1; mutatorVariant.V2] do
      let mutator =
        match kind with
        | simpleSorterModelType.Msce ->
            SimpleSorterModelMutator.getMsceModelMutator rngFactory.LcgFactory (UMX.tag true) (UMX.tag 0.1)
                (UMX.tag 1.0) (UMX.tag 0.0) (UMX.tag 0.0) variant
        | simpleSorterModelType.Msrs ->
            SimpleSorterModelMutator.getMsrsModelMutator rngFactory.LcgFactory (UMX.tag true) (UMX.tag 0.1)
                (UMX.tag 0.3) (UMX.tag 0.3) (UMX.tag 0.4) variant
        | _ ->
            SimpleSorterModelMutator.getMsuf4ModelMutator 8<channelCount> rngFactory.LcgFactory (UMX.tag true)
                (UMX.tag 0.1) (UMX.tag 0.1) (UMX.tag 0.3) (UMX.tag 0.3) (UMX.tag 0.4) variant
        |> sorterModelMutator.Simple
      for best, distinct in [false, false; true, false; false, true] do
        let settings = {
            PoolCount = 2; SortersPerPool = 129; RetainedParentsPerPool = 129; ChildrenPerParent = 3
            InitialPoolSeed = 7; InitialPoolSelection = if best then initialPoolSelection.Best else initialPoolSelection.Random
            Generations = 5; MutationMod = 3; AdjustCeLengthFrequency = 2; SortedFraction = 0.8
            PrioritizeNewMutants = distinct; DistinctSorterHashes = distinct
            SelectionMeasure = if best then SorterEvalMeasure.stageBiased_UnsortedCount else SorterEvalMeasure.stageBiasedFilterUnsorted }
        let name = $"{kind}-{variant}-best{best}-distinct{distinct}"
        let snapshots = ResizeArray<_>()
        let result = StandardSgd.runObserved (fun snapshot -> snapshots.Add(snapshot, bytes snapshot))
                         (ParallelOptions(MaxDegreeOfParallelism = 4)) settings mutator models tests
        for snapshot, saved in snapshots do
            if saved <> bytes snapshot then failwith $"Earlier snapshot was modified: {name}"
            let summary = { TestsId = UMX.untag snapshot.TestsId; Rows = EvolutionReports.summarize settings.SelectionMeasure snapshot }
            rows.Add {| Case = name; Generation = snapshot.Generation; Snapshot = hash saved
                        Summary = summary |> SorterPoolSummarySetCodec.encode |> hash |}
        let restored = snd snapshots[2] |> MessagePackSetup.deserialize<sorterPoolSetDto> |> SorterPoolSetDto.toDomain
        let resumed = StandardSgd.resumeObserved ignore (ParallelOptions(MaxDegreeOfParallelism = 1)) settings mutator restored tests
        if bytes result <> bytes resumed then failwith $"Checkpoint resume differs: {name}"

File.WriteAllText(destination, JsonSerializer.Serialize(rows, JsonSerializerOptions(WriteIndented = true)))
printfn "Verified %d snapshots and summaries, including checkpoint resume and immutable earlier generations." rows.Count
