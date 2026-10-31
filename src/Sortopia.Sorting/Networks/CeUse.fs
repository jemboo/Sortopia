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
        { ceIndex = ceIndex; useCount = useCount; ce = ce }
    member this.CeIndex with get() : int<ceIndex> = this.ceIndex
    member this.UseCount with get() : int = this.useCount
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
        let mergedUseCount = ceUse1.UseCount + ceUse2.UseCount
        ceUse.create ceUse1.CeIndex mergedUseCount ceUse1.Ce
