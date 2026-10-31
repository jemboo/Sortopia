namespace Sortopia.Sorting.Networks

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting

 type sorterSet =
    private { 
        sorterSetId: Guid<sorterSetId>
        sorters: sorter array
    }

    static member create (sorterSetId: Guid<sorterSetId>)
                         (sorters: sorter array) : sorterSet =
        if isNull sorters then nullArg "sorters"
        { sorterSetId = sorterSetId; sorters = Array.copy sorters; }

    member this.Id with get() = this.sorterSetId
    member this.Sorters with get() : sorter[] = Array.copy this.sorters

 module SorterSet = 
    
    let mergeSorterSets (sorterSets: sorterSet []) : sorterSet =
        let idSet = sorterSets |> Array.map(fun ss -> ss.Id |> UMX.untag :> obj)
        let mergedId = GuidUtils.guidFromObjs idSet |> UMX.tag<sorterSetId>
        let mergedSorters = sorterSets |> Array.collect(fun ss -> ss.Sorters)
        sorterSet.create mergedId mergedSorters
