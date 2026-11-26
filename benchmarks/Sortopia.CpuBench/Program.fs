module Sortopia.CpuBench

open System
open System.IO
open System.Diagnostics
open System.Reflection
open System.Runtime
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open Sortopia.FileDb
open Sortopia.Functions

[<EntryPoint>]
let main args =
    let template, output, workers = Path.GetFullPath args[0], Path.GetFullPath args[1], int args[2]
    if Directory.Exists output then failwith "Use a new output directory."
    let cpuPercent, memoryBytes = 50, 8L * 1024L * 1024L * 1024L
    if ExecutionResources.availableMemory () < memoryBytes then failwith "This comparison needs 8 GiB of available memory."
    // Use the CLI's actual enforcement implementation, which is intentionally internal.
    let limitsType = Assembly.Load("sortopia").GetType("Sortopia.Cli.WindowsExecutionLimits", true)
    let apply = limitsType.GetMethod("apply", BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic)
    use limits = apply.Invoke(null, [| box cpuPercent; box memoryBytes |]) :?> IDisposable
    use workerProcess = Process.GetCurrentProcess()
    for file in Directory.EnumerateFiles(template, "*", SearchOption.AllDirectories) do
        let target = Path.Combine(output, Path.GetRelativePath(template, file))
        Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
        File.Copy(file, target, false)
    let files = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(output, "selection.json")))
    let paths = files |> Array.map (fun file -> Path.Combine(output, "runParameters", file))
    let allocated = GC.GetTotalAllocatedBytes true
    let pauses = GC.GetTotalPauseDuration()
    let collections = Array.init 3 GC.CollectionCount
    let completedWork = ThreadPool.CompletedWorkItemCount
    let cpu, user, kernel = workerProcess.TotalProcessorTime, workerProcess.UserProcessorTime, workerProcess.PrivilegedProcessorTime
    printfn "Starting %d workers: 50%% CPU, 8 GiB memory, server GC %b." workers GCSettings.IsServerGC
    let timer = Stopwatch.StartNew()
    let report = RunParameterExecution.executeFilesWithOptions paths (resultsExecutionOptions(4, workers, memoryBytes, workers)) null null CancellationToken.None
    timer.Stop()
    let cpuSeconds = (workerProcess.TotalProcessorTime - cpu).TotalSeconds
    let userSeconds = (workerProcess.UserProcessorTime - user).TotalSeconds
    let kernelSeconds = (workerProcess.PrivilegedProcessorTime - kernel).TotalSeconds
    let allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated
    let pauseSeconds = (GC.GetTotalPauseDuration() - pauses).TotalSeconds
    let collectionCounts = Array.init 3 (fun generation -> GC.CollectionCount generation - collections[generation])
    let workItems = ThreadPool.CompletedWorkItemCount - completedWork
    let result = {| Workers = workers; Seconds = timer.Elapsed.TotalSeconds; CpuSeconds = cpuSeconds
                    UserSeconds = userSeconds; KernelSeconds = kernelSeconds; AllocatedBytes = allocatedBytes
                    GcPauseSeconds = pauseSeconds; Collections = collectionCounts; ThreadPoolWorkItems = workItems
                    ServerGc = GCSettings.IsServerGC; Error = report.Error |}
    File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(result, JsonSerializerOptions(WriteIndented = true)))
    if report.Status <> runExecutionStatus.Completed then failwith report.Error
    let resultsFolder = Path.Combine(output, "data", "results")
    let hashes = Directory.GetFiles(resultsFolder, "*.msgpack", SearchOption.AllDirectories) |> Array.sort |> Array.map (fun file ->
        use stream = File.OpenRead file
        {| Path = Path.GetRelativePath(resultsFolder, file); Hash = Convert.ToHexString(SHA256.HashData stream) |})
    File.WriteAllText(Path.Combine(output, "result-hashes.json"), JsonSerializer.Serialize hashes)
    printfn "Finished: %.2fs elapsed, %.2fs CPU, %.2fs GC pause, %.2f GiB allocated, %d work items."
        timer.Elapsed.TotalSeconds cpuSeconds pauseSeconds (float allocatedBytes / 1073741824.0) workItems
    0
