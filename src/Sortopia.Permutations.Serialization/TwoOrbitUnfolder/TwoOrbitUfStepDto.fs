namespace Sortopia.Permutations.Serialization.TwoOrbitUnfolder

open Sortopia.Core
open Sortopia.Permutations

[<Struct>]
type twoOrbitUfStepDto =
    { twoOrbitPairTypes: twoOrbitPairType array
      order: int }
    
    static member Create (twoOrbitPairTypes: twoOrbitPairType array) 
                         (order: int) : twoOrbitUfStepDto =
        if order < 4 then
            failwith $"Order must be at least 4, got {order}"
        else if order % 2 <> 0 then
            failwith $"Order must be even, got {order}"
        else if Array.length twoOrbitPairTypes <> order / 2 then
            failwith $"TwoOrbitTypes length ({Array.length twoOrbitPairTypes}) must be order / 2 ({order / 2})"
        else
            { twoOrbitPairTypes = twoOrbitPairTypes; order = order }

module TwoOrbitUnfolderStepDto =

    let fromDomain (step: twoOrbitUfStep) : twoOrbitUfStepDto =
        { twoOrbitPairTypes = step.TwoOrbitPairTypes
          order = step.Order }

    let toDomain (dto: twoOrbitUfStepDto) : twoOrbitUfStep =
        twoOrbitUfStep.create dto.twoOrbitPairTypes dto.order