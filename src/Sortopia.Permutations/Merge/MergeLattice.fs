namespace Sortopia.Permutations

open Sortopia.Core
open System
open System.Collections.Generic
open FSharp.UMX


type mergeLattice =
    private 
        { 
          latticeDimension: int<latticeDimension>
          edgeLength: int<latticeDistance> 
        }

          static member create
            (latticeDimension: int<latticeDimension>)
            (edgeLength: int<latticeDistance>)
            : mergeLattice =
            if %latticeDimension < 2 then
                invalidArg "latticeDimension" "latticeDimension length must be at least 2."
            { 
              latticeDimension = latticeDimension
              edgeLength = edgeLength
            }

          member this.LatticeDimension with get() = this.latticeDimension
          member this.EdgeLength with get() = this.edgeLength
          // one less than the number of lattice points along a path from (0,0,...,0) to (edgeLength, edgeLength, ..., edgeLength)
          member this.MaxPathLength with get() = %this.edgeLength * %this.latticeDimension


module MergeLattice =

    let create  (latticeDimension: int<latticeDimension>)
                (edgeLength: int<latticeDistance>)
        : mergeLattice =
        mergeLattice.create latticeDimension edgeLength


    let getCompletedLevelSetMapsStandard
        (ml: mergeLattice)
        : latticeLevelSetMap seq =
        
        seq {
            for llsm in LatticeLevelSetMap.getAllLevelSetMapsStandard ml.LatticeDimension ml.EdgeLength do
                    LatticeLevelSetMap.setupMaps llsm
                    yield llsm  
        }


    let getCompletedLevelSetMapsVV
        (ml: mergeLattice)
        : latticeLevelSetMap seq =
        
        seq {
            for llsm in LatticeLevelSetMap.getAllLevelSetMapsVV ml.LatticeDimension ml.EdgeLength do
                    LatticeLevelSetMap.setupMaps llsm
                    yield llsm
        }


    let getPermutationsStandard 
            (reporter: (string -> unit) option)
            (ml: mergeLattice) : latticePathPermutations =
        let mutable lpsCurrent = LatticePathPermutations.createLevelZero ml.LatticeDimension ml.MaxPathLength

        getCompletedLevelSetMapsStandard ml |> Seq.iter (
            if reporter.IsSome then
                fun lssm ->
                    reporter.Value (sprintf "Processing level set map at PoleSideLevel %d and CenterSideLevel%d"
                                        (%lssm.PoleSideLevel) (%lssm.CenterSideLevel))
                    lpsCurrent <- LatticePathPermutations.update lpsCurrent lssm
            else
            fun lssm ->
                lpsCurrent <- LatticePathPermutations.update lpsCurrent lssm
        )
        lpsCurrent


    let getPermutationsVV_1
            (reporter: (string -> unit) option)
            (ml: mergeLattice) : latticePathPermutations =
        let mutable lpsCurrent = LatticePathPermutations.createLevelZero ml.LatticeDimension ml.MaxPathLength

        getCompletedLevelSetMapsVV ml |> Seq.iter (
            if reporter.IsSome then
                fun lssm ->
                    reporter.Value (sprintf "Processing level set map at PoleSideLevel %d and CenterSideLevel%d"
                                        (%lssm.PoleSideLevel) (%lssm.CenterSideLevel))
                    lpsCurrent <- LatticePathPermutations.update lpsCurrent lssm
            else
            fun lssm ->
                lpsCurrent <- LatticePathPermutations.update lpsCurrent lssm
        )
        lpsCurrent