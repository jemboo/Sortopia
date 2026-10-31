
namespace Sortopia.Permutations

open Sortopia.Core

open System
open FSharp.UMX

[<Measure>] type latticeDistance
[<Measure>] type latticeDimension

[<Struct; CustomEquality; CustomComparison>]
type latticePoint = 
    private { 
        coords: int[]
        hashCode: int 
    }
    with
        static member create(arr: int[]) = 
            let copy = Array.copy arr
            let mutable h = 17
            for x in copy do h <- h * 31 + x
            { coords = copy; hashCode = h }

        static member createUnsafe(arr: int[], h: int) = 
                { coords = arr; hashCode = h }
        member p.Coords = p.coords
        member p.Dimension = p.coords.Length
        member this.Item with get(i:int) = this.coords.[i]
        member this.Sum with get() = Array.sum this.coords

        override p.GetHashCode() = p.hashCode
        
        override p.Equals(obj) =
            match obj with
            | :? latticePoint as other ->
                p.hashCode = other.hashCode && 
                ReadOnlySpan(p.coords).SequenceEqual(ReadOnlySpan(other.coords))
            | _ -> false

        interface IComparable with
            member this.CompareTo(obj) =
                match obj with
                | :? latticePoint as other ->
                    let lenCompare = compare this.coords.Length other.coords.Length
                    if lenCompare <> 0 then lenCompare
                    else
                        ReadOnlySpan(this.coords).SequenceCompareTo(ReadOnlySpan(other.coords))
                | _ -> invalidArg "obj" "Cannot compare latticePoint"


module LatticePoint =

    let isNonDecreasing (p: latticePoint) =
        let span = ReadOnlySpan(p.Coords)
        let mutable ok = true
        let mutable i = 0
        while i < span.Length - 1 && ok do
            if span.[i] > span.[i+1] then ok <- false
            i <- i + 1
        ok


    /// Generate all lattice points where each coordinate is in 0..maxValue
    let latticeCube 
            (dim:int<latticeDimension>) 
            (edgeLength:int<latticeDistance>) : seq<latticePoint> =
        seq {
            let current = Array.zeroCreate %dim

            let rec loop pos =
                seq {
                    if pos = %dim then
                        yield latticePoint.create (Array.copy current)
                    else
                        for v in 0 .. %edgeLength do
                            current.[pos] <- v
                            yield! loop (pos + 1)
                }

            yield! loop 0
        }


    /// Generates all lattice points of length dim whose entries:
    /// • sum to latticelevel
    /// • lie in 0 .. maxDistance
    let getLevelSet 
            (dim: int<latticeDimension>) 
            (latticeLevel: int<latticeDistance>) 
            (edgeLength: int<latticeDistance>) : seq<latticePoint> =
    
        let results = ResizeArray<latticePoint>()
        let d = %dim
        let edge = %edgeLength
        let level = %latticeLevel
    
        // Pre-allocate a single buffer to work in
        let current = Array.zeroCreate d

        let rec loop pos sumRemaining =
            if pos = d then
                if sumRemaining = 0 then
                    // Only copy and create the struct when we have a winner
                    results.Add(latticePoint.create current)
            else
                let slotsLeft = d - pos
            
                // Tighten bounds to avoid impossible branches
                let lowerBound = max 0 (sumRemaining - edge * (slotsLeft - 1))
                let upperBound = min edge sumRemaining

                for v in lowerBound .. upperBound do
                    current.[pos] <- v
                    loop (pos + 1) (sumRemaining - v)

        loop 0 level
        results :> seq<latticePoint>


    let getLevelSetVV
            (dim: int<latticeDimension>) 
            (latticeLevel: int<latticeDistance>) 
            (maxDistance: int<latticeDistance>) : seq<latticePoint> =
        
        let d = %dim
        let edge = %maxDistance
        let level = %latticeLevel

        let rec generate (pos: int) (sumRemaining: int) (lastValue: int) (current: int[]) : seq<latticePoint> =
            seq {
                if pos = d - 1 then
                    // The last element is determined by the remaining sum
                    if sumRemaining >= lastValue && sumRemaining <= edge then
                        current.[pos] <- sumRemaining
                        // We MUST copy the array here so the yielded object 
                        // owns its own data, independent of the recursive buffer.
                        yield latticePoint.create (Array.copy current)
                else
                    let slotsLeft = d - pos
                    
                    // Standard non-decreasing bounds:
                    // 1. v >= lastValue
                    // 2. v <= average remaining (to allow room for non-decreasing growth)
                    // 3. v <= edge
                    let lowerBound = lastValue
                    let upperBound = min edge (sumRemaining / slotsLeft)

                    for v in lowerBound .. upperBound do
                        current.[pos] <- v
                        yield! generate (pos + 1) (sumRemaining - v) v current
            }

        if level < 0 || level > d * edge then
            Seq.empty
        else
            let buffer = Array.zeroCreate d
            generate 0 level 0 buffer


    let getUnderCovers (subject:latticePoint) : latticePoint[] =
        let n = subject.Dimension
        let results = ResizeArray<latticePoint>()
        for i in 0 .. n-1 do
            if subject.[i] - 1 >= 0 then
                let arr = Array.copy subject.coords
                arr.[i] <- arr.[i] - 1
                results.Add (latticePoint.create arr)
        results.ToArray()


    let getOverCovers (subject:latticePoint) (maxDistance:int<latticeDistance>) : latticePoint[] =
        let n = subject.Dimension
        let results = ResizeArray<latticePoint>()
        for i in 0 .. n-1 do
            if subject.[i] < %maxDistance then
                let arr = Array.copy subject.coords
                arr.[i] <- arr.[i] + 1
                results.Add (latticePoint.create arr)
        results.ToArray()


    let getUnderCoversVV (subject:latticePoint) : latticePoint[] =
        getUnderCovers subject |> Array.filter isNonDecreasing


    let getOverCoversVV (subject:latticePoint) (maxDistance:int<latticeDistance>) : latticePoint[] =
        getOverCovers subject maxDistance |> Array.filter isNonDecreasing


    /// the two lattice points must be the same dimension and differ in at least one coordinate
    let getFirstDifferingIndex (lowPoint: latticePoint) (hiPoint: latticePoint) : int =
        ArrayUtils.firstDiffIndexOrThrow lowPoint.Coords hiPoint.Coords


    // This is used by updateWithLatticePoint to convert a path in the mergeLattice to a permutation.
    // Place (level - 1) in the index returned, where level here is the level of the lowPoint
    let getPermutationIndex 
                (edgeLength: int<latticeDistance>) 
                (lowPoint:latticePoint) 
                (hiPoint:latticePoint) : int =
        let index = getFirstDifferingIndex lowPoint hiPoint
        %edgeLength * index +  %edgeLength - lowPoint.coords.[index] - 1
