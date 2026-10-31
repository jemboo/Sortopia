namespace Sortopia.Core
open System
open System.Collections
open System.Collections.Generic
open System.Runtime.CompilerServices
open LanguagePrimitives

module CollectionUtils =


    // orderItems orders two items of a type that supports comparison.
    let inline orderItems< ^T when ^T : comparison> (x: ^T) (y: ^T) : ^T * ^T =
        if x <= y then (x, y) else (y, x)

    // Generic array equality check
    let inline arrayEquals< ^a when ^a: equality> (xs: ^a[]) (ys: ^a[]) : bool =
        if xs.Length <> ys.Length then false
        else
            let mutable i = 0
            let mutable equal = true
            while i < xs.Length && equal do
                equal <- xs.[i] = ys.[i]
                i <- i + 1
            equal


    //returns the last n items of the list in the original order
    let rec last n xs =
        if List.length xs <= n then xs else last n xs.Tail

    //returns the first n items of the list in the original order,
    //or all the items if it's shorter than n
    let first n (xs: 'a list) =
        let mn = min n xs.Length
        xs |> List.take mn

    let takeUpToOrWhile (n: int) (predicate: 'a -> bool) (source: seq<'a>) : seq<'a> =
        if n < 0 then
            invalidArg "n" "Number of items must be non-negative"
        Seq.takeWhile (fun x -> predicate x) (Seq.truncate n source)


    // pairsWithNext can zip two sequences of unequal length
    let pairWithNext (source: seq<'a>) : seq<'a * ('a option)> =
        seq {
            let enumerator = source.GetEnumerator()
            while enumerator.MoveNext() do
                let current = enumerator.Current
                if enumerator.MoveNext() then
                    yield (current, Some enumerator.Current)
                else
                    yield (current, None)
        }
