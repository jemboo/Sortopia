namespace Sortopia.Permutations

open Sortopia.Core

open System.Collections.Generic
open FSharp.UMX

[<Struct>]
type latticePathPermutations = 
    private { 
        items: latticePathPermutation []
        level: int<latticeDistance> 
    }
    with
        static member create(items: latticePathPermutation [], level: int<latticeDistance>) =
            { items = items; level = level }
            
        member this.Level = this.level
        member this.Items = this.items
        member this.Count = this.items.Length
        member this.Item with get(i:int) = this.items.[i]


module LatticePathPermutations =

    let createLevelZero 
            (latticeDimension: int<latticeDimension>) 
            (pathLength: int<latticeDistance>) : latticePathPermutations =
        let initial = [| latticePathPermutation.createEmpty latticeDimension pathLength |]
        latticePathPermutations.create(initial, 0<latticeDistance>)

    let update (lpPerms : latticePathPermutations) 
               (lssm: latticeLevelSetMap) : latticePathPermutations =

        // 1. Determine which map and level threshold to use based on direction
        let isPoleToCenter = %lssm.PoleSideLevel < %lssm.CenterSideLevel
        
        let expectedLevel = if isPoleToCenter then lssm.PoleSideLevel else lssm.CenterSideLevel
        let map = if isPoleToCenter then lssm.PoleSideMap else lssm.CenterSideMap
        
        // 2. Validation
        if lpPerms.Level <> expectedLevel then
            invalidArg "lssm" (sprintf "Level mismatch: Expected %d, but permutations are at %d" (%expectedLevel) (%lpPerms.Level))

        // 3. Parallel Expansion
        // Array.Parallel.collect is highly efficient for branching structures.
        // It maps each path to N new paths and flattens the result into one array.
        let nextLevelIndex = %lssm.MaxDistance - %expectedLevel - 1
        
        let nextItems = 
            lpPerms.Items 
            |> Array.Parallel.collect (fun lpPerm ->
                match map.TryGetValue(lpPerm.Lp) with
                | true, mappedPoints ->
                    // For each valid move in the lattice, create a new branched path
                    mappedPoints 
                    |> List.toArray // Convert list to array for performance if not already done in setupMaps
                    |> Array.map (fun nextLp ->
                        LatticePathPermutation.updateWithLatticePoint 
                            lssm.EdgeLength 
                            nextLevelIndex 
                            nextLp 
                            lpPerm)
                | _ -> [||] // No valid moves from this point (should not happen with complete maps)
            )

        latticePathPermutations.create(nextItems, lpPerms.Level + 1<latticeDistance>)

    let toPermutations (lpPerms: latticePathPermutations) : permutation [] =
        // Fast parallel conversion to the final permutation objects
        lpPerms.Items 
        |> Array.Parallel.map LatticePathPermutation.toPermutation