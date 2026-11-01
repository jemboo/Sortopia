namespace Sortopia.Core
open System
open FSharp.UMX

module Combinatorics =

    // For deterministic testing
    let indexPicker (indices: int[]) : int -> int =
        let mutable counter = 0
        fun _ ->
            let result = indices.[counter % indices.Length]
            counter <- counter + 1
            result

    // For deterministic testing
    let floatPicker (values: float[]) : unit -> float =
        let mutable counter = 0
        fun () ->
            let result = values.[counter % values.Length]
            counter <- counter + 1
            result


    /// Reflects a value across a modulus, computing modulus - value - 1.
    /// <param name="modulus">The modulus for reflection (must be positive).</param>
    /// <param name="value">The value to reflect (must be non-negative and less than modulus).</param>
    /// <returns>The reflected value.</returns>
    let inline reflect (width: int) (index: int) : int = (width - 1) - index


    /// Computes the Cartesian product of a list of key-value pairs, yielding all possible maps.
    let rec cartesianProductMaps (lists: ('key * 'value list) list) : Map<'key, 'value> seq =
        match lists with
        | [] -> Seq.empty<Map<'key, 'value>>
        | [(key, values)] ->
            seq { for value in values do yield Map.ofList [(key, value)] }
        | (key, values)::tl ->
            seq {
                for value in values do
                    for restMap in cartesianProductMaps tl do
                        yield Map.add key value restMap
            }


    /// Performs a Fisher-Yates shuffle, yielding a sequence of shuffled items.
    /// <param name="indexShuffler">A function generating random indices in [0, nLeft).</param>
    /// <param name="initialList">The input array to shuffle.</param>
    /// <returns>A sequence of shuffled items.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when initialList is null.</exception>
    /// <exception cref="System.ArgumentException">Thrown when indexShuffler returns an invalid index.</exception>
    let fisherYatesShuffle (indexShuffler: int -> int) 
                           (initialList: 'a array) : 'a seq =
        let availableFlags = Array.init initialList.Length (fun i -> (i, true))
    
        let nextItem nLeft =
            let nItem = indexShuffler nLeft // Random index from remaining items
            let index =
                availableFlags
                |> Array.filter (fun (_, flag) -> flag)
                |> Array.item nItem
                |> fst
            availableFlags.[index] <- (index, false) // Mark as used
            initialList.[index]
    
        seq { for n in initialList.Length .. -1 .. 1 -> nextItem n }


    /// Picks two distinct numbers from 0 to n-1 and returns them in ascending order.
    /// <param name="indexPicker">A function generating random indices in [0, n).</param>
    /// <param name="n">The range of indices (must be at least 2).</param>
    /// <returns>A tuple of two distinct indices in ascending order.</returns>
    /// <exception cref="System.ArgumentException">Thrown when n is less than 2 or indexPicker returns invalid indices.</exception>
    let pickAndOrderTwoDistinct (indexPicker: int -> int) (n: int) : int * int =
        if n < 2 then invalidArg "n" "n must be at least 2 to pick two distinct numbers"
        let first = indexPicker n
        if first < 0 || first >= n then invalidArg "indexPicker" $"First index {first} out of range [0, {n})"
        let second = 
            let rec pickDistinct () =
                let candidate = indexPicker n
                if candidate < 0 || candidate >= n then invalidArg "indexPicker" $"Second index {candidate} out of range [0, {n})"
                if candidate <> first then candidate
                else pickDistinct ()
            pickDistinct ()
        if first < second then (first, second) else (second, first)


    /// Picks a single item from a weighted array.
    /// <param name="items">An array of (item, weight) pairs, where weights are non-negative.</param>
    /// <param name="floatPicker">A function generating random floats in [0,1).</param>
    /// <returns>A randomly selected item based on weights.</returns>
    /// <exception cref="System.ArgumentException">Thrown when items is empty or weights are invalid.</exception>
    let pick (items: ('a * float)[]) (floatPicker: unit -> float) : 'a =
        if items.Length = 0 then invalidArg "items" "Item list cannot be empty"
        if items |> Array.exists (fun (_, w) -> w < 0.0) then invalidArg "items" "Weights must be non-negative"
        let totalWeight = items |> Array.sumBy snd
        if totalWeight <= 0.0 then invalidArg "items" "Total weight must be positive"
        let r = floatPicker() * totalWeight
        let mutable weightSum = 0.0
        let mutable selectedItem = fst items.[items.Length - 1] // Fallback to last item
        let mutable i = 0
        while i < items.Length && weightSum <= r do
            let (item, weight) = items.[i]
            weightSum <- weightSum + weight
            if weightSum >= r then
                selectedItem <- item
            i <- i + 1
        selectedItem


    // predicate: true iff array is non-decreasing (a[i] <= a[i+1] for all i)
    let private isNonDecreasing (arr: int[]) =
        let n = arr.Length
        let mutable i = 0
        let mutable ok = true
        while i < n - 1 && ok do
            if arr.[i] > arr.[i+1] then ok <- false
            i <- i + 1
        ok


    /// Given an integer array "subject" and a maximum value "maxVal",
    /// generates all integer arrays that can be formed by incrementing
    /// a single element of "subject" by 1, without exceeding "maxVal - 1".
    let getOverCovers (subject:int[]) (maxVal:int) : int[][] =
        let n = subject.Length
        let results = ResizeArray<int[]>()

        for i in 0 .. n-1 do
            // Only valid if increment doesn't exceed maxVal-1
            if subject.[i] + 1 < maxVal then
                let arr = Array.copy subject
                arr.[i] <- arr.[i] + 1
                results.Add arr

        results.ToArray()

    /// Given an integer array "subject",
    /// generates all integer arrays that can be formed by decrementing
    /// a single element of "subject" by 1, without going below 0.
    let getUnderCovers (subject:int[]) : int[][] =
        let n = subject.Length
        let results = ResizeArray<int[]>()
        for i in 0 .. n-1 do
            // Only valid if decrement doesn't go below 0
            if subject.[i] - 1 >= 0 then
                let arr = Array.copy subject
                arr.[i] <- arr.[i] - 1
                results.Add arr
        results.ToArray()


    /// Non-decreasing version of getUnderCovers
    let getUnderCoversVV (subject:int[]) : int[][] =
        getUnderCovers subject
        |> Array.filter isNonDecreasing


    /// Non-decreasing version of getOverCovers
    let getOverCoversVV (subject:int[]) (maxVal:int) : int[][] =
        // simple +1 approach
        let candidate = Array.map (fun x -> x + 1) subject
        if Array.exists (fun x -> x >= maxVal) candidate then
            [||]  // violates maxVal constraint
        else if isNonDecreasing candidate then
            [| candidate |]
        else
            [||]
