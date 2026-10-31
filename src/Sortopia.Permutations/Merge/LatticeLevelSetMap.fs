namespace Sortopia.Permutations

open Sortopia.Core

open System.Collections.Generic
open FSharp.UMX

type coverType =
    | FullCover = 0
    | VVCover = 1

type latticeLevelSetMap =
    private 
        { centerSideMap: Dictionary<latticePoint, latticePoint list>
          poleSideMap: Dictionary<latticePoint, latticePoint list>
          latticeDimension: int<latticeDimension>
          edgeLength: int<latticeDistance>
          poleSideLevel: int<latticeDistance>
          centerSideLevel: int<latticeDistance> 
          coverStrategy: coverType }

    static member create
        (latticeDimension: int<latticeDimension>)
        (edgeLength: int<latticeDistance>)
        (poleSideLevel: int<latticeDistance>)
        (centerSideLevel: int<latticeDistance>)
        (coverType: coverType)
        : latticeLevelSetMap =
        
        if %latticeDimension < 2 then
            invalidArg "latticeDimension" "Dimension must be at least 2."

        // Determine which keyMaker to use based on strategy
        let keyMaker = 
            match coverType with
            | coverType.FullCover -> LatticePoint.getLevelSet
            | coverType.VVCover   -> LatticePoint.getLevelSetVV
            | _ -> failwith "Unknown cover strategy"

        let centerSideMap = Dictionary<latticePoint, latticePoint list>()
        for point in keyMaker latticeDimension centerSideLevel edgeLength do
            centerSideMap.[point] <- []

        let poleSideMap = Dictionary<latticePoint, latticePoint list>()
        for point in keyMaker latticeDimension poleSideLevel edgeLength do
            poleSideMap.[point] <- []

        { centerSideMap = centerSideMap
          poleSideMap = poleSideMap
          latticeDimension = latticeDimension
          edgeLength = edgeLength
          poleSideLevel = poleSideLevel
          centerSideLevel = centerSideLevel
          coverStrategy = coverType }

    member private this.UnderCover(p: latticePoint) =
        match this.coverStrategy with
        | coverType.FullCover -> LatticePoint.getUnderCovers p
        | coverType.VVCover   -> LatticePoint.getUnderCoversVV p
        | _ -> [||]

    member private this.OverCover(p: latticePoint) =
        match this.coverStrategy with
        | coverType.FullCover -> LatticePoint.getOverCovers p this.EdgeLength
        | coverType.VVCover   -> LatticePoint.getOverCoversVV p this.EdgeLength
        | _ -> [||]

    // --- Public Members ---

    member this.CenterSideMap = this.centerSideMap
    member this.PoleSideMap = this.poleSideMap
    member this.CoverStrategy = this.coverStrategy
    member this.LatticeDimension = this.latticeDimension
    member this.EdgeLength = this.edgeLength    
    member this.MaxDistance with get() = this.edgeLength * this.latticeDimension
    member this.PoleSideLevel = this.poleSideLevel
    member this.CenterSideLevel = this.centerSideLevel
    
    member this.getPoleSidePointCandidates (centerPoint: latticePoint) =
        if (%this.CenterSideLevel > %this.PoleSideLevel) then
            this.UnderCover centerPoint
        else
            this.OverCover centerPoint

    member this.getCenterSidePointCandidates (polePoint: latticePoint) =
        if (%this.CenterSideLevel < %this.PoleSideLevel) then
            this.UnderCover polePoint
        else
            this.OverCover polePoint


module LatticeLevelSetMap =

    let getAllLevelSetMaps 
            (latticeDimension: int<latticeDimension>) 
            (edgeLength: int<latticeDistance>) 
            (coverType: coverType)
            : latticeLevelSetMap seq = 

        let maxPathLength = %edgeLength * %latticeDimension
        let midPoint = maxPathLength / 2
        
        seq {
            // Toward Center
            for level in 1 .. midPoint do
                let levelTag = UMX.tag<latticeDistance> level
                yield latticeLevelSetMap.create 
                        latticeDimension 
                        edgeLength 
                        (levelTag - 1<latticeDistance>)
                        levelTag
                        coverType

            // Toward Pole
            for level in midPoint .. (%maxPathLength - 1) do
                let levelTag = UMX.tag<latticeDistance> level
                yield latticeLevelSetMap.create 
                        latticeDimension 
                        edgeLength 
                        (levelTag + 1<latticeDistance>)
                        levelTag
                        coverType
        }

    // Facade methods are now just passing an Enum
    let getAllLevelSetMapsStandard dim edge = 
        getAllLevelSetMaps dim edge coverType.FullCover

    let getAllLevelSetMapsVV dim edge = 
        getAllLevelSetMaps dim edge coverType.VVCover


    let setupMaps
            (llsm: latticeLevelSetMap) : unit =

        let poleSideKeys = llsm.PoleSideMap.Keys |> Seq.toArray |> Array.sort
        for lp in poleSideKeys do
            let ccpCandidates =
                llsm.getCenterSidePointCandidates lp
            let ccpWinners = 
                       ccpCandidates
                       |> Array.sort
                       |> Array.filter(fun ccp -> llsm.CenterSideMap.[ccp].Length = 0)

            let ccp = if (ccpWinners.Length = 0) then 
                          ccpCandidates.[0]
                      else
                         ccpWinners.[0]
                         // failwith "Failed to initialize level set map: no available center side points."

            llsm.PoleSideMap.[lp] <- ccp :: llsm.PoleSideMap.[lp]
            llsm.CenterSideMap.[ccp] <- lp :: llsm.CenterSideMap.[ccp]

        for lp in llsm.CenterSideMap.Keys do
            match llsm.CenterSideMap.[lp] with
            | [] ->
                let pspCandidates = llsm.getPoleSidePointCandidates lp
                if (not (llsm.PoleSideMap.ContainsKey(pspCandidates.[0]))) then
                    failwith "Failed to initialize level set map: pole side point not found in map."    

                llsm.CenterSideMap.[lp] <- [ pspCandidates.[0] ]

                llsm.PoleSideMap.[pspCandidates.[0]] <-
                    lp :: llsm.PoleSideMap.[pspCandidates.[0]]

            | _ -> ()
