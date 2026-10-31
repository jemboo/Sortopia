namespace Sortopia.Permutations

open Sortopia.Core

open FSharp.UMX
open Combinatorics


module PermRsOps = 

    let makeRandomPerm_Rs
        (indexPicker: int -> int) 
        (floatPicker: unit -> float)
        (opsGenRates: opsGenRates) 
        (order: int) : permRs =
        if order % 2 <> 0 then
            failwith "Perm_Rs order must be even"
        
        let mutationModePicker () = 
            opsGenRates.PickMode floatPicker

        let rsPairTracker = Array.init (order / 2) (fun dex -> ((dex, reflect order dex), true))
        let _availableFlags () = rsPairTracker |> Seq.mapi (fun i (pair, flag) -> (i, pair, flag)) |> Seq.filter (fun (_, _, flag) -> flag)

        let twoOrbits =
            seq {
                while (_availableFlags () |> Seq.length > 0) do
                    let availablePairs = _availableFlags () |> Seq.toArray
                    if availablePairs.Length < 2 then
                        // Since only one pair is left, just yield it
                        let (origIndex, lastPair, _) = availablePairs.[0]
                        rsPairTracker.[origIndex] <- (lastPair, false)
                        yield lastPair
                    else
                        let (dex1, dex2) = pickAndOrderTwoDistinct indexPicker availablePairs.Length
                        let (origIndex1, pair1, _) = availablePairs.[dex1]
                        let (origIndex2, pair2, _) = availablePairs.[dex2]
                        rsPairTracker.[origIndex1] <- (pair1, false)
                        rsPairTracker.[origIndex2] <- (pair2, false)
                        let mode = mutationModePicker ()
                        match mode with
                        | opsGenMode.SelfRefl ->
                            // Return original self-symmetric pairs
                            yield pair1
                            yield pair2
                        | opsGenMode.Ortho ->
                            // Reconfigure pairs as Ortho pairs
                            yield (fst pair1, fst pair2)
                            yield (snd pair1, snd pair2)
                        | opsGenMode.Para ->
                            // Reconfigure pairs as Para pairs
                            yield (fst pair1, snd pair2)
                            yield (fst pair2, snd pair1)
            } |> Seq.toList

        
        let perm_Si = twoOrbits |> PermSi.fromTranspositions order

        permRs.create perm_Si.Array


    /// Analyzes two distinct points in a Perm_Rs permutation to determine their symmetry properties.
    /// <param name="indexPicker">Function to select a random index from 0 to n-1.</param>
    /// <param name="permRs">The Perm_Rs permutation to analyze, which must be self-inverse.</param>
    /// <returns>A TwoOrbitPair </returns>
    /// <exception cref="ArgumentException">Thrown if permRs.Order is less than 4 or not even, or if PermPair constraints are violated.</exception>
    let findRsPoints (indexPicker: int -> int) (permRs: permRs) : twoOrbitPair =
        // Validate input
        let order = %permRs.Order
        let permArray = permRs.Array

        let _randomlyFindOtherReflectionSymmetricTwoOrbits (indexToExclude:int) =
            let rsTwoOrbs = permRs.PermSi 
                           |> PermSi.getTwoOrbits 
                           |> Array.filter(fun tob -> 
                            (tob |> TwoOrbit.isReflectionSymmetric %permRs.Order) && (tob.First <> indexToExclude))
            if rsTwoOrbs.Length = 0 then
               None
            else
               Some (rsTwoOrbs[indexPicker (rsTwoOrbs.Length)])

        // Select two distinct indices in ascending order
        let first, second = pickAndOrderTwoDistinct indexPicker (order / 2)
        
        // Compute reflection indices
        let firstReflect = reflect order first
        let secondReflect = reflect order second
        
        // Get mapped values from the permutation
        let firstMap = permArray.[first]
        let secondMap = permArray.[second]
        
        if firstReflect = firstMap then
            if secondReflect = secondMap then
                // Both points are self-symmetric
                twoOrbitPair.create order (twoOrbit.create [ first; firstMap ]) (twoOrbit.create [ second; secondMap] |> Some) 
            else
                // First point is self-symmetric, second is not
                let otherRsTwoOrbit = _randomlyFindOtherReflectionSymmetricTwoOrbits first
                match otherRsTwoOrbit with
                | Some orb -> 
                    twoOrbitPair.create order (twoOrbit.create [ first; firstMap ]) (orb |> Some) 
                | None ->
                    let secondReflectMap = permArray.[secondReflect]
                    twoOrbitPair.create order (twoOrbit.create [ second; secondMap ]) (twoOrbit.create [ secondReflect; secondReflectMap] |> Some) 
        else
            // Neither point is self-symmetric, use first point's reflection
            let firstReflectMap = permArray.[firstReflect]
            twoOrbitPair.create order (twoOrbit.create [ first; firstMap ]) (twoOrbit.create [ firstReflect; firstReflectMap] |> Some) 



    // Mutates a Perm_Rs based on opsActionMode
    let mutatePerm_Rs
            (indexPicker: int -> int) 
            (opam:opsActionMode) 
            (pRs: permRs) : permRs =

        if (opam = opsActionMode.NoAction) then
            // for NoAction mode, return the permutation as is
            pRs
        else
            let newArray = Array.copy pRs.Array
            // Find two points in the permutation to mutate
            let twoOrbitPair = findRsPoints indexPicker pRs

            let twoOrbitTypeOpt = twoOrbitPair |> TwoOrbitPairOps.getTwoOrbitPairTypeOption
            let twoOrbitPairTypeFound = 
                match twoOrbitTypeOpt with
                | Some t -> t
                | None -> failwith "TwoOrbitPair must have a valid TwoOrbitPairType"
            let secondPair = 
                match twoOrbitPair.SecondOrbit with
                | Some sto -> sto
                | None -> failwith "TwoOrbitPair must have a SecondOrbit"

            let firstL = twoOrbitPair.FirstOrbit.First
            let firstH = twoOrbitPair.FirstOrbit.Second
            let secondL = secondPair.First
            let secondH = secondPair.Second

            match opam with
            | opsActionMode.Ortho -> 
                match twoOrbitPairTypeFound with
                | twoOrbitPairType.Ortho -> ()

                | _ -> 
                    newArray.[firstL] <- secondL
                    newArray.[secondL] <- firstL 
                    newArray.[firstH] <- secondH
                    newArray.[secondH] <- firstH 

            | opsActionMode.Para -> 
                match twoOrbitPairTypeFound with
                | twoOrbitPairType.Ortho -> 
                    newArray.[firstL] <- secondL
                    newArray.[secondL] <- firstL 
                    newArray.[firstH] <- secondH
                    newArray.[secondH] <- firstH

                | twoOrbitPairType.Para -> ()

                | twoOrbitPairType.SelfRefl -> 
                    newArray.[firstL] <- secondH
                    newArray.[secondH] <- firstL 
                    newArray.[firstH] <- secondL
                    newArray.[secondL] <- firstH 

            | opsActionMode.SelfRefl -> 
                match twoOrbitPairTypeFound with
                | twoOrbitPairType.SelfRefl -> ()
                | _ -> 
                    newArray.[firstL] <- secondH
                    newArray.[secondH] <- firstL 
                    newArray.[secondL] <- firstH
                    newArray.[firstH] <- secondL 
            | opsActionMode.NoAction -> failwith "NoAction mode should not reach here"

            newArray |> permRs.createUnsafe

