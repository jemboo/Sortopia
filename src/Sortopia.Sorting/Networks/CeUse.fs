namespace Sortopia.Sorting.Networks

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting

type ceUse = 
    private {
        ceIndex :int<ceIndex>
        useCount :int
        ce: ce
    }
    static member create 
                (ceIndex: int<ceIndex>) 
                (useCount: int)
                (ce: ce): ceUse =
        if int ceIndex < 0 then invalidArg "ceIndex" "CE indices must be nonnegative."
        if useCount < -1 then invalidArg "useCount" "Usage must be nonnegative or -1 for an already-applied prefix CE."
        { ceIndex = ceIndex; useCount = useCount; ce = ce }
    member this.CeIndex with get() : int<ceIndex> = this.ceIndex
    member this.UseCount with get() : int = this.useCount
    /// The CE belongs to the fixed prefix already applied when preparing the inputs.
    member this.IsPrefix with get() : bool = this.useCount = -1
    member this.Ce with get() : ce = this.ce

module CeUse =

    let toString (ceUse: ceUse) : string =
        sprintf "[%d, %d, %s]" (%ceUse.CeIndex) ceUse.UseCount (ceUse.Ce |> Ce.toString)

    let arrayToString (ceUses: ceUse array) : string =
        ceUses
        |> Array.map toString
        |> String.concat "; "

    // merges two ceUses with the same CeIndex and the same ce, combining their use counts
    let merge (ceUse1: ceUse) (ceUse2: ceUse) : ceUse =
        if ceUse1.CeIndex <> ceUse2.CeIndex then
            invalidArg "ceUse2" "Cannot merge ceUses with different CeIndex values."
        if ceUse1.Ce <> ceUse2.Ce then
            invalidArg "ceUse2" "Cannot merge ceUses with different ce values."
        if ceUse1.IsPrefix <> ceUse2.IsPrefix then
            invalidArg "ceUse2" "Cannot merge prefix markers with measured usage."
        let mergedUseCount = if ceUse1.IsPrefix then -1 else Checked.(+) ceUse1.UseCount ceUse2.UseCount
        ceUse.create ceUse1.CeIndex mergedUseCount ceUse1.Ce
