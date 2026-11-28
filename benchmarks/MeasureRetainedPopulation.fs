// Diagnostic-only: evolve the saved generation-zero population with production
// algorithms, pause for exact external heap walks, and verify saved snapshot bytes.
module Sortopia.MeasureRetainedPopulation

open System
open System.IO
open System.Reflection
open System.Collections.Generic
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSharp.UMX
open Sortopia.Core
open Sortopia.Core.Serialization
open Sortopia.Permutations
open Sortopia.Sorting.Inputs
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.Evaluation
open Sortopia.Evaluation.Sgd
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.FileDb
open Sortopia.RetainedBench
open FunctionParameters

let token = CancellationToken.None
let codec = typeof<SortopiaFileDb>.Assembly.GetType("Sortopia.FileDb.DataCodec", true)
let encode = codec.GetMethod("encode", BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)

let verifyPayload folder item (snapshot: sorterPoolSet) =
    let path = RunOutputFiles.snapshotPath folder item snapshot.Generation
    let originalFile = File.ReadAllBytes path
    let envelope = originalFile |> MessagePackSetup.deserialize<dataEnvelopeDto>
    let bytes, rawLength = encode.Invoke(null, [| box (sortopiaStorageData.SorterPoolSet snapshot) |]) :?> (byte array * int)
    let hash = SHA256.HashData bytes
    if hash <> envelope.Checksum || rawLength <> envelope.RawLength then
        failwith $"Snapshot payload differs at generation {snapshot.Generation}."
    let rebuiltFile = { envelope with Payload = bytes; RawLength = rawLength; Checksum = hash } |> MessagePackSetup.serialize
    if rebuiltFile <> originalFile then failwith "Complete saved snapshot bytes differ."
    Convert.ToHexString hash

let roots (snapshot: sorterPoolSet) =
    let members = snapshot.Pools |> Array.collect _.Members
    members |> Array.map (fun m -> box m.Model), members |> Array.map (fun m -> box m.Evaluation)

let pause label seedModels seedEvaluations snapshot =
    let models, evaluations = roots snapshot
    HeapCheckpoint.Pause(label, snapshot, models, evaluations, seedModels, seedEvaluations)

// What-if only: deduplicate immutable stage values without changing any model ID,
// ordering or saved data. This is intentionally not a new production cache.
let canonicalizeWith (stages: Dictionary<twoOrbitUf4, twoOrbitUf4>) (snapshot: sorterPoolSet) =
    let intern stage =
        match stages.TryGetValue stage with
        | true, existing -> existing
        | _ -> stages.Add(stage, stage); stage
    let convert = function
        | sorterModel.Simple (simpleSorterModel.Msuf4 model) ->
            msuf4.create model.Id model.ChannelCount model.Prefix (model.TwoOrbitUnfolder4s |> Array.map intern)
            |> simpleSorterModel.Msuf4 |> sorterModel.Simple
        | _ -> failwith "This diagnostic expects MSUF4 models."
    let pools = snapshot.Pools |> Array.map (fun pool ->
        { pool with Members = pool.Members |> Array.map (fun m -> { m with Model = convert m.Model }) })
    let result = { snapshot with Pools = pools }
    result, stages.Count

let canonicalize snapshot = canonicalizeWith (Dictionary<twoOrbitUf4, twoOrbitUf4>()) snapshot

