namespace Sortopia.Permutations

open Sortopia.Core

module TwoOrbitTypeOps =

    ///makeTwoOrbitTypeGen
    let makeRandomTwoOrbitPairTypes 
            (floatPicker:unit -> float)
            (opsGenRates:opsGenRates) 
            : twoOrbitPairType seq =
        seq {
            while true do
                floatPicker |> opsGenRates.PickMode |> OpsGenMode.toTwoOrbitPairType
        }

    let private mutateTwoOrbitPairType
            (floatPicker:unit -> float)
            (opsTransitionRates:opsTransitionRates)
            (topt:twoOrbitPairType) =
        match topt with
        | twoOrbitPairType.Ortho ->
            match opsTransitionRates.PickMode floatPicker twoOrbitType.Ortho with
            | opsActionMode.Para -> twoOrbitPairType.Para
            | opsActionMode.SelfRefl -> twoOrbitPairType.SelfRefl
            | _ -> twoOrbitPairType.Ortho
        | twoOrbitPairType.Para ->
            match opsTransitionRates.PickMode floatPicker twoOrbitType.Para with
            | opsActionMode.Ortho -> twoOrbitPairType.Ortho
            | opsActionMode.SelfRefl -> twoOrbitPairType.SelfRefl
            | _ -> twoOrbitPairType.Para
        | twoOrbitPairType.SelfRefl ->
            match opsTransitionRates.PickMode floatPicker twoOrbitType.SelfRefl with
            | opsActionMode.Para -> twoOrbitPairType.Para
            | opsActionMode.Ortho -> twoOrbitPairType.Ortho
            | _ -> twoOrbitPairType.SelfRefl

    let mutateTwoOrbitPairTypes floatPicker opsTransitionRates twoOrbitTypes =
        twoOrbitTypes |> Seq.map (mutateTwoOrbitPairType floatPicker opsTransitionRates)


    // makeTwoOrbitUnfolderStep
    let makeRandomTwoOrbitUnfolderStep
                (floatPicker:unit -> float)
                (order:int) 
                (opsGenRates:opsGenRates) 
                : twoOrbitUfStep =
        twoOrbitUfStep.create 
                (makeRandomTwoOrbitPairTypes floatPicker opsGenRates 
                    |> Seq.take (order / 2)
                    |> Seq.toArray)
                order


    // mutateTwoOrbitUnfolderStep
    let mutateTwoOrbitUnfolderStep
                (floatPicker:unit -> float)
                (opsTransitionRates:opsTransitionRates) 
                (tOufs:twoOrbitUfStep) =
        tOufs.MapTypes(mutateTwoOrbitPairType floatPicker opsTransitionRates)
