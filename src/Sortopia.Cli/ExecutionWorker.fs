namespace Sortopia.Cli

open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Sortopia.FileDb

// Public constructors let System.Text.Json populate the private stdin protocol.
[<CLIMutable>]
type ExecutionWorkerRequest = {
    Arguments: string array
    CpuPercent: int
    TotalMemoryBytes: int64
    CpuWorkers: int
    ParallelFiles: int
    WorkerMemoryBytes: int64
    GcHeapCount: int
}

/// GC policy must be set before CLR startup. The small launcher and its worker
/// inherit ONE job, so CPU percentage is not applied twice and memory is aggregate.
module internal ExecutionWorker =

    let configureEnvironment (start: ProcessStartInfo) (request: ExecutionWorkerRequest) =
        // Remove competing heap limits, including per-generation overrides, from
        // the child only. Never change the terminal or machine environment.
        for prefix in ["DOTNET_"; "COMPlus_"] do
            for setting in ["gcServer"; "GCHeapCount"; "GCHeapHardLimit"; "GCHeapHardLimitPercent"
                            "GCHeapHardLimitSOH"; "GCHeapHardLimitLOH"; "GCHeapHardLimitPOH"
                            "GCHeapHardLimitSOHPercent"; "GCHeapHardLimitLOHPercent"; "GCHeapHardLimitPOHPercent"] do
                start.Environment.Remove(prefix + setting) |> ignore
        start.Environment["DOTNET_gcServer"] <- if request.GcHeapCount > 1 then "1" else "0"
        start.Environment["DOTNET_GCHeapCount"] <- request.GcHeapCount.ToString("x")
        start.Environment["DOTNET_GCHeapHardLimit"] <- (request.WorkerMemoryBytes / 4L * 3L).ToString("x")

    let private pump (reader: StreamReader) (writer: TextWriter) = task {
        let mutable reading = true
        while reading do
            let! line = reader.ReadLineAsync()
            if isNull line then reading <- false else do! writer.WriteLineAsync(line)
    }

    let launch args cpuPercent memoryBytes (execution: resultsExecutionOptions) metricsPath (token: CancellationToken) =
        // Fail before executing if a requested diagnostics destination is unusable.
        use metrics =
            match metricsPath with
            | Some path -> new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)
            | None -> null
        use limits = WindowsExecutionLimits.applyTracked cpuPercent memoryBytes
        use current = Process.GetCurrentProcess()
        let reserve = max 67108864L (current.PrivateMemorySize64 + 16777216L)
        let workerMemory = memoryBytes - reserve
        if workerMemory < 134217728L then invalidOp "The memory budget leaves less than 128 MiB for execution after launcher overhead. Increase the budget."
        let request = {
            Arguments = args; CpuPercent = cpuPercent; TotalMemoryBytes = memoryBytes
            CpuWorkers = execution.CpuWorkers; ParallelFiles = execution.MaxParallelJobs
            WorkerMemoryBytes = workerMemory; GcHeapCount = ExecutionTuning.gcHeapCount execution.CpuWorkers workerMemory }
        CommandLineReport.write "Batch limits: %d%% CPU, %.2f GiB total committed memory, %d CPU workers, at most %d parallel files." cpuPercent (float memoryBytes / 1073741824.0) request.CpuWorkers request.ParallelFiles
        let assembly = Assembly.GetExecutingAssembly().Location
        let apphost = Path.ChangeExtension(assembly, ".exe")
        let start = ProcessStartInfo()
        start.UseShellExecute <- false
        // The launcher owns the terminal. A hidden worker also avoids opening
        // a console when invoked from a headless benchmark or test process.
        start.CreateNoWindow <- true
        start.RedirectStandardInput <- true
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        start.StandardInputEncoding <- UTF8Encoding(false)
        start.StandardOutputEncoding <- Encoding.UTF8
        start.StandardErrorEncoding <- Encoding.UTF8
        if File.Exists apphost then start.FileName <- apphost
        else
            start.FileName <- Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") |> Option.ofObj |> Option.defaultValue "dotnet"
            start.ArgumentList.Add assembly
        start.ArgumentList.Add "--execution-worker"
        configureEnvironment start request
        token.ThrowIfCancellationRequested()
        use child = Process.Start start
        try
            child.StandardInput.AutoFlush <- true
            child.StandardInput.WriteLine(JsonSerializer.Serialize request)
            use cancel = token.Register(fun () ->
                try child.StandardInput.WriteLine("cancel")
                with :? IOException -> () | :? ObjectDisposedException -> ())
            let stdout = pump child.StandardOutput Console.Out :> Task
            let stderr = pump child.StandardError Console.Error :> Task
            let mutable pending = [stdout; stderr; child.WaitForExitAsync()]
            // Observe a failed output pipe immediately, rather than deadlocking a
            // child whose output is no longer being consumed. No progress polling.
            while not pending.IsEmpty do
                let completed = Task.WhenAny(pending).GetAwaiter().GetResult()
                completed.GetAwaiter().GetResult()
                pending <- pending |> List.filter (fun task -> not (Object.ReferenceEquals(task, completed)))
            if not (isNull metrics) then
                let usage = limits.ReadUsage()
                let measured =
                    {| CpuSeconds = usage.CpuSeconds
                       PeakCommittedBytes = usage.PeakCommittedBytes
                       TotalProcesses = usage.TotalProcesses
                       TotalMemoryBytes = memoryBytes
                       WorkerMemoryBytes = workerMemory
                       CpuPercent = cpuPercent
                       CpuWorkers = request.CpuWorkers
                       ParallelFiles = request.ParallelFiles
                       WorkersPerFile = execution.MaxWorkersPerFile
                       GcHeapCount = request.GcHeapCount
                       ServerGC = request.GcHeapCount > 1
                       ExitCode = child.ExitCode |}
                JsonSerializer.Serialize(metrics, measured)
            child.ExitCode
        finally
            if not child.HasExited then
                child.Kill(true)
                child.WaitForExit()

    let run (execute: string array -> resultsExecutionOptions -> CancellationToken -> int) (token: CancellationToken) =
        Console.OutputEncoding <- UTF8Encoding(false)
        use input = new StreamReader(Console.OpenStandardInput())
        let line = input.ReadLine()
        if isNull line then invalidOp "Missing execution worker request."
        let request = JsonSerializer.Deserialize<ExecutionWorkerRequest> line
        if isNull (box request) || isNull request.Arguments || request.CpuWorkers < 1 || request.ParallelFiles < 1 ||
           request.CpuPercent < 1 || request.CpuPercent > 100 || request.TotalMemoryBytes < request.WorkerMemoryBytes ||
           request.WorkerMemoryBytes < 134217728L || request.GcHeapCount <> ExecutionTuning.gcHeapCount request.CpuWorkers request.WorkerMemoryBytes then
            invalidOp "Invalid execution worker request."
        WindowsExecutionLimits.requireInherited request.CpuPercent request.TotalMemoryBytes
        if GCSettings.IsServerGC <> (request.GcHeapCount > 1) then invalidOp "The runtime did not apply the requested GC mode. No results were executed."
        if GC.GetGCMemoryInfo().TotalAvailableMemoryBytes > request.WorkerMemoryBytes / 4L * 3L then
            invalidOp "The runtime did not apply the requested GC memory limit. No results were executed."
        let execution = resultsExecutionOptions(request.ParallelFiles, request.CpuWorkers, request.WorkerMemoryBytes)
        CommandLineReport.write "Execution tuning: %d workers per file; %s GC, at most %d heap(s); %.2f GiB worker budget." execution.MaxWorkersPerFile (if GCSettings.IsServerGC then "server" else "workstation") request.GcHeapCount (float request.WorkerMemoryBytes / 1073741824.0)
        use cancellation = CancellationTokenSource.CreateLinkedTokenSource token
        // Parent forwards Ctrl+C over stdin. EOF also cancels if the launcher dies.
        // Windows console streams cannot cancel a pending read. A background read
        // lives until this worker exits; it never delays successful completion.
        let monitorGate = obj ()
        let mutable finished = false
        let monitor = new Thread(ThreadStart(fun () ->
            try input.ReadLine() |> ignore
            with :? IOException -> () | :? ObjectDisposedException -> ()
            lock monitorGate (fun () -> if not finished then cancellation.Cancel())))
        // Do not occupy a thread-pool worker, especially with a one-CPU budget.
        monitor.IsBackground <- true
        monitor.Name <- "Execution cancellation"
        monitor.Start()
        try execute request.Arguments execution cancellation.Token
        finally lock monitorGate (fun () -> finished <- true)
