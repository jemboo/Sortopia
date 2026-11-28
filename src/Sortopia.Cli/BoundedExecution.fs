namespace Sortopia.Cli

open System
open System.IO
open System.Runtime
open System.Text.Json
open System.Threading
open Sortopia.FileDb

/// CPU-limited execution with an independent soft target and optional hard cap.
module internal BoundedExecution =
    let run cpuPercent memoryBytes (execution: resultsExecutionOptions) metricsPath
            (execute: resultsExecutionOptions -> CancellationToken -> int) (token: CancellationToken) =
        use metrics =
            match metricsPath with
            | Some path -> new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)
            | None -> null
        token.ThrowIfCancellationRequested()
        use limits = WindowsExecutionLimits.applyTracked cpuPercent memoryBytes
        CommandLineReport.write "Batch limits: %d%% CPU, %d CPU workers, at most %d parallel files." cpuPercent execution.CpuWorkers execution.MaxParallelJobs
        if execution.MemoryTargetBytes > 0L then
            CommandLineReport.write "Memory target: %.2f GiB for scheduling; a work item can exceed it when running alone." (float execution.MemoryTargetBytes / 1073741824.0)
        if limits.MemoryBytes > 0L then
            CommandLineReport.write "Hard memory cap: %.2f GiB total committed memory; allocations can fail at this cap." (float limits.MemoryBytes / 1073741824.0)
        else CommandLineReport.write "Hard memory cap: none."
        CommandLineReport.write "Execution tuning: %d workers per file; %s GC with runtime-managed heaps; %.2f GiB managed-heap limit." execution.MaxWorkersPerFile (if GCSettings.IsServerGC then "server" else "workstation") (float (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes) / 1073741824.0)
        let mutable exitCode = 1
        try
            try
                token.ThrowIfCancellationRequested()
                exitCode <- execute execution token
                exitCode
            with :? OperationCanceledException when token.IsCancellationRequested ->
                exitCode <- 130
                reraise ()
        finally
            if not (isNull metrics) then
                let usage = limits.ReadUsage()
                JsonSerializer.Serialize(metrics,
                    {| CpuSeconds = usage.CpuSeconds; PeakCommittedBytes = usage.PeakCommittedBytes
                       TotalProcesses = usage.TotalProcesses; HardMemoryLimitBytes = limits.MemoryBytes
                       MemoryTargetBytes = execution.MemoryTargetBytes
                       ManagedHeapLimitBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
                       CpuPercent = cpuPercent; CpuWorkers = execution.CpuWorkers
                       ParallelFiles = execution.MaxParallelJobs; WorkersPerFile = execution.MaxWorkersPerFile
                       ServerGC = GCSettings.IsServerGC; ExitCode = exitCode |})
