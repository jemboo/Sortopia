namespace Sortopia.Permutations

open Sortopia.Core
open System

type seed6ActionMode =
    | Ortho1
    | Ortho2
    | Para1
    | Para2
    | Para3
    | Para4
    | SelfRefl
    | NoAction


[<Struct; StructuralEquality; NoComparison>]
type seed6ActionRates = 
    private 
        { 
            ortho1Thresh: float
            ortho2Thresh: float
            para1Thresh: float
            para2Thresh: float
            para3Thresh: float
            para4Thresh: float
            selfReflThresh: float
        }

    static member create (ortho1Rate: float) (ortho2Rate: float) (para1Rate: float) 
                         (para2Rate: float) (para3Rate: float) (para4Rate: float) (selfReflRate: float) : seed6ActionRates =
        let noAction = 1.0 - ortho1Rate - ortho2Rate - para1Rate - para2Rate - para3Rate - para4Rate - selfReflRate
        let epsilon = 1e-10
        if ortho1Rate < 0.0 || ortho1Rate > 1.0 then failwith "ortho1Rate must be between 0 and 1"
        else if ortho2Rate < 0.0 || ortho2Rate > 1.0 then failwith "ortho2Rate must be between 0 and 1"
        else if para1Rate < 0.0 || para1Rate > 1.0 then failwith "para1Rate must be between 0 and 1"
        else if para2Rate < 0.0 || para2Rate > 1.0 then failwith "para2Rate must be between 0 and 1"
        else if para3Rate < 0.0 || para3Rate > 1.0 then failwith "para3Rate must be between 0 and 1"
        else if para4Rate < 0.0 || para4Rate > 1.0 then failwith "para4Rate must be between 0 and 1"
        else if selfReflRate < 0.0 || selfReflRate > 1.0 then failwith "selfReflRate must be between 0 and 1"
        else if noAction < -epsilon then failwith "Sum of Seed6ActionRates rates must not exceed 1.0"
        {
            ortho1Thresh = ortho1Rate
            ortho2Thresh = ortho1Rate + ortho2Rate
            para1Thresh = ortho1Rate + ortho2Rate + para1Rate
            para2Thresh = ortho1Rate + ortho2Rate + para1Rate + para2Rate
            para3Thresh = ortho1Rate + ortho2Rate + para1Rate + para2Rate + para3Rate
            para4Thresh = ortho1Rate + ortho2Rate + para1Rate + para2Rate + para3Rate + para4Rate
            selfReflThresh = ortho1Rate + ortho2Rate + para1Rate + para2Rate + para3Rate + para4Rate + selfReflRate
        }

    static member createUniform (amt: float) : seed6ActionRates =
        let rate = amt / 7.0
        seed6ActionRates.create rate rate rate rate rate rate rate

    static member createBiased (mode: seed6ActionMode, baseAmt: float, biasAmt: float) : seed6ActionRates =
        if baseAmt < 0.0 || baseAmt > 1.0 then failwith "baseAmt must be between 0 and 1"
        if biasAmt < 0.0 || biasAmt > 1.0 then failwith "biasAmt must be between 0 and 1"
        let adjustedBase = baseAmt - (biasAmt / 6.0)
        if adjustedBase < 0.0 then failwith "Adjusted base rate must not be negative"
        let biasedRate = baseAmt + biasAmt
        match mode with
        | seed6ActionMode.Ortho1 -> seed6ActionRates.create biasedRate adjustedBase adjustedBase adjustedBase adjustedBase adjustedBase adjustedBase
        | seed6ActionMode.Ortho2 -> seed6ActionRates.create adjustedBase biasedRate adjustedBase adjustedBase adjustedBase adjustedBase adjustedBase
        | seed6ActionMode.Para1 -> seed6ActionRates.create adjustedBase adjustedBase biasedRate adjustedBase adjustedBase adjustedBase adjustedBase
        | seed6ActionMode.Para2 -> seed6ActionRates.create adjustedBase adjustedBase adjustedBase biasedRate adjustedBase adjustedBase adjustedBase
        | seed6ActionMode.Para3 -> seed6ActionRates.create adjustedBase adjustedBase adjustedBase adjustedBase biasedRate adjustedBase adjustedBase
        | seed6ActionMode.Para4 -> seed6ActionRates.create adjustedBase adjustedBase adjustedBase adjustedBase adjustedBase biasedRate adjustedBase
        | seed6ActionMode.SelfRefl -> seed6ActionRates.create adjustedBase adjustedBase adjustedBase adjustedBase adjustedBase adjustedBase biasedRate
        | seed6ActionMode.NoAction -> failwith "NoAction mode is not valid for Seed6ActionRates"



    member this.Ortho1Rate with get() = this.ortho1Thresh
    member this.Ortho2Rate with get() = this.ortho2Thresh - this.ortho1Thresh
    member this.Para1Rate with get() = this.para1Thresh - this.ortho2Thresh
    member this.Para2Rate with get() = this.para2Thresh - this.para1Thresh
    member this.Para3Rate with get() = this.para3Thresh - this.para2Thresh
    member this.Para4Rate with get() = this.para4Thresh - this.para3Thresh
    member this.SelfReflRate with get() = this.selfReflThresh - this.para4Thresh
    member this.NoActionRate with get() = 1.0 - this.selfReflThresh

    member this.toString() =
        sprintf "Seed6ActionRates(Ortho1: %.2f, Ortho2: %.2f, Para1: %.2f, Para2: %.2f, Para3: %.2f, Para4: %.2f, SelfRefl: %.2f, NoAction: %.2f)" 
                this.Ortho1Rate this.Ortho2Rate this.Para1Rate this.Para2Rate this.Para3Rate this.Para4Rate this.SelfReflRate this.NoActionRate

    /// Assumes that floatPicker returns a float in the range [0.0, 1.0).
    member this.PickMode (floatPicker: unit -> float) : seed6ActionMode =
        let r = floatPicker()
        if r < this.ortho1Thresh then
            seed6ActionMode.Ortho1
        elif r < this.ortho2Thresh then
            seed6ActionMode.Ortho2
        elif r < this.para1Thresh then
            seed6ActionMode.Para1
        elif r < this.para2Thresh then
            seed6ActionMode.Para2
        elif r < this.para3Thresh then
            seed6ActionMode.Para3
        elif r < this.para4Thresh then
            seed6ActionMode.Para4
        elif r < this.selfReflThresh then
            seed6ActionMode.SelfRefl
        else
            seed6ActionMode.NoAction


    member this.PickModeWithDefault  (floatPicker: unit -> float)  (defaultSeed6GenMode:seed6GenMode): seed6GenMode =
        let r = floatPicker()
        if r < this.ortho1Thresh then
            seed6GenMode.Ortho1
        elif r < this.ortho2Thresh then
            seed6GenMode.Ortho2
        elif r < this.para1Thresh then
            seed6GenMode.Para1
        elif r < this.para2Thresh then
            seed6GenMode.Para2
        elif r < this.para3Thresh then
            seed6GenMode.Para3
        elif r < this.para4Thresh then
            seed6GenMode.Para4
        elif r < this.selfReflThresh then
            seed6GenMode.SelfRefl
        else
            defaultSeed6GenMode

    interface IStableSerializable with
        member this.WriteStableBytes writer =
            writer.Write("seed6ActionRates")
            GuidUtils.writeStableFloat writer this.ortho1Thresh
            GuidUtils.writeStableFloat writer this.ortho2Thresh
            GuidUtils.writeStableFloat writer this.para1Thresh
            GuidUtils.writeStableFloat writer this.para2Thresh
            GuidUtils.writeStableFloat writer this.para3Thresh
            GuidUtils.writeStableFloat writer this.para4Thresh
            GuidUtils.writeStableFloat writer this.selfReflThresh
