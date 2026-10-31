namespace Sortopia.Permutations

open Sortopia.Core

module RandomUnfolderOps4 = 

    // makeTwoOrbitUnfolder4
    let makeRandomTwoOrbitUf4 
            (floatPicker:unit -> float) 
            (uf4GenRates:uf4GenRates) : twoOrbitUf4 =

        let seedTypeUf4 = floatPicker |> uf4GenRates.seedOpsGenRates.PickMode |> OpsGenMode.toTwoOrbitPairType
        let twoOrbitUnfolderSteps = 
            uf4GenRates.opsGenRatesArray.RatesArray
                |> Array.mapi(fun dex rates ->
                    TwoOrbitTypeOps.makeRandomTwoOrbitUnfolderStep floatPicker (4 * (MathUtils.integerPower 2 dex)) rates)
        twoOrbitUf4.create seedTypeUf4 twoOrbitUnfolderSteps



    // mutateTwoOrbitUnfolder4
    let mutateTwoOrbitUf4
                (floatPicker:unit -> float)
                (uf4MutationRates:uf4MutationRates) 
                (tOuf4:twoOrbitUf4) : twoOrbitUf4 =

        let seedMutated = 
                uf4MutationRates.seedOpsTransitionRates.TransitionMode 
                        floatPicker 
                        (tOuf4.SeedTwoOrbitPairType |> OpsGenMode.fromTwoOrbitType )
                        |> OpsGenMode.toTwoOrbitPairType

        let stepsMutated = 
                uf4MutationRates.twoOrbitPairOpsTransitionRates.RatesArray
                |> Array.mapi(fun dex rates -> 
                     TwoOrbitTypeOps.mutateTwoOrbitUnfolderStep 
                                floatPicker 
                                rates 
                                tOuf4.TwoOrbitUnfolderSteps[dex])

        twoOrbitUf4.create seedMutated stepsMutated




module RandomUnfolderOps6 =
 
    // makeTwoOrbitUnfolder6
    let makeRandomTwoOrbitUf6 (floatPicker:unit -> float) (uf6GenRates:uf6GenRates) 
                : twoOrbitUf6 =

        let seed6TwoOrbitType = 
                uf6GenRates.seedGenRatesUf6.PickMode floatPicker |> Seed6GenMode.toSeed6TwoOrbitType

        let twoOrbitUfSteps = 
                    uf6GenRates.opsGenRatesArray.RatesArray
                                |> Array.mapi(
            fun dex rates -> TwoOrbitTypeOps.makeRandomTwoOrbitUnfolderStep floatPicker (6 * (MathUtils.integerPower 2 dex)) rates)

        twoOrbitUf6.create seed6TwoOrbitType twoOrbitUfSteps


    // mutateTwoOrbitUnfolder6
    let mutateTwoOrbitUf6
                (floatPicker:unit -> float)
                (uf6MutationRates:uf6MutationRates) 
                (tOuf4:twoOrbitUf6) : twoOrbitUf6 =

        let seed6TwoOrbitTypeMutated = 
                uf6MutationRates.seed6TransitionRates.TransitionMode
                        floatPicker
                        (tOuf4.Seed6TwoOrbitType |> Seed6GenMode.fromSeed6TwoOrbitType)
                        |> Seed6GenMode.toSeed6TwoOrbitType

        let twoOrbitUnfolderSteps = 
                    uf6MutationRates.opsTransitionRates.RatesArray
                    |> Array.mapi(
                            fun dex rates -> 
                                TwoOrbitTypeOps.mutateTwoOrbitUnfolderStep 
                                        floatPicker 
                                        rates 
                                        tOuf4.TwoOrbitUnfolderSteps[dex])

        twoOrbitUf6.create seed6TwoOrbitTypeMutated twoOrbitUnfolderSteps