[<EntryPoint>]
let main args =
    let folder, output = Path.GetFullPath args[0], Path.GetFullPath args[1]
    let limitType = Assembly.Load("sortopia").GetType("Sortopia.Cli.WindowsExecutionLimits", true)
    let apply = limitType.GetMethod("apply", BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)
    use limits = apply.Invoke(null, [| box 25; box 0L |]) :?> IDisposable
    Directory.CreateDirectory output |> ignore
    let item = RunParameterFiles.listWorkItems (Path.Combine(folder, "runParameters")) token
               |> Array.find (fun item -> item.Parameters.TryFind ParamKeys.orthoRateKey = Some 2.5)
    let p = item.Parameters
    let settings = {
        PoolCount = required ParamKeys.sorterPoolCountKey p
        SortersPerPool = required ParamKeys.sorterCountPerPoolKey p
        RetainedParentsPerPool = required ParamKeys.selectedSorterCountPerPoolKey p
        ChildrenPerParent = required ParamKeys.sorterChildCountKey p
        InitialPoolSeed = required ParamKeys.initialPoolSeedKey p
        InitialPoolSelection = required ParamKeys.initialPoolSelectionKey p |> InitialPoolSelection.fromString
        Generations = EvolutionSampling.target item.Sgd
        MutationMod = required ParamKeys.mutationModKey p
        AdjustCeLengthFrequency = required ParamKeys.adjustCeLengthFrequencyKey p
        SortedFraction = required ParamKeys.sortedFractionKey p
        PrioritizeNewMutants = required ParamKeys.prioritizeNewMutantsKey p
        DistinctSorterHashes = required ParamKeys.distinctSorterHashesKey p
        SelectionMeasure = required ParamKeys.sorterEvalMeasureKey p |> SorterEvalFunctions.fromCompactString }
    let database = SortopiaFileDb(folder, item.ProjectId) :> ISortopiaDb
    let testsReference = item.SetupInputs |> List.find (fun reference -> reference.DataType = sortopiaDataType.SortableTests "")
    let tests = match database.Load(testsReference, token) with
                | Ok { Value = sortopiaStorageData.SortableTests value } -> value
                | other -> failwith $"Expected sortable tests: {other}"
    let mutator =
        SimpleSorterModelMutator.getMsuf4ModelMutator (SortableTests.getChannelCount tests)
            (required ParamKeys.rngTypeKey p |> RngType.fromString |> RngFactory.create) (UMX.tag true)
            (required ParamKeys.seedModificationRateKey p |> UMX.tag)
            (required ParamKeys.modificationRateKey p |> UMX.tag)
            (required ParamKeys.orthoRateKey p |> UMX.tag)
            (required ParamKeys.paraRateKey p |> UMX.tag)
            (required ParamKeys.selfSymRateKey p |> UMX.tag)
            (required ParamKeys.mutatorVariantKey p |> MutatorVariant.fromString)
        |> sorterModelMutator.Simple
    let initial = RunOutputFiles.loadSnapshot folder item 0 token
    let seedModels, seedEvaluations = roots initial
    pause "generation-0" seedModels seedEvaluations initial
    let final = StandardSgd.resumeObserved ignore (ParallelOptions(MaxDegreeOfParallelism = 8)) settings mutator initial tests
    let finalHash = verifyPayload folder item final
    pause "evolved-generation-20" seedModels seedEvaluations final
    let canonical, uniqueStages = canonicalize final
    let canonicalHash = verifyPayload folder item canonical
    pause "canonical-evolved-generation-20" [||] [||] canonical
    let sharedStages = Dictionary<twoOrbitUf4, twoOrbitUf4>()
    let canonicalSeeds, _ = canonicalizeWith sharedStages initial
    let canonicalWithSeeds, combinedUnique = canonicalizeWith sharedStages final
    let canonicalSeedModels, canonicalSeedEvaluations = roots canonicalSeeds
    let combinedHash = verifyPayload folder item canonicalWithSeeds
    pause "canonical-evolved-with-seeds" canonicalSeedModels canonicalSeedEvaluations canonicalWithSeeds
    let loaded = RunOutputFiles.loadSnapshot folder item 20 token
    let loadedHash = verifyPayload folder item loaded
    pause "loaded-generation-20" [||] [||] loaded
    let canonicalLoaded, loadedUnique = canonicalize loaded
    let canonicalLoadedHash = verifyPayload folder item canonicalLoaded
    pause "canonical-loaded-generation-20" [||] [||] canonicalLoaded
    let report = {| WorkItem = string item.Id; MembersPerPool = settings.SortersPerPool
                    Pools = settings.PoolCount; Generations = final.Generation
                    FinalPayloadHash = finalHash; CanonicalPayloadHash = canonicalHash
                    LoadedPayloadHash = loadedHash; CanonicalLoadedPayloadHash = canonicalLoadedHash
                    UniqueStageValues = uniqueStages; LoadedUniqueStageValues = loadedUnique
                    CombinedSeedStageValues = combinedUnique; CanonicalWithSeedsPayloadHash = combinedHash |}
    File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(report, JsonSerializerOptions(WriteIndented = true)))
    printfn "VERIFIED %s" (JsonSerializer.Serialize report)
    0
