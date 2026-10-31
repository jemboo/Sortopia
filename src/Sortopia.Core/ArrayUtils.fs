namespace Sortopia.Core

open System
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Runtime.Intrinsics
open LanguagePrimitives


module ArrayUtils =
    
    // returns arrayLength arrays of length arrayLength, where each array is a rotated version 
    //of the previous one, the first one being trueCount trues followed by (arrayLength - trueCount) falses.
    let makeRotatingFlags (arrayLength: int) (trueCount: int) : bool[][] =
        if trueCount < 0 || trueCount > arrayLength then
            invalidArg "trueCount" "trueCount must be between 0 and arrayLength."
        let baseArray = Array.init arrayLength (fun i -> i < trueCount)
        Array.init arrayLength (fun i ->
            Array.init arrayLength (fun j ->
                baseArray.[(i + j) % arrayLength]
            )
        )


    let inline distanceSquared< ^a when ^a: (static member Zero: ^a)
                                        and ^a: (static member (+): ^a * ^a -> ^a)
                                        and ^a: (static member (-): ^a * ^a -> ^a)
                                        and ^a: (static member (*): ^a * ^a -> ^a)>
                    (a: ^a[]) (b: ^a[]) : ^a =
        let mutable acc = GenericZero<^a>
        let mutable i = 0
        while i < a.Length do
            acc <- acc + (a.[i] - b.[i]) * (a.[i] - b.[i])
            i <- i + 1
        acc


    let inline distanceSquaredOffset< ^a when ^a: (static member Zero: ^a)
                                        and ^a: (static member (+): ^a * ^a -> ^a)
                                        and ^a: (static member (-): ^a * ^a -> ^a)
                                        and ^a: (static member (*): ^a * ^a -> ^a)>
            (longArray: ^a[]) (shortArray: ^a[]) : ^a[] =
        let n = shortArray.Length
        let m = longArray.Length / n
        let result = Array.zeroCreate m
        for i = 0 to m - 1 do
            let mutable acc = GenericZero<^a>
            for j = 0 to n - 1 do
                let diff = longArray.[i * n + j] - shortArray.[j]
                acc <- acc + diff * diff
            result.[i] <- acc
        result


    let inline isSorted< ^a when ^a: comparison> (values: ^a[]) : bool =
        let len = values.Length
        let mutable ok = true
        let mutable i = 0
        while i < len - 1 && ok do
            if values.[i] > values.[i+1] then 
                ok <- false
            else 
                i <- i + 1
        ok


    let lastNonZeroIndex (arr: int array) : int =
        let rec loop i =
            if i < 0 then
                -1  // Return -1 if no non-zero value is found
            elif arr.[i] <> 0 then
                i
            else
                loop (i - 1)
        loop (arr.Length - 1)

    /// Finds the first index at which two arrays differ. 
    /// Throws an exception if the arrays are identical or of different lengths.
    let firstDiffIndexOrThrow (a: 'T[]) (b: 'T[]) =
        if a.Length <> b.Length then
            invalidArg "b" "Arrays must have the same length"

        let mutable i = 0
        let mutable found = -1

        while i < a.Length && found = -1 do
            if a.[i] <> b.[i] then
                found <- i
            i <- i + 1

        if found >= 0 then
            found
        else
            invalidOp "Arrays are identical; no differing index found"
