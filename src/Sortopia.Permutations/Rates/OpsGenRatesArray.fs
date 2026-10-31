namespace Sortopia.Permutations

open Sortopia.Core

open System

[<Struct; StructuralEquality; NoComparison>]
type opsGenRatesArray =
    private { opsGenRates: opsGenRates array }

    static member create (rates: opsGenRates array) : opsGenRatesArray =
        let rates = Array.copy rates
        if Array.isEmpty rates then failwith "opsGenRatesArray cannot be empty"
        
        { opsGenRates = rates }

    member this.Length = this.opsGenRates.Length
    member this.Item(index: int) = this.opsGenRates.[index]
    member this.RatesArray = Array.copy this.opsGenRates
    
    member this.toString() =
        String.Join(", ", Array.map (fun r -> r.ToString()) this.opsGenRates)

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("opsGenRatesArray")
            writer.Write(this.opsGenRates.Length)
            for rate in this.opsGenRates do
                GuidUtils.writeStableValue writer rate

module OpsGenRatesArray =

    let createUniform (length: int) : opsGenRatesArray =
        if length <= 0 then failwith "Length must be positive"
        let rates = Array.init length (fun _ -> opsGenRates.createUniform())
        opsGenRatesArray.create rates


    /// Mutates an array based on the provided rates. Returns a new array.
    /// No length adjustments are needed since OpsGenMode does not include insertion or deletion.
    let mutate<'a> 
        (opsGenRatesArray: opsGenRatesArray) 
        (orthoMutator: 'a -> 'a) 
        (paraMutator: 'a -> 'a) 
        (selfSymMutator: 'a -> 'a) 
        (floatPicker: unit -> float) 
        (arrayToMutate: 'a[]) : 'a[] = 
        if opsGenRatesArray.Length <> arrayToMutate.Length then
            failwith "Array length does not match rates length"
    
        Array.init arrayToMutate.Length (fun i ->
            let rate = opsGenRatesArray.Item(i)
            match rate.PickMode floatPicker with
            | opsGenMode.Ortho -> orthoMutator arrayToMutate.[i]
            | opsGenMode.Para -> paraMutator arrayToMutate.[i]
            | opsGenMode.SelfRefl -> selfSymMutator arrayToMutate.[i])
