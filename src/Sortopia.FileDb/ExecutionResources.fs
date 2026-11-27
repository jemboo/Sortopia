namespace Sortopia.FileDb

open System
open System.Diagnostics
open System.Globalization
open System.Runtime.InteropServices
open System.Threading

/// Launch-only budgets. They never participate in parameter files or output identities.
module ExecutionResources =
    let private number (value: string) =
        match Decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) with
        | true, value when value > 0M -> value
        | _ -> invalidArg "arguments" "Memory must be a positive GiB amount (for example 8GiB) or a percentage (for example 50%)."

    let cpuPercent (value: string) =
        match Int32.TryParse value with
        | true, value when value >= 1 && value <= 100 -> value
        | _ -> invalidArg "arguments" "CPU limit must be a whole percentage from 1 to 100."

    let private memoryValue (value: string) =
        let text = if isNull value then "" else value.Trim()
        if text.EndsWith("%", StringComparison.Ordinal) then
            let percent = number (text.Substring(0, text.Length - 1))
            if percent > 100M then invalidArg "arguments" "Memory percentage must be greater than zero and no more than 100."
            true, percent
        else
            let amount = if text.EndsWith("GiB", StringComparison.OrdinalIgnoreCase) then text.Substring(0, text.Length - 3).Trim() else text
            let gib = number amount
            if gib < 0.25M || gib > decimal Int64.MaxValue / 1073741824M then
                invalidArg "arguments" "Memory must be at least 0.25 GiB and fit in a 64-bit byte count."
            false, gib

    let validateMemory value = memoryValue value |> ignore

    let memoryBytes (availableBytes: int64) value =
        let percent, amount = memoryValue value
        let bytes = if percent then decimal availableBytes * amount / 100M else amount * 1073741824M
        if bytes < 268435456M then invalidArg "arguments" "The resolved memory budget is below 0.25 GiB. Free memory or choose a larger budget."
        if bytes > decimal availableBytes then invalidArg "arguments" "The memory budget exceeds currently available memory. Choose a smaller amount."
        int64 bytes

    let cpuWorkers available percent = max 1 (int (int64 available * int64 percent / 100L))

    let parallelJobs workers (value: string) =
        if String.Equals(value, "auto", StringComparison.OrdinalIgnoreCase) then min 4 workers
        else
            match Int32.TryParse value with
            | true, count when count > 0 -> min workers count
            | _ -> invalidArg "arguments" "Parallel files must be Auto or a positive whole number."

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type private MemoryStatus =
        val mutable Length: uint32
        val mutable Load: uint32
        val mutable TotalPhysical: uint64
        val mutable AvailablePhysical: uint64
        val mutable TotalPageFile: uint64
        val mutable AvailablePageFile: uint64
        val mutable TotalVirtual: uint64
        val mutable AvailableVirtual: uint64
        val mutable AvailableExtendedVirtual: uint64

    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool private GlobalMemoryStatusEx(MemoryStatus& status)

    let availableMemory () =
        if not (OperatingSystem.IsWindows()) then raise (PlatformNotSupportedException("CPU and memory enforcement currently requires Windows."))
        let mutable status = Unchecked.defaultof<MemoryStatus>
        status.Length <- uint32 (Marshal.SizeOf<MemoryStatus>())
        if not (GlobalMemoryStatusEx(&status)) then raise (ComponentModel.Win32Exception(Marshal.GetLastWin32Error()))
        int64 (min status.AvailablePhysical status.AvailablePageFile)

/// Admission control supplements the OS hard cap; estimates cannot guarantee that
/// an individual population fits. No timer or file polling is involved.
type internal ExecutionMemoryGate(limit: int64, readCommitted: unit -> int64) =
    let gate = obj ()
    let mutable reserved = 0L
    let mutable active = 0
    let mutable baseline = 0L
    let ceiling = limit / 10L * 8L
    let committed () = readCommitted ()
    // Server GC can retain empty committed regions after an ordinary collection.
    // Reclaim them only under admission pressure, so reusable heap capacity does
    // not look like live input memory and prevent the next file from starting.
    let reclaim () = GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true)
    let insufficient reservation used =
        let gib bytes = (float bytes / 1073741824.0).ToString("F2", CultureInfo.InvariantCulture)
        let available = max 0L (ceiling - used)
        invalidOp $"The memory budget cannot admit this work item: estimated reservation {gib reservation} GiB, {gib available} GiB available after headroom ({gib limit} GiB worker budget). Increase the memory budget or reduce the model/population size; completed outputs and saved snapshots can be reused."

    member _.CheckHeadroom() =
        if limit > 0L && committed () >= ceiling then
            reclaim ()
            let used = committed ()
            if used >= ceiling then insufficient 0L used

    member _.Acquire(estimate: int64, token: CancellationToken) =
        if limit <= 0L then { new IDisposable with member _.Dispose() = () }
        else
            let reservation = max (64L * 1024L * 1024L) estimate
            if reservation > ceiling then insufficient reservation (committed ())
            lock gate (fun () ->
                let mutable acquired = false
                while not acquired do
                    token.ThrowIfCancellationRequested()
                    let used = committed ()
                    if active = 0 then baseline <- used
                    let occupied = max used (baseline + reserved)
                    if occupied <= ceiling && reservation <= ceiling - occupied then
                        active <- active + 1
                        reserved <- reserved + reservation
                        acquired <- true
                    elif active = 0 then
                        reclaim ()
                        baseline <- committed ()
                        if baseline > ceiling || reservation > ceiling - baseline then insufficient reservation baseline
                    else Monitor.Wait(gate, 100) |> ignore)
            { new IDisposable with
                member _.Dispose() =
                    lock gate (fun () ->
                        active <- active - 1
                        reserved <- reserved - reservation
                        Monitor.PulseAll gate) }

    new(limit) = new ExecutionMemoryGate(limit, fun () ->
        use current = Process.GetCurrentProcess()
        current.PrivateMemorySize64)
