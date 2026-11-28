// Validate completed larger-pool trials, including every member of every saved snapshot.
// dotnet fsi --lib:<CLI build> benchmarks/VerifyPoolSweep.fsx -- <trial project> <members per pool> <baseline result-hashes.json> <new report.json> [partial]
#r "MessagePack.dll"
#r "FSharp.UMX.dll"
#r "Sortopia.Core.dll"
#r "Sortopia.Core.Serialization.dll"
#r "Sortopia.Sorting.dll"
#r "Sortopia.Models.Sorting.dll"
#r "Sortopia.Evaluation.dll"
#r "Sortopia.Runs.dll"
#r "Sortopia.Functions.dll"
#r "Sortopia.Workspaces.dll"
#r "Sortopia.FileDb.dll"

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open Sortopia.Runs
open Sortopia.FileDb

let args = fsi.CommandLineArgs |> Array.skip 1
let folder, members, reference, destination = args[0], int args[1], args[2], args[3]
let allowPartial = args.Length > 4 && args[4] = "partial"
if File.Exists destination then failwith "Use a new verification report path."
let root = Path.Combine(folder, "data", "results")
let expected =
    use manifest = JsonDocument.Parse(File.ReadAllText reference)
    manifest.RootElement.EnumerateArray() |> Seq.map (fun row ->
        row.GetProperty("Path").GetString(), row.GetProperty("Hash").GetString()) |> Map.ofSeq
let actual = Directory.EnumerateFiles(root, "*.msgpack", SearchOption.AllDirectories) |> Seq.map (fun path ->
    Path.GetRelativePath(root, path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes path))) |> Map.ofSeq
if (not allowPartial && actual <> expected) ||
   (actual |> Map.exists (fun path hash -> Map.tryFind path expected <> Some hash)) then
    failwith "Saved result/snapshot paths or bytes differ from the baseline."
let items = RunParameterFiles.listWorkItems (Path.Combine(folder, "runParameters")) CancellationToken.None
let snapshots = ResizeArray<_>()
let mutable complete = 0
for item in items do
    if item.Parameters.TryFind ParamKeys.sorterCountPerPoolKey <> Some members ||
       item.Parameters.TryFind ParamKeys.selectedSorterCountPerPoolKey <> Some members ||
       item.Parameters.TryFind ParamKeys.sorterPoolCountKey <> Some 2 then
        failwith "Unexpected population settings."
    let status = RunOutputFiles.inspect folder item CancellationToken.None
    if status.State = outputState.Complete then complete <- complete + 1
    elif status.State = outputState.Invalid || not allowPartial then
        failwith $"Incomplete or invalid work item: {item.Id}: {status.Error}"
    let samples = RunOutputFiles.samples item
    if (samples.Snapshots |> Array.map fst) <> [|0; 20|] then failwith "Unexpected snapshot generations."
    for generation, _ in samples.Snapshots |> Array.filter (fun (generation, _) -> File.Exists(RunOutputFiles.snapshotPath folder item generation)) do
        let snapshot = RunOutputFiles.loadSnapshot folder item generation CancellationToken.None
        if snapshot.Generation <> generation || snapshot.Pools.Length <> 2 ||
           (snapshot.Pools |> Array.exists (fun pool -> pool.Members.Length <> members)) then
            failwith "A saved snapshot has the wrong generation or population size."
        snapshots.Add {| WorkItem = string item.Id; Generation = generation
                         Pools = snapshot.Pools.Length; MembersPerPool = members |}
File.WriteAllText(destination, JsonSerializer.Serialize(
    {| MembersPerPool = members; WorkItems = items.Length; CompletedWorkItems = complete
       IdenticalFiles = actual.Count; Snapshots = snapshots.ToArray() |}, JsonSerializerOptions(WriteIndented = true)))
printfn "Verified %d complete work items, %d byte-identical files, and %d snapshots with %d members per pool."
    complete actual.Count snapshots.Count members
