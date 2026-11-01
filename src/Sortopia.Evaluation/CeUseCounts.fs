namespace Sortopia.Evaluation

open System
open System.Threading
open FSharp.UMX
open Sortopia.Core
open Sortopia.Sorting


// -----------------------------
// CE use-count container
// -----------------------------

[<CustomEquality; NoComparison>]
type ceUseCounts =
    private {
        useCounts: int array
    }

    static member Create (ceLength: int<ceLength>) =
        if %ceLength < 0 then invalidArg "ceLength" "Length must be nonnegative."
        { useCounts = Array.zeroCreate %ceLength }

    static member CreateFromArray (counts: int[]) =
        if isNull counts then nullArg "counts"
        if Array.exists (fun n -> n < 0) counts then invalidArg "counts" "Counts must be nonnegative."
        { useCounts = Array.copy counts }

    // ----------------
    // Robust Hashing (Single-Threaded)
    // ----------------

    override this.GetHashCode() =
        // Robust FNV-1a Hash + Avalanche
        let mutable h = 2166136261u
        let arr = this.useCounts
        for i = 0 to arr.Length - 1 do
            h <- (h ^^^ uint32 arr.[i]) * 16777619u

        // Final mixing (avalanche) to ensure quality
        h <- h ^^^ (h >>> 16)
        h <- h * 0x85ebca6bu
        h <- h ^^^ (h >>> 13)
        h <- h * 0xc2b2ae35u
        h <- h ^^^ (h >>> 16)

        // Keep the historical hash algorithm without caching mutable state.
        let finalHash = if h = 0u then 1 else int h
        finalHash

    override this.Equals(obj) =
        match obj with
        | :? ceUseCounts as other ->
            // Check hash first: extremely fast for rejecting inequality
            if this.GetHashCode() <> other.GetHashCode() then false
            else
                // Use Span for vectorized hardware-accelerated equality check
                System.ReadOnlySpan<int>(this.useCounts)
                    .SequenceEqual(System.ReadOnlySpan<int>(other.useCounts))
        | _ -> false

    // ----------------
    // Fast Mutations
    // ----------------

    member this.IncrementBy (index: int<ceIndex>) (amount: int) : unit =
        if amount < 0 then invalidArg "amount" "Increment must be nonnegative."
        this.useCounts.[%index] <- Checked.(+) this.useCounts.[%index] amount

    member this.Increment (index: int<ceIndex>) : unit =
        this.useCounts.[%index] <- Checked.(+) this.useCounts.[%index] 1

    // ----------------
    // Atomic accumulation; inspect equality/hash only after writers finish.
    // ----------------

    member this.IncrementAtomicBy (index: int<ceIndex>) (amount: int) : unit =
        if amount < 0 then invalidArg "amount" "Increment must be nonnegative."
        Interlocked.Add(&this.useCounts.[%index], amount) |> ignore

    member this.IncrementAtomic (index: int<ceIndex>) : unit =
        Interlocked.Increment(&this.useCounts.[%index]) |> ignore


    // ----------------
    // Properties
    // ----------------

    member this.Length = this.useCounts.Length

    member this.Item with get (index: int<ceIndex>) = this.useCounts.[%index]

    /// Number of CEs that have been used at least once
    member this.UsedCeCount : int<ceLength> =
        (Array.fold (fun acc count -> if count <> 0 then acc + 1 else acc) 0 this.useCounts)
        |> UMX.tag<ceLength>

    member this.LastUsedCeIndex : int<ceIndex> =
        ArrayUtils.lastNonZeroIndex this.useCounts
        |> UMX.tag<ceIndex>

    /// Safe snapshot (no external mutation)
    member this.ToArray() : int[] =
        Array.copy this.useCounts
