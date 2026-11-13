namespace Sortopia.Models.Sorting.Simple

open System
open Sortopia.Permutations

module private RelativeWeights =
    let normalize (weights: (string * float) list) =
        for name, value in weights do
            if not (Double.IsFinite value) || value < 0.0 then
                invalidArg name "Weights must be finite and nonnegative."
        let values = weights |> List.map snd
        let maximum = List.max values
        if maximum = 0.0 then invalidArg "weights" "Set at least one positive weight."
        let sum = List.sum values
        if Double.IsFinite sum then fun value -> value / sum
        else
            let scaledSum = values |> List.sumBy (fun value -> value / maximum)
            fun value -> (value / maximum) / scaledSum

/// Named OPS mode weights converted to one probability distribution.
module OpsGenerationProbabilities =
    let fromWeights ortho para selfReflection : opsGenRates =
        let probability = RelativeWeights.normalize [ "Ortho", ortho; "Para", para; "SelfReflection", selfReflection ]
        opsGenRates.create(probability ortho, probability para, probability selfReflection)

/// Named six-channel seed mode weights converted to one probability distribution.
module Seed6GenerationProbabilities =
    let fromWeights ortho1 ortho2 para1 para2 para3 para4 selfReflection : seed6GenRates =
        let probability = RelativeWeights.normalize [
            "Ortho1", ortho1; "Ortho2", ortho2; "Para1", para1; "Para2", para2
            "Para3", para3; "Para4", para4; "SelfReflection", selfReflection ]
        seed6GenRates.create(probability ortho1, probability ortho2, probability para1, probability para2,
                            probability para3, probability para4, probability selfReflection)
