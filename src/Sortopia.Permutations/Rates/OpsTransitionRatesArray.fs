namespace Sortopia.Permutations

open Sortopia.Core

open System

[<Struct; StructuralEquality; NoComparison>]
type opsTransitionRatesArray =
    private { opsTransitionRates: opsTransitionRates array }

    static member create (rates: opsTransitionRates array) : opsTransitionRatesArray =
        let rates = Array.copy rates
        if Array.isEmpty rates then failwith "opsTransitionRatesArray cannot be empty"
        
        { opsTransitionRates = rates }

    member this.Length = this.opsTransitionRates.Length
    member this.Item(index: int) = this.opsTransitionRates.[index]
    member this.RatesArray = Array.copy this.opsTransitionRates

    member this.toString() =
        String.Join(", ", Array.map (fun (r: opsTransitionRates) -> r.toString()) this.opsTransitionRates)

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("opsTransitionRatesArray")
            writer.Write(this.opsTransitionRates.Length)
            for rate in this.opsTransitionRates do
                GuidUtils.writeStableValue writer rate

module OpsTransitionRatesArray =

    let mutate<'a> 
        (opsTransitionRatesArray: opsTransitionRatesArray) 
        (orthoMutator: 'a -> 'a) 
        (paraMutator: 'a -> 'a) 
        (selfSymMutator: 'a -> 'a) 
        (floatPicker: unit -> float) 
        (twoOrbitType: twoOrbitType) 
        (arrayToMutate: 'a[]) : 'a[] = 
        if opsTransitionRatesArray.Length <> arrayToMutate.Length then
            failwith "Array length does not match rates length"
    
        Array.init arrayToMutate.Length (fun i ->
            let rate = opsTransitionRatesArray.Item(i)
            match rate.PickMode floatPicker twoOrbitType with
            | opsActionMode.Ortho -> orthoMutator arrayToMutate.[i]
            | opsActionMode.Para -> paraMutator arrayToMutate.[i]
            | opsActionMode.SelfRefl -> selfSymMutator arrayToMutate.[i]
            | opsActionMode.NoAction -> arrayToMutate.[i])
