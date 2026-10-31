namespace Sortopia.Permutations

open Sortopia.Core
open System
open Combinatorics

type indelMode =
    | NoAction
    | Mutation
    | Insertion
    | Deletion


[<Struct; StructuralEquality; NoComparison>]
type indelRates = 
    private 
        { mutationThresh: float; insertionThresh: float; deletionThresh: float }

    static member create (mutationRate: float, insertionRate: float, deletionRate: float) : indelRates =
        let noAction = 1.0 - mutationRate - insertionRate - deletionRate
        let epsilon = 1e-10
        if mutationRate < 0.0 || mutationRate > 1.0 then failwith "mutationRate must be between 0 and 1"
        else if insertionRate < 0.0 || insertionRate > 1.0 then failwith "insertionRate must be between 0 and 1"
        else if deletionRate < 0.0 || deletionRate > 1.0 then failwith "deletionRate must be between 0 and 1"
        else if noAction < -epsilon then failwith "Sum of Indel rates must not exceed 1.0"
        {
            mutationThresh = mutationRate
            insertionThresh = mutationRate + insertionRate
            deletionThresh = mutationRate + insertionRate + deletionRate
        }

    static member createMod (modificationRate: float, mutationRate: float, 
                             insertionRate: float, deletionRate: float) : indelRates = 
        let adj = modificationRate / (mutationRate + insertionRate + deletionRate)
        indelRates.create (adj*mutationRate, adj*insertionRate, adj*deletionRate)

    member this.MutationRate with get() = this.mutationThresh
    member this.InsertionRate with get() = this.insertionThresh - this.mutationThresh
    member this.DeletionRate with get() = this.deletionThresh - this.insertionThresh
    member this.NoActionRate with get() = 1.0 - this.deletionThresh
    member this.toString() =
        sprintf "IndelRates(Mutation: %.2f, Insertion: %.2f, Deletion: %.2f)" 
                this.MutationRate this.InsertionRate this.DeletionRate  

    /// Assumes that floatPicker returns a float in the range [0.0, 1.0).
    member this.PickMode (floatPicker: unit -> float) : indelMode =
        let r = floatPicker()
        if r < this.mutationThresh then
            indelMode.Mutation
        elif r < this.insertionThresh then
            indelMode.Insertion
        elif r < this.deletionThresh then
            indelMode.Deletion
        else
        indelMode.NoAction

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("indelRates")
            GuidUtils.writeStableFloat writer this.mutationThresh
            GuidUtils.writeStableFloat writer this.insertionThresh
            GuidUtils.writeStableFloat writer this.deletionThresh

module IndelRates =

    /// Mutates an array based on a single uniform indelRate. 
    /// Returns a new array with the same length as the input array.
    let mutate<'a> 
        (rates: indelRates) 
        (inserter: unit -> 'a) 
        (mutator: 'a -> 'a) 
        (floatPicker: unit -> float) 
        (arrayToMutate: 'a[]) : 'a[] = 
        
        let mutable deletionCount = 0
        let mutable insertionCount = 0
    
        let results = 
            seq {
                for i in 0 .. arrayToMutate.Length - 1 do
                    // Apply the uniform rate to every element
                    match rates.PickMode floatPicker with
                    | indelMode.Mutation -> 
                        yield mutator arrayToMutate.[i]
                    | indelMode.Insertion -> 
                        insertionCount <- insertionCount + 1
                        yield inserter ()
                        yield arrayToMutate.[i]
                    | indelMode.Deletion -> 
                        deletionCount <- deletionCount + 1
                    | indelMode.NoAction -> 
                        yield arrayToMutate.[i]
            } |> Seq.toArray
    
        let p = deletionCount - insertionCount
        if p > 0 then
            // Append p insertions to match input length
            Array.append results (Array.init p (fun _ -> inserter ()))
        elif p < 0 then
            // Trim excess elements to match input length
            Array.take arrayToMutate.Length results
        else
            results