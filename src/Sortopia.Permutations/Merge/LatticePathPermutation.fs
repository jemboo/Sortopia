namespace Sortopia.Permutations

open Sortopia.Core

open System
open FSharp.UMX


[<Struct>]
type latticePathPermutation = 
    { 
        Lp: latticePoint
        PArray: int[] 
    }
    with
        static member createEmpty (latticeDimension: int<latticeDimension>) (pathLength: int<latticeDistance>) = 
            { Lp = latticePoint.create(Array.zeroCreate %latticeDimension)
              PArray = Array.create %pathLength -1 }

        static member create (lp: latticePoint) (pArray: int[]) = 
            { Lp = lp; PArray = pArray }

        member this.Length = this.PArray.Length

        /// Accessor for external consumption
        member this.Item
            with get(i: int) = 
                let v = this.PArray.[i]
                if v = -1 then None else Some v

        member this.IsComplete = 
            Array.IndexOf(this.PArray, -1) = -1



module LatticePathPermutation =

    /// Deep copy the array, but return a new Struct
    let copy (lptp: latticePathPermutation) =
        { Lp = lptp.Lp; PArray = Array.copy lptp.PArray }
        
    let updateWithLatticePoint 
            (edgeLength: int<latticeDistance>) 
            (newLevel: int)
            (newLp: latticePoint) 
            (lpPerm: latticePathPermutation) : latticePathPermutation =
        
        // 1. Calculate the index before we "move"
        let index = LatticePoint.getPermutationIndex edgeLength lpPerm.Lp newLp
        
        // 2. Create the new array for the branched path
        let newArray = Array.copy lpPerm.PArray
        newArray.[index] <- newLevel
        
        // 3. Return a brand new struct with the new point and the new array
        { Lp = newLp; PArray = newArray }

    let toPermutation (lptp: latticePathPermutation) : permutation =
        if not lptp.IsComplete then
            failwith "Cannot convert an incomplete lattice path to a permutation."
        permutation.create (Array.copy lptp.PArray)