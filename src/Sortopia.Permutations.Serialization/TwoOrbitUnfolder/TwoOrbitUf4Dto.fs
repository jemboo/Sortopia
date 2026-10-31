namespace Sortopia.Permutations.Serialization.TwoOrbitUnfolder

open System
open Sortopia.Core
open Sortopia.Permutations

[<Struct>]
type twoOrbitUf4Dto =
    { seedType: twoOrbitPairType
      twoOrbitUfStepDtos: twoOrbitUfStepDto array }
    
    static member Create 
                    (seedType: twoOrbitPairType) 
                    (twoOrbitUnfolderSteps: twoOrbitUfStepDto array) : twoOrbitUf4Dto =
        if Array.isEmpty twoOrbitUnfolderSteps then
            failwith "TwoOrbitUnfolderSteps array cannot be empty"
        else
            { seedType = seedType
              twoOrbitUfStepDtos = twoOrbitUnfolderSteps }

module TwoOrbitUf4Dto =

    let fromDomain (tou: twoOrbitUf4) : twoOrbitUf4Dto =
        { seedType = tou.SeedTwoOrbitPairType
          twoOrbitUfStepDtos = tou.TwoOrbitUnfolderSteps |> Array.map TwoOrbitUnfolderStepDto.fromDomain }

    let toDomain (dto: twoOrbitUf4Dto) : twoOrbitUf4 =
        let steps = 
            dto.twoOrbitUfStepDtos 
            |> Array.map TwoOrbitUnfolderStepDto.toDomain
        
        twoOrbitUf4.create dto.seedType steps

    let getOrder (twoOrbitUnfolder: twoOrbitUf4Dto) : int =
        4 * (MathUtils.integerPower 2 (Array.length twoOrbitUnfolder.twoOrbitUfStepDtos))