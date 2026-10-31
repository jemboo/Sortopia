namespace Sortopia.Sorting.Networks

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting

module CeSorting =

    let inline sortBy< ^a when ^a: comparison> 
                (ces: ce[]) 
                (values: ^a[]) : ^a[] =

        for i = 0 to ces.Length - 1 do
            let ce = ces.[i]
            let lowIdx = (int ce.LowChannel)
            let hiIdx = (int ce.HighChannel)
            
            if values.[lowIdx] > values.[hiIdx] then
                let temp = values.[lowIdx]
                values.[lowIdx] <- values.[hiIdx]
                values.[hiIdx] <- temp
        values

    let inline sortByWithUses< ^a when ^a: comparison> 
                (ces: ce[]) 
                (useCounter: int[])
                (values: ^a[]) : ^a[] =

        for i = 0 to ces.Length - 1 do
            let ce = ces.[i]
            if values.[(int ce.LowChannel)] > values.[(int ce.HighChannel)] then
                let temp = values.[(int ce.LowChannel)]
                values.[(int ce.LowChannel)] <- values.[(int ce.HighChannel)]
                values.[(int ce.HighChannel)] <- temp
                useCounter.[i] <- useCounter.[i] + 1
        values


    let private validateHistoryInputs (ces: ce[]) (useCounter: int[]) (values: 'a[]) =
        if isNull ces then nullArg "ces"
        if isNull useCounter then nullArg "useCounter"
        if isNull values then nullArg "values"
        if useCounter.Length <> ces.Length then
            invalidArg "useCounter" "Supply one counter per comparator."
        for comparator in ces do
            if int comparator.HighChannel >= values.Length then
                invalidArg "ces" "Comparator endpoints must be within the input width."

    let private applyAndCount (comparator: ce) index (useCounter: int[]) (values: 'a[]) =
        let low, high = int comparator.LowChannel, int comparator.HighChannel
        if values.[low] > values.[high] then
            let temp = values.[low]
            values.[low] <- values.[high]
            values.[high] <- temp
            useCounter.[index] <- useCounter.[index] + 1

    /// Independent snapshots: the initial input followed by one result after every comparator.
    /// The input is unchanged. Counters accumulate actual swaps in comparator order.
    /// All dimensions and endpoints are validated before any counter is changed.
    let sortByWithHistoryAndUses (ces: ce[]) (useCounter: int[]) (values: 'a[]) : 'a[][] =
        validateHistoryInputs ces useCounter values
        let working = Array.copy values
        let history = Array.zeroCreate (ces.Length + 1)
        history.[0] <- Array.copy working
        for index in 0 .. ces.Length - 1 do
            applyAndCount ces.[index] index useCounter working
            history.[index + 1] <- Array.copy working
        history

    /// One independent snapshot AFTER each stage; the initial input is not included.
    /// Counter order is the concatenation of sequence.Stages' canonical comparator arrays.
    /// Empty stages still produce a snapshot; an empty sequence produces an empty history.
    let sortByStagesWithHistoryAndUses (sequence: stageSequence) (useCounter: int[]) (values: 'a[]) : 'a[][] =
        if obj.ReferenceEquals(sequence, null) then nullArg "sequence"
        if isNull values then nullArg "values"
        if int sequence.ChannelCount <> values.Length then
            invalidArg "values" "Input width must match the stage sequence."
        let stages = sequence.Stages |> Array.map (fun stage -> stage.Ces)
        validateHistoryInputs (Array.concat stages) useCounter values
        let working = Array.copy values
        let mutable index = 0
        stages |> Array.map (fun ces ->
            for comparator in ces do
                applyAndCount comparator index useCounter working
                index <- index + 1
            Array.copy working)
