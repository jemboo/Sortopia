namespace Sortopia.Sorting.Networks

open FSharp.UMX
open Sortopia.Sorting

type internal stageBuilder = 
        private { 
                    channelCount: int<channelCount>;
                    ces: ResizeArray<ce>;  
                    occupied: bool[] 
                } with


        member this.Ces with get() = this.ces.ToArray()


        member this.AddCe(ce: ce) =
            if this.occupied.[(int ce.LowChannel)] then
                invalidArg "ce" $"Cannot add CE {ce} because position {(int ce.LowChannel)} is already occupied."
            if this.occupied.[(int ce.HighChannel)] then
                invalidArg "ce" $"Cannot add CE {ce} because position {(int ce.HighChannel)} is already occupied."
            this.ces.Add(ce)
            this.occupied.[(int ce.LowChannel)] <- true
            this.occupied.[(int ce.HighChannel)] <- true

        member this.CanAddCe(ce: ce) =
            not (this.occupied.[(int ce.LowChannel)] || this.occupied.[(int ce.HighChannel)])

// Core module for Stage operations

type internal stageBuilderSequence = 
        private { 
                    channelCount: int<channelCount>;
                    stageBuilders: ResizeArray<stageBuilder>;  
                } with

        static member create (channelCount: int<channelCount>) : stageBuilderSequence =
            if channelCount <= 1<channelCount> then
                invalidArg "channelCount" "Channel count must be greater than 1."
            { channelCount = channelCount; stageBuilders = ResizeArray<stageBuilder>() }


        member this.StageBuilders with get() = this.stageBuilders.ToArray()

        member this.StageLength with get() = this.stageBuilders.Count |> UMX.tag<stageLength>

        member this.AddCe (ce: ce) :  unit =
            if (int ce.LowChannel) >= (this.channelCount |> int) || (int ce.HighChannel) >= (this.channelCount |> int) then
                invalidArg "ce" $"CE {ce} has indices out of bounds for channel count {this.channelCount}."

            // Find the last stage that CANNOT accept the CE
            let firstBlockedIndex = 
                this.stageBuilders 
                |> Seq.tryFindIndexBack (fun sb -> not (sb.CanAddCe(ce)))

            // If a stage is blocked at index `i`, the target is the one right after it (`i + 1`)
            // If no stages are blocked, it means every stage from the end back to 0 can accept it, 
            // so the earliest available stage is index 0.
            let targetStageIndex = 
                match firstBlockedIndex with
                | Some i -> i + 1
                | None   -> 0

            // If the calculated index is beyond our existing stages, it means we need a new stage
            if targetStageIndex >= this.stageBuilders.Count then
                let newStage = 
                    { 
                        channelCount = this.channelCount
                        ces = ResizeArray<ce>()
                        occupied = Array.create (this.channelCount |> int) false
                    }
                newStage.AddCe(ce)
                this.stageBuilders.Add(newStage)
            else
                // Add to the earliest stage that can accept the CE
                this.stageBuilders.[targetStageIndex].AddCe(ce)

module StageBuilderSequence =

    let private fromCes (channelCount : int<channelCount>) (ces: ce array) : stageBuilderSequence =
        if isNull ces then nullArg "ces"
        if ces |> Array.exists (fun c -> int c.HighChannel >= int channelCount) then
            invalidArg "ces" "Comparator endpoints must be within the network width."
        let stageBuilderSeq = stageBuilderSequence.create(channelCount)
        ces |> Array.iter stageBuilderSeq.AddCe
        stageBuilderSeq

    let toStageSequence (channelCount: int<channelCount>) (ces: ce array) : stageSequence =
        let stageBuilderSeq = fromCes channelCount ces
        let stages = stageBuilderSeq.StageBuilders |> Array.map(fun st -> stage.create st.Ces channelCount)
        stageSequence.create stages channelCount

    /// Returns (stageSequence, movedStages)
    /// 
    /// movedStages is array-of-arrays with the same shape as stageSeq.Stages:
    /// movedStages.[stageIndex].[ceIndexInStage] = how many stages back this CE was moved
    /// when the greedy algorithm placed it.
    /// Returns (stageSequence, movedStages)
    /// 
    /// movedStages : int[][] matches the structure of stageSeq.Stages
    /// movedStages.[stageIdx].[posInStage] = how many stages back this CE was moved.
    let toStageSequenceWithMoveInfo (channelCount: int<channelCount>) (ces: ce array) =
        if isNull ces then nullArg "ces"
        if ces |> Array.exists (fun c -> int c.HighChannel >= int channelCount) then
            invalidArg "ces" "Comparator endpoints must be within the network width."
        let stageBuilderSeq = stageBuilderSequence.create(channelCount)
        
        // Per-stage lists of (stagesMoved, originalIndex)
        let moveTracker = ResizeArray<ResizeArray<int * int>>()

        ces |> Array.iteri (fun origIdx ce ->
            let currentCount = stageBuilderSeq.stageBuilders.Count

            let firstBlockedIndex =
                stageBuilderSeq.stageBuilders
                |> Seq.tryFindIndexBack (fun sb -> not (sb.CanAddCe(ce)))

            let targetIdx =
                match firstBlockedIndex with
                | Some i -> i + 1
                | None -> 0

            // === Updated movement calculation ===
            let stagesMoved =
                if targetIdx >= currentCount then
                    0                                   // Created a new stage
                else
                    currentCount - targetIdx - 1        // How many stages it jumped forward

            // Record the move info
            while moveTracker.Count <= targetIdx do
                moveTracker.Add(ResizeArray<int * int>())
            moveTracker.[targetIdx].Add(stagesMoved, origIdx)

            // Add the CE to the builder (existing logic)
            if targetIdx >= currentCount then
                let newSb =
                    {
                        channelCount = channelCount
                        ces = ResizeArray<ce>()
                        occupied = Array.create (channelCount |> int) false
                    }
                newSb.AddCe(ce)
                stageBuilderSeq.stageBuilders.Add(newSb)
            else
                stageBuilderSeq.stageBuilders.[targetIdx].AddCe(ce)
        )

        // Build final immutable stageSequence
        let stages = 
            stageBuilderSeq.StageBuilders 
            |> Array.map (fun sb -> stage.create sb.Ces channelCount)

        let stageSeq = stageSequence.create stages channelCount

        // Build movedStages array-of-arrays aligned with final sorted stages
        let movedStages =
            stageBuilderSeq.StageBuilders
            |> Array.mapi (fun sIdx sb ->
                let movesWithOrig = moveTracker.[sIdx]
                
                // Sort in the same order as the stage (by Low, Hi)
                let paired =
                    Array.zip sb.Ces (movesWithOrig |> Seq.toArray)
                    |> Array.sortBy (fun (c, _) -> (int c.LowChannel), (int c.HighChannel))
                
                paired |> Array.map (fun (_, (moved, _)) -> moved)
            )

        stageSeq, movedStages
