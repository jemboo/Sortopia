namespace Sortopia.FileDb

open System
open System.Collections.Generic
open System.Threading
open Sortopia.Evaluation

/// Files register only after memory admission, and release their share on exit.
/// Leases bound overlapping phases even when a new file starts mid-evaluation.
type internal ExecutionCpuBudget(capacity: int) =
    let gate = obj ()
    let active = ResizeArray<int64>()
    let mutable nextId = 0L
    let mutable allocated = 0
    do if capacity < 1 then invalidArg "capacity" "CPU workers must be positive."

    member _.Register() =
        let id = lock gate (fun () ->
            nextId <- nextId + 1L
            active.Add nextId
            nextId)
        let mutable disposed = false
        { new IEvaluationCpuBudget with
            member _.Acquire(requested, token) =
                if requested < 1 then invalidArg "requested" "Requested CPU workers must be positive."
                let count = lock gate (fun () ->
                    let mutable count = 0
                    while count = 0 do
                        token.ThrowIfCancellationRequested()
                        if disposed then raise (ObjectDisposedException("CPU allocation"))
                        // Give remainder workers to the oldest active files.
                        let share = max 1 (capacity / active.Count + (if active.IndexOf id < capacity % active.Count then 1 else 0))
                        count <- min requested (min share (capacity - allocated))
                        if count = 0 then Monitor.Wait(gate, 100) |> ignore
                    allocated <- allocated + count
                    count)
                let mutable released = false
                { new IEvaluationCpuLease with
                    member _.WorkerCount = count
                    member _.Dispose() = lock gate (fun () ->
                        if not released then
                            released <- true
                            allocated <- allocated - count
                            Monitor.PulseAll gate) }
            member _.Dispose() = lock gate (fun () ->
                if not disposed then
                    disposed <- true
                    active.Remove id |> ignore
                    Monitor.PulseAll gate) }
