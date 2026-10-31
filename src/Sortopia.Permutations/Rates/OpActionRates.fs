namespace Sortopia.Permutations

open Sortopia.Core
open System

type opActionMode =
    | NoAction
    | Ortho
    | Para


[<Struct; StructuralEquality; NoComparison>]
type opActionRates = 
    private 
        { orthoThresh: float; paraThresh: float; }

    static member create (orthoRate: float, paraRate: float) : opActionRates =
        let noAction = 1.0 - orthoRate - paraRate
        let epsilon = 1e-10
        if orthoRate < 0.0 || orthoRate > 1.0 then failwith "orthoRate must be between 0 and 1"
        else if paraRate < 0.0 || paraRate > 1.0 then failwith "paraRate must be between 0 and 1"
        else if noAction < -epsilon then failwith "Sum of OpActionRates rates must not exceed 1.0"
        {
            orthoThresh = orthoRate
            paraThresh = orthoRate + paraRate
        }

    static member createUniform (amt:float) : opActionRates =
        opActionRates.create(amt, amt)

    static member createMod (modificationRate: float, orthoRate: float, paraRate: float) : opActionRates = 
        let adj = modificationRate / (orthoRate + paraRate)
        opActionRates.create (adj*orthoRate, adj*paraRate)

    static member createBiased(opActionMode: opActionMode, baseAmt:float, biasAmt: float) : opActionRates =
        match opActionMode with
        | opActionMode.Ortho -> opActionRates.create(baseAmt + biasAmt, baseAmt - biasAmt)
        | opActionMode.Para -> opActionRates.create(baseAmt - biasAmt, baseAmt + biasAmt)
        | opActionMode.NoAction -> failwith "NoAction mode is not valid for OpActionRates"


    member this.OrthoRate with get() = this.orthoThresh
    member this.ParaRate with get() = this.paraThresh - this.orthoThresh
    member this.NoActionRate with get() = 1.0 - this.paraThresh
    member this.toString() =
        sprintf "OpActionRates(Ortho: %.2f, Para: %.2f)" 
                this.OrthoRate this.ParaRate    

    /// Assumes that floatPicker returns a float in the range [0.0, 1.0).
    member this.PickMode (floatPicker: unit -> float) : opActionMode =
        let r = floatPicker()
        if r < this.orthoThresh then
            opActionMode.Ortho
        elif r < this.paraThresh then
            opActionMode.Para
        else
        opActionMode.NoAction

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("opActionRates")
            GuidUtils.writeStableFloat writer this.orthoThresh
            GuidUtils.writeStableFloat writer this.paraThresh

module OpActionRates =

    /// Mutates an array of PermSi using a single uniform opActionRates.
    let mutateV1 
        (rates: opActionRates) 
        (orthoMutator: permSi -> permSi) 
        (paraMutator: permSi -> permSi) 
        (floatPicker: unit -> float) 
        (arrayToMutate: permSi[]) : permSi[] = 
        
        arrayToMutate |> Array.map (fun psi ->
            match rates.PickMode floatPicker with
            | opActionMode.Ortho    -> orthoMutator psi
            | opActionMode.Para     -> paraMutator psi
            | opActionMode.NoAction -> psi
        )


    let mutateV2
        (rates: opActionRates) 
        (orthoMutator: permSi -> permSi)
        (paraMutator: permSi -> permSi)
        (floatPicker: unit -> float)
        (permEval: permSi -> int)
        (arrayToMutate: permSi[]) : permSi[] = 
    
        arrayToMutate |> Array.map (fun psi ->
            match rates.PickMode floatPicker with
            | opActionMode.NoAction -> 
                psi
            | mode ->
                let mutated = 
                    match mode with
                    | opActionMode.Ortho -> orthoMutator psi
                    | opActionMode.Para  -> paraMutator psi
                    | opActionMode.NoAction -> psi // Unreachable, but completes exhaustiveness

                let oVal = permEval psi
                let mVal = permEval mutated

                if oVal > (mVal + 1) then psi else mutated
        )