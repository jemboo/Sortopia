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

    let mutateTwoOrbitPairTypes
            (floatPicker:unit -> float)
            (opsTransitionRates:opsTransitionRates)
            (twoOrbitTypes:twoOrbitPairType seq)
             : twoOrbitPairType seq =

         twoOrbitTypes |> Seq.map( fun topt ->
                match topt with
                | twoOrbitPairType.Ortho -> 
                    match (opsTransitionRates.PickMode floatPicker twoOrbitType.Ortho) with
                    | opsActionMode.Para -> twoOrbitPairType.Para
                    | opsActionMode.SelfRefl -> twoOrbitPairType.SelfRefl
                    | _ -> twoOrbitPairType.Ortho
 
                | twoOrbitPairType.Para -> 
                    match (opsTransitionRates.PickMode floatPicker twoOrbitType.Para) with
                    | opsActionMode.Ortho -> twoOrbitPairType.Ortho
                    | opsActionMode.SelfRefl -> twoOrbitPairType.SelfRefl
                    | _ -> twoOrbitPairType.Para

                | twoOrbitPairType.SelfRefl -> 
                    match (opsTransitionRates.PickMode floatPicker twoOrbitType.SelfRefl) with
                    | opsActionMode.Para -> twoOrbitPairType.Para
                    | opsActionMode.Ortho -> twoOrbitPairType.Ortho
                    | _ -> twoOrbitPairType.SelfRefl
                )


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
        twoOrbitUfStep.create 
                (mutateTwoOrbitPairTypes floatPicker opsTransitionRates tOufs.TwoOrbitPairTypes
                    |> Seq.take (tOufs.Order / 2)
                    |> Seq.toArray)
                tOufs.Order
