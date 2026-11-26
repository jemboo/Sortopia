// Creates isolated, shortened copies of a saved SGD sweep. Never changes the source.
// dotnet fsi --lib:<CLI build> benchmarks/PrepareSweeps.fsx -- <source project> <new output directory> [grid generations] [sorters per pool]
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
open MessagePack
open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Runs
open Sortopia.FileDb

let args = fsi.CommandLineArgs |> Array.skip 1
let source, destination = Path.GetFullPath args[0], Path.GetFullPath args[1]
let generations = if args.Length > 2 then int args[2] else 100
let population = if args.Length > 3 then Some(int args[3]) else None
let tuningOnly = args.Length > 4 && args[4] = "tuning"
if population |> Option.exists (fun value -> value < 1) then failwith "Population must be positive."
if generations < 20 || generations % 20 <> 0 then failwith "Generations must be a positive multiple of 20."
if Directory.Exists destination then failwith "Use a new benchmark directory."
if destination.StartsWith(source + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) then
    failwith "Benchmark output must be outside the source project."
let items = RunParameterFiles.listWorkItems (Path.Combine(source, "runParameters")) CancellationToken.None
            |> Array.sortBy (fun item -> item.SetupSetNumber, item.SetNumber)
if items.Length < 9 then failwith "This benchmark expects at least nine saved SGD parameter files."
let envelopeOptions = MessagePackSetup.options.WithCompression(MessagePackCompression.Lz4BlockArray)
let copyFile (path: string) (target: string) =
    Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
    File.Copy(path, target, false)
let make name (selected: runParameters[]) (targets: int[]) =
    let folder = Path.Combine(destination, name)
    Directory.CreateDirectory(Path.Combine(folder, "runParameters")) |> ignore
    Directory.CreateDirectory(Path.Combine(folder, "runs")) |> ignore
    let projectId = string selected[0].ProjectId
    copyFile (Path.Combine(source, projectId + ".msgpack")) (Path.Combine(folder, projectId + ".msgpack"))
    for path in Directory.EnumerateFiles(Path.Combine(source, "data", "setup"), "*", SearchOption.AllDirectories) do
        copyFile path (Path.Combine(folder, Path.GetRelativePath(source, path)))
    let records = Array.map2 (fun (item: runParameters) target ->
        let original = Path.Combine(source, "runParameters", string item.Id + ".msgpack")
        let envelope = MessagePackSerializer.Deserialize<runParametersEnvelopeDto>(ReadOnlyMemory(File.ReadAllBytes original), envelopeOptions)
        let saved = MessagePackSetup.deserialize<sgdRunParametersDto> envelope.Payload
        if saved.Version <> 4 then failwith "This benchmark expects version 4 SGD parameter envelopes."
        // A distinct copied run per file allows unequal generation targets.
        let runId = UMX.untag item.Id
        let parameters = saved.Invocation.Parameters |> Array.map (fun binding ->
            match population with
            | Some count when binding.Key = "SorterCountPerPool" || binding.Key = "SelectedSorterCountPerPool" ->
                { binding with Value = MessagePackSetup.serialize count }
            | _ -> binding)
        let invocation = { saved.Invocation with RunId = runId; Parameters = parameters }
        let payload = MessagePackSetup.serialize { saved with Invocation = invocation; SnapshotInterval = 20 }
        let checksum = SHA256.HashData payload
        let id = Guid(checksum[0..15])
        let bytes = MessagePackSerializer.Serialize({ envelope with Payload = payload; Checksum = checksum }, envelopeOptions)
        File.WriteAllBytes(Path.Combine(folder, "runParameters", string id + ".msgpack"), bytes)
        let run: sgdRunTargetDto = {
            id = runId; parentProjectId = UMX.untag item.ProjectId
            sgd = Some { SnapshotInterval = 20; SnapshotCount = target / 20; TempSnapshotInterval = Some 10 } }
        File.WriteAllBytes(Path.Combine(folder, "runs", string runId + ".msgpack"), MessagePackSetup.serialize run)
        {| OriginalFile = string item.Id; File = string id + ".msgpack"; Target = target
           SetupSet = item.SetupSetNumber |> Option.defaultValue 0; ResultsSet = item.SetNumber |}) selected targets
    File.WriteAllText(Path.Combine(folder, "selection.json"), JsonSerializer.Serialize(records |> Array.map _.File))
    records

let manifest =
    if tuningOnly then
        [| {| Scenario = "larger-population"; Files = make "larger-population" items[0..2] (Array.create 3 generations); Precomplete = 0 |} |]
    else [|
        {| Scenario = "fresh-grid"; Files = make "fresh-grid" items (Array.create items.Length generations); Precomplete = 0 |}
        {| Scenario = "uneven-tail"; Files = make "uneven-tail" items[0..3] [|20; 20; 20; max 100 (generations * 2)|]; Precomplete = 0 |}
        {| Scenario = "mostly-complete"; Files = make "mostly-complete" items[0..3] [|generations; generations; generations; max 100 generations|]; Precomplete = 3 |}
    |]
File.WriteAllText(Path.Combine(destination, "manifest.json"), JsonSerializer.Serialize(manifest, JsonSerializerOptions(WriteIndented = true)))
printfn "Prepared %d scenarios in %s" manifest.Length destination
