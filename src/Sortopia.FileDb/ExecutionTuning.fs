namespace Sortopia.FileDb

open System
open Sortopia.Runs

/// Machine-local throughput policy; never changes parameters or result identities.
module ExecutionTuning =
    // Measured sweeps spent substantially more CPU beyond eight workers for a
    // smaller elapsed-time gain. Other active files can use the remainder.
    let workersPerFile cpuWorkers = max 1 (min 8 cpuWorkers)

    // Limit GC to half the CPU allowance and at least 512 MiB of process budget
    // per heap. Eight heaps could not fit the constrained larger-population sweep;
    // a single heap uses workstation GC instead.
    let gcHeapCount cpuWorkers memoryBytes =
        let memoryHeaps = if memoryBytes <= 0L then 8 else int (min 8L (memoryBytes / 536870912L))
        max 1 (min (min 8 (cpuWorkers / 2)) memoryHeaps)

    /// A conservative admission hint for evolving parents, offspring and their
    /// evaluation buffers; admission takes the larger of this and the source-size
    /// estimate. The 512 KiB allowance is calibrated to the 16-channel sweeps;
    /// it is not an upper bound on arbitrary model/test inputs. OS limits remain
    /// authoritative. In particular, sharing a source does not make evolution free.
    let populationReservationBytes (parameters: parameterSet) =
        match parameters.TryFind ParamKeys.sorterPoolCountKey,
              parameters.TryFind ParamKeys.sorterCountPerPoolKey,
              parameters.TryFind ParamKeys.sorterChildCountKey with
        | Some pools, Some sorters, Some children when pools > 0 && sorters > 0 && children > 0 ->
            let bytes = bigint pools * bigint sorters * (bigint children + 1I) * 524288I
            int64 (min (bigint Int64.MaxValue) bytes)
        | _ -> 0L

    /// Generated sets have no source payload to size. Reserve for both the model
    /// object graph and its DTO/serialization buffers, using the function's stage
    /// preview rather than optional saved display metadata. This is a conservative
    /// hint calibrated against 5,000 16-channel, 895-stage MSUF4 models, not a bound.
    let modelGenerationReservationBytes (parameters: parameterSet) suffixStages =
        let width, prefixStages =
            match parameters.TryFind ParamKeys.prefixLibIdKey with
            | Some prefix -> int prefix.ChannelCount, int prefix.StageLength
            | None -> parameters.TryFind ParamKeys.channelCountKey |> Option.defaultValue 0, 0
        match parameters.TryFind ParamKeys.sorterCountKey with
        | Some count when count > 0 && width > 0 && suffixStages > 0 ->
            let stages = bigint suffixStages + bigint prefixStages
            let bytes = bigint count * (4096I + bigint width * stages * 64I)
            int64 (min (bigint Int64.MaxValue) bytes)
        | _ -> 0L
