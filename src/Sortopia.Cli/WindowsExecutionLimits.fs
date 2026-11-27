namespace Sortopia.Cli

open System
open System.ComponentModel
open System.Diagnostics
open System.Runtime.InteropServices
open Microsoft.Win32.SafeHandles

/// Applied only by the terminal CLI, before reading any input populations.
/// Closing the handle does not kill the process; limits remain until it exits.
module internal WindowsExecutionLimits =
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type private BasicLimits =
        val mutable PerProcessTime: int64
        val mutable PerJobTime: int64
        val mutable Flags: uint32
        val mutable MinimumWorkingSet: unativeint
        val mutable MaximumWorkingSet: unativeint
        val mutable ActiveProcessLimit: uint32
        val mutable Affinity: unativeint
        val mutable PriorityClass: uint32
        val mutable SchedulingClass: uint32

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type private IoCounters =
        val mutable ReadOperations: uint64
        val mutable WriteOperations: uint64
        val mutable OtherOperations: uint64
        val mutable ReadBytes: uint64
        val mutable WriteBytes: uint64
        val mutable OtherBytes: uint64

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type private ExtendedLimits =
        val mutable Basic: BasicLimits
        val mutable Io: IoCounters
        val mutable ProcessMemory: unativeint
        val mutable JobMemory: unativeint
        val mutable PeakProcessMemory: unativeint
        val mutable PeakJobMemory: unativeint

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type private CpuLimits =
        val mutable Flags: uint32
        val mutable Rate: uint32

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type private Accounting =
        val mutable UserTime: int64
        val mutable KernelTime: int64
        val mutable PeriodUserTime: int64
        val mutable PeriodKernelTime: int64
        val mutable PageFaults: uint32
        val mutable TotalProcesses: uint32
        val mutable ActiveProcesses: uint32
        val mutable TerminatedProcesses: uint32

    type Usage = { CpuSeconds: float; PeakCommittedBytes: uint64; TotalProcesses: uint32 }

    [<DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)>]
    extern SafeFileHandle private CreateJobObject(nativeint attributes, string name)
    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool private SetInformationJobObject(SafeFileHandle job, int informationClass, nativeint information, uint32 length)
    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool private AssignProcessToJobObject(SafeFileHandle job, nativeint targetProcess)
    [<DllImport("kernel32.dll", SetLastError = true)>]
    extern bool private QueryInformationJobObject(SafeFileHandle job, int informationClass, nativeint information, uint32 length, nativeint returnedLength)

    let private setInformation<'a when 'a: struct> job kind (value: 'a) =
        let size = Marshal.SizeOf<'a>()
        let buffer = Marshal.AllocHGlobal size
        try
            Marshal.StructureToPtr(value, buffer, false)
            if not (SetInformationJobObject(job, kind, buffer, uint32 size)) then
                raise (Win32Exception(Marshal.GetLastWin32Error(), "Could not enforce the requested execution limits."))
        finally Marshal.FreeHGlobal buffer

    let private query<'a when 'a: struct> job kind =
        let size = Marshal.SizeOf<'a>()
        let buffer = Marshal.AllocHGlobal size
        try
            if not (QueryInformationJobObject(job, kind, buffer, uint32 size, 0n)) then
                raise (Win32Exception(Marshal.GetLastWin32Error()))
            Marshal.PtrToStructure<'a> buffer
        finally Marshal.FreeHGlobal buffer

    /// Windows stores job memory caps in whole pages. Round down so the enforced
    /// budget never exceeds the selection, and all worker/GC budgets use the same value.
    let memoryLimitBytes (requestedBytes: int64) =
        let page = int64 Environment.SystemPageSize
        if requestedBytes < page then invalidArg "requestedBytes" "The memory limit must be at least one system page."
        requestedBytes - requestedBytes % page

    type Handle internal (job: SafeFileHandle, memoryBytes: int64) =
        member _.MemoryBytes = memoryBytes
        member _.ReadUsage() =
            let accounting = query<Accounting> job 1
            let memory = query<ExtendedLimits> job 9
            { CpuSeconds = float (accounting.UserTime + accounting.KernelTime) / float TimeSpan.TicksPerSecond
              PeakCommittedBytes = uint64 memory.PeakJobMemory; TotalProcesses = accounting.TotalProcesses }
        interface IDisposable with member _.Dispose() = job.Dispose()

    let requireInherited cpuPercent (memoryBytes: int64) =
        use currentJob = new SafeFileHandle(0n, false)
        let cpu = query<CpuLimits> currentJob 15
        let memory = query<ExtendedLimits> currentJob 9
        if cpu.Flags &&& 0x5u <> 0x5u || cpu.Rate <> uint32 (cpuPercent * 100) ||
           memory.Basic.Flags &&& 0x200u = 0u || uint64 memory.JobMemory <> uint64 memoryBytes then
            invalidOp $"The execution worker did not inherit the requested CPU and memory limits. Expected CPU rate {cpuPercent * 100} and job memory {memoryBytes} bytes; found CPU flags {cpu.Flags}, rate {cpu.Rate}, memory flags {memory.Basic.Flags}, job memory {uint64 memory.JobMemory} bytes. No results were executed."

    let applyTracked cpuPercent memoryBytes =
        if not (OperatingSystem.IsWindows()) || not Environment.Is64BitProcess then
            raise (PlatformNotSupportedException("CPU and memory enforcement requires 64-bit Windows."))
        let memoryBytes = memoryLimitBytes memoryBytes
        let job = CreateJobObject(0n, null)
        try
            if job.IsInvalid then raise (Win32Exception(Marshal.GetLastWin32Error()))
            let mutable cpu = Unchecked.defaultof<CpuLimits>
            cpu.Flags <- 0x5u // ENABLE | HARD_CAP, percent of total CPU time.
            cpu.Rate <- uint32 (cpuPercent * 100)
            setInformation job 15 cpu
            let mutable limits = Unchecked.defaultof<ExtendedLimits>
            let mutable basic = Unchecked.defaultof<BasicLimits>
            basic.Flags <- 0x200u ||| 0x20u // JOB_MEMORY | PRIORITY_CLASS
            basic.PriorityClass <- 0x4000u // BELOW_NORMAL_PRIORITY_CLASS
            limits.Basic <- basic
            limits.JobMemory <- unativeint memoryBytes
            setInformation job 9 limits
            use current = Process.GetCurrentProcess()
            if not (AssignProcessToJobObject(job, current.Handle)) then
                raise (Win32Exception(Marshal.GetLastWin32Error(), "Could not attach the execution process to its resource limits. No results were executed."))
            // Leave 25% for native allocations, stacks and the runtime. Refresh
            // explicitly: changing environment variables after CLR startup has no effect.
            AppContext.SetData("GCHeapHardLimit", uint64 (memoryBytes / 4L * 3L))
            GC.RefreshMemoryLimit()
            new Handle(job, memoryBytes)
        with _ ->
            job.Dispose()
            reraise ()

    let apply cpuPercent memoryBytes : IDisposable = applyTracked cpuPercent memoryBytes
