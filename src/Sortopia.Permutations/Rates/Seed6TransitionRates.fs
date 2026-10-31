namespace Sortopia.Permutations

open Sortopia.Core
open System

[<Struct; StructuralEquality; NoComparison>]
type seed6TransitionRates = 
    private 
        { 
            ortho1Rates: seed6ActionRates
            ortho2Rates: seed6ActionRates
            para1Rates: seed6ActionRates
            para2Rates: seed6ActionRates
            para3Rates: seed6ActionRates
            para4Rates: seed6ActionRates
            selfReflRates: seed6ActionRates
        }

    static member create (ortho1Rates: seed6ActionRates) (ortho2Rates: seed6ActionRates) (para1Rates: seed6ActionRates) 
                         (para2Rates: seed6ActionRates) (para3Rates: seed6ActionRates) (para4Rates: seed6ActionRates) 
                         (selfReflRates: seed6ActionRates) : seed6TransitionRates =
        { 
            ortho1Rates = ortho1Rates
            ortho2Rates = ortho2Rates
            para1Rates = para1Rates
            para2Rates = para2Rates
            para3Rates = para3Rates
            para4Rates = para4Rates
            selfReflRates = selfReflRates
        }

    static member createUniform (amt: float) : seed6TransitionRates =
        let rates = seed6ActionRates.createUniform amt
        seed6TransitionRates.create rates rates rates rates rates rates rates


    static member createBiased (seed6GenMode: seed6GenMode) (baseAmt:float) (biasAmt:float) : seed6TransitionRates =
        match seed6GenMode with
        | seed6GenMode.Ortho1 -> 
            let ortho1Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho1, baseAmt, biasAmt)
            let ortho2Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho1, baseAmt, biasAmt)
            let para1Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho1, baseAmt, biasAmt)
            let para2Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho1, baseAmt, biasAmt)
            let para3Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho1, baseAmt, biasAmt)
            let para4Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho1, baseAmt, biasAmt)
            let selfReflRates = seed6ActionRates.createBiased(seed6ActionMode.Ortho1, baseAmt, biasAmt)
            seed6TransitionRates.create ortho1Rates ortho2Rates para1Rates para2Rates para3Rates para4Rates selfReflRates
        | seed6GenMode.Ortho2 ->
            let ortho1Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho2, baseAmt, biasAmt)
            let ortho2Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho2, baseAmt, biasAmt)
            let para1Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho2, baseAmt, biasAmt)
            let para2Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho2, baseAmt, biasAmt)
            let para3Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho2, baseAmt, biasAmt)
            let para4Rates = seed6ActionRates.createBiased(seed6ActionMode.Ortho2, baseAmt, biasAmt)
            let selfReflRates = seed6ActionRates.createBiased(seed6ActionMode.Ortho2, baseAmt, biasAmt)
            seed6TransitionRates.create ortho1Rates ortho2Rates para1Rates para2Rates para3Rates para4Rates selfReflRates
        | seed6GenMode.Para1 -> 
            let ortho1Rates = seed6ActionRates.createBiased(seed6ActionMode.Para1, baseAmt, biasAmt)
            let ortho2Rates = seed6ActionRates.createBiased(seed6ActionMode.Para1, baseAmt, biasAmt)
            let para1Rates = seed6ActionRates.createBiased(seed6ActionMode.Para1, baseAmt + biasAmt, biasAmt)
            let para2Rates = seed6ActionRates.createBiased(seed6ActionMode.Para1, baseAmt, biasAmt)
            let para3Rates = seed6ActionRates.createBiased(seed6ActionMode.Para1, baseAmt, biasAmt)
            let para4Rates = seed6ActionRates.createBiased(seed6ActionMode.Para1, baseAmt, biasAmt)
            let selfReflRates = seed6ActionRates.createBiased(seed6ActionMode.Para1, baseAmt, biasAmt)
            seed6TransitionRates.create ortho1Rates ortho2Rates para1Rates para2Rates para3Rates para4Rates selfReflRates
        | seed6GenMode.Para2 -> 
            let ortho1Rates = seed6ActionRates.createBiased(seed6ActionMode.Para2, baseAmt, biasAmt)
            let ortho2Rates = seed6ActionRates.createBiased(seed6ActionMode.Para2, baseAmt, biasAmt)
            let para1Rates = seed6ActionRates.createBiased(seed6ActionMode.Para2, baseAmt, biasAmt)
            let para2Rates = seed6ActionRates.createBiased(seed6ActionMode.Para2, baseAmt + biasAmt, biasAmt)
            let para3Rates = seed6ActionRates.createBiased(seed6ActionMode.Para2, baseAmt, biasAmt)
            let para4Rates = seed6ActionRates.createBiased(seed6ActionMode.Para2, baseAmt, biasAmt)
            let selfReflRates = seed6ActionRates.createBiased(seed6ActionMode.Para2, baseAmt, biasAmt)
            seed6TransitionRates.create ortho1Rates ortho2Rates para1Rates para2Rates para3Rates para4Rates selfReflRates
        | seed6GenMode.Para3 -> 
            let ortho1Rates = seed6ActionRates.createBiased(seed6ActionMode.Para3, baseAmt, biasAmt)
            let ortho2Rates = seed6ActionRates.createBiased(seed6ActionMode.Para3, baseAmt, biasAmt)
            let para1Rates = seed6ActionRates.createBiased(seed6ActionMode.Para3, baseAmt, biasAmt)
            let para2Rates = seed6ActionRates.createBiased(seed6ActionMode.Para3, baseAmt, biasAmt)
            let para3Rates = seed6ActionRates.createBiased(seed6ActionMode.Para3, baseAmt + biasAmt, biasAmt)
            let para4Rates = seed6ActionRates.createBiased(seed6ActionMode.Para3, baseAmt, biasAmt)
            let selfReflRates = seed6ActionRates.createBiased(seed6ActionMode.Para3, baseAmt, biasAmt)
            seed6TransitionRates.create ortho1Rates ortho2Rates para1Rates para2Rates para3Rates para4Rates selfReflRates
        | seed6GenMode.Para4 -> 
            let ortho1Rates = seed6ActionRates.createBiased(seed6ActionMode.Para4, baseAmt, biasAmt)
            let ortho2Rates = seed6ActionRates.createBiased(seed6ActionMode.Para4, baseAmt, biasAmt)
            let para1Rates = seed6ActionRates.createBiased(seed6ActionMode.Para4, baseAmt, biasAmt)
            let para2Rates = seed6ActionRates.createBiased(seed6ActionMode.Para4, baseAmt, biasAmt)
            let para3Rates = seed6ActionRates.createBiased(seed6ActionMode.Para4, baseAmt, biasAmt)
            let para4Rates = seed6ActionRates.createBiased(seed6ActionMode.Para4, baseAmt + biasAmt, biasAmt)
            let selfReflRates = seed6ActionRates.createBiased(seed6ActionMode.Para4, baseAmt, biasAmt)
            seed6TransitionRates.create ortho1Rates ortho2Rates para1Rates para2Rates para3Rates para4Rates selfReflRates
        | seed6GenMode.SelfRefl -> 
            let ortho1Rates = seed6ActionRates.createBiased(seed6ActionMode.SelfRefl, baseAmt, biasAmt)
            let ortho2Rates = seed6ActionRates.createBiased(seed6ActionMode.SelfRefl, baseAmt, biasAmt)
            let para1Rates = seed6ActionRates.createBiased(seed6ActionMode.SelfRefl, baseAmt, biasAmt)
            let para2Rates = seed6ActionRates.createBiased(seed6ActionMode.SelfRefl, baseAmt, biasAmt)
            let para3Rates = seed6ActionRates.createBiased(seed6ActionMode.SelfRefl, baseAmt, biasAmt)
            let para4Rates = seed6ActionRates.createBiased(seed6ActionMode.SelfRefl, baseAmt, biasAmt)
            let selfReflRates = seed6ActionRates.createBiased(seed6ActionMode.SelfRefl, baseAmt + biasAmt, biasAmt)
            seed6TransitionRates.create ortho1Rates ortho2Rates para1Rates para2Rates para3Rates para4Rates selfReflRates


    member this.PickMode (floatPicker: unit -> float) (orbitType: twoOrbitTripleType) : seed6ActionMode =
        match orbitType with
        | twoOrbitTripleType.Ortho1 -> this.ortho1Rates.PickMode floatPicker
        | twoOrbitTripleType.Ortho2 -> this.ortho2Rates.PickMode floatPicker
        | twoOrbitTripleType.Para1 -> this.para1Rates.PickMode floatPicker
        | twoOrbitTripleType.Para2 -> this.para2Rates.PickMode floatPicker
        | twoOrbitTripleType.Para3 -> this.para3Rates.PickMode floatPicker
        | twoOrbitTripleType.Para4 -> this.para4Rates.PickMode floatPicker
        | twoOrbitTripleType.SelfRefl -> this.selfReflRates.PickMode floatPicker


    member this.TransitionMode (floatPicker: unit -> float) (seed6GenMode: seed6GenMode) : seed6GenMode =
        match seed6GenMode with
        | seed6GenMode.Ortho1 -> this.ortho1Rates.PickModeWithDefault floatPicker seed6GenMode
        | seed6GenMode.Ortho2 -> this.ortho2Rates.PickModeWithDefault floatPicker seed6GenMode
        | seed6GenMode.Para1 -> this.para1Rates.PickModeWithDefault floatPicker seed6GenMode
        | seed6GenMode.Para2 -> this.para2Rates.PickModeWithDefault floatPicker seed6GenMode
        | seed6GenMode.Para3 -> this.para3Rates.PickModeWithDefault floatPicker seed6GenMode
        | seed6GenMode.Para4 -> this.para4Rates.PickModeWithDefault floatPicker seed6GenMode
        | seed6GenMode.SelfRefl -> this.selfReflRates.PickModeWithDefault floatPicker seed6GenMode


    member this.toString() =
        sprintf "Seed6TransitionRates(Ortho1: %s, Ortho2: %s, Para1: %s, Para2: %s, Para3: %s, Para4: %s, SelfRefl: %s)"
                (this.ortho1Rates.toString())
                (this.ortho2Rates.toString())
                (this.para1Rates.toString())
                (this.para2Rates.toString())
                (this.para3Rates.toString())
                (this.para4Rates.toString())
                (this.selfReflRates.toString())


    member this.Ortho1Rates with get() = this.ortho1Rates
    member this.Ortho2Rates with get() = this.ortho2Rates
    member this.Para1Rates with get() = this.para1Rates
    member this.Para2Rates with get() = this.para2Rates
    member this.Para3Rates with get() = this.para3Rates
    member this.Para4Rates with get() = this.para4Rates
    member this.SelfReflRates with get() = this.selfReflRates

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("seed6TransitionRates")
            GuidUtils.writeStableValue writer this.ortho1Rates
            GuidUtils.writeStableValue writer this.ortho2Rates
            GuidUtils.writeStableValue writer this.para1Rates
            GuidUtils.writeStableValue writer this.para2Rates
            GuidUtils.writeStableValue writer this.para3Rates
            GuidUtils.writeStableValue writer this.para4Rates
            GuidUtils.writeStableValue writer this.selfReflRates
