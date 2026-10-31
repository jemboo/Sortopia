namespace Sortopia.Sorting.Inputs

open System
open System.Collections
open System.Collections.Generic
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks

[<Struct; StructuralEquality; NoComparison>]
type sortableIntArray =
    private { 
        values: int[] 
        channelCount: int<channelCount>
        symbolSetSize: int<symbolSetSize>
    }

    static member create(values: int[], channelCount: int<channelCount>, symbolSetSize: int<symbolSetSize>) =
        if isNull values then nullArg "values"
        if channelCount < 0<channelCount> then
            invalidArg "channelCount" "Channel count must be non-negative."
        if values.Length <> int channelCount then
            invalidArg "values" $"Values length ({values.Length}) must equal channel count ({int channelCount})."
        if %symbolSetSize <= 0 then
            invalidArg "symbolSetSize" "Symbol set size must be positive."
        let storedValues = Array.copy values

        { values = storedValues; channelCount = channelCount;
          symbolSetSize = symbolSetSize }

    member this.ToSortableBoolArrays() : sortableBoolArray[] =
        if this.ChannelCount <= 1<channelCount> then
            [||]
        else
            let thresholds = [| 0 .. %this.channelCount |]
            let vals = this.values
            let sw = this.ChannelCount
            thresholds 
            |> Array.map (
                fun threshold ->
                    let boolValues = vals |> Array.map (fun v -> v >= threshold )
                    sortableBoolArray.create(boolValues, sw))

    static member createFromPermutation(perm: permutation) =
        sortableIntArray.create(perm.Array, (%perm.Order |> UMX.tag<channelCount>), (%perm.Order |> UMX.tag<symbolSetSize>))

    /// Gets a copy of the values array.
    member this.Values with get() = Array.copy this.values

    /// Gets a value without exposing the backing array.
    member this.Item
        with get(index: int) = this.values.[index]
    

    /// Gets the channel count.
    member this.ChannelCount = this.channelCount

    /// Gets the symbol set size.
    member this.SymbolSetSize = this.symbolSetSize

    /// Checks if the values array is sorted in non-decreasing order.
    member this.IsSorted = ArrayUtils.isSorted this.values
    
    /// Initial sortable followed by the result of every comparator. The original sortable is unchanged.
    member this.SortByCesWithHistory (ces: ce[]) (useCounter: int[]) : sortableIntArray[] =
        let width, symbols = this.ChannelCount, this.SymbolSetSize
        CeSorting.sortByWithHistoryAndUses ces useCounter this.values
        |> Array.map (fun values -> sortableIntArray.create(values, width, symbols))

    /// Exactly one sortable after each stage; no initial-input entry. Counters follow canonical stage order.
    member this.SortByStagesWithHistory (sequence: stageSequence) (useCounter: int[]) : sortableIntArray[] =
        let width, symbols = this.ChannelCount, this.SymbolSetSize
        CeSorting.sortByStagesWithHistoryAndUses sequence useCounter this.values
        |> Array.map (fun values -> sortableIntArray.create(values, width, symbols))

    interface IEnumerable<int> with
        member this.GetEnumerator() =
            (this.values :> seq<int>).GetEnumerator()

    interface IEnumerable with
        member this.GetEnumerator() =
            (this.values :> IEnumerable).GetEnumerator()

module SortableIntArray =
    // Custom comparer for sortableIntArray based only on Values

    type SortableIntArrayValueComparer() =
        interface IEqualityComparer<sortableIntArray> with
            member _.Equals(x, y) = x.values = y.values
            member _.GetHashCode(obj) = hash obj.values

    /// Deduplicates ordered values; symbol-set metadata does not affect this projection.
    let removeDuplicates (arr: sortableIntArray[]) : sortableIntArray[] =
        if arr = null || arr.Length <= 1 then arr
        else
            // Uses framework hashing and exact structural equality.
            let set = HashSet<sortableIntArray>(SortableIntArrayValueComparer())
            for i = 0 to arr.Length - 1 do
                set.Add(arr.[i]) |> ignore
            
            let result = Array.zeroCreate set.Count
            set.CopyTo(result)
            result

    let getOrbit (maxCount: int) (perm: permutation) : sortableIntArray seq =
        Permutation.powerSequence perm  
        |> CollectionUtils.takeUpToOrWhile maxCount (fun perm -> not (Permutation.isIdentity perm))
        |> Seq.map sortableIntArray.createFromPermutation

    let getMergeTestCases 
            (channelCount: int<channelCount>)
            (mergeDimension: int<mergeDimension>)
            (mergeFillType: mergeSuffixType)
            : sortableIntArray [] =

        if int mergeDimension < 1 then invalidArg "mergeDimension" "Merge dimension must be positive."
        if int channelCount < 1 then invalidArg "channelCount" "Channel count must be positive."
        if %channelCount % %mergeDimension <> 0 then
            invalidArg "channelCount" (sprintf "Channel count: %d must be divisible by mergeDimension : %d " %channelCount  %mergeDimension)

        let latticeDimension = %mergeDimension |> UMX.tag<latticeDimension>
        let edgeLength = %channelCount / %mergeDimension |> UMX.tag<latticeDistance>
        let mergeLattice = MergeLattice.create latticeDimension edgeLength
        let sortableIntArrays =
            match mergeFillType with
            | NoSuffix -> 
                MergeLattice.getPermutationsStandard None mergeLattice
                |> LatticePathPermutations.toPermutations
                |> Array.map(sortableIntArray.createFromPermutation)
            | VV_1 ->
                MergeLattice.getPermutationsVV_1 None mergeLattice
                |> LatticePathPermutations.toPermutations
                |> Array.map(sortableIntArray.createFromPermutation)

        sortableIntArrays
