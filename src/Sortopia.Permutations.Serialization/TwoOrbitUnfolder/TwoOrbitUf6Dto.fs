namespace Sortopia.Permutations.Serialization.TwoOrbitUnfolder

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations

type twoOrbitUf6Dto =
    { seedType: twoOrbitTripleType
      twoOrbitUfSteps: twoOrbitUfStepDto array }
    
    static member Create (seedType: twoOrbitTripleType) 
                         (twoOrbitUnfolderSteps: twoOrbitUfStepDto array) : twoOrbitUf6Dto =
        if Array.isEmpty twoOrbitUnfolderSteps then
            failwith "TwoOrbitUnfolderSteps list cannot be empty"
        else
            if twoOrbitUnfolderSteps |> Array.exists (fun step -> step.order < 4 || step.order % 2 <> 0) then
                invalidArg (nameof twoOrbitUnfolderSteps) "All TwoOrbitUfStep orders must be at least 4 and even"
            else
                { seedType = seedType
                  twoOrbitUfSteps = twoOrbitUnfolderSteps }

module TwoOrbitUf6Dto =

    let fromDomain (tou: twoOrbitUf6) : twoOrbitUf6Dto =
        { seedType = tou.Seed6TwoOrbitType
          twoOrbitUfSteps = tou.TwoOrbitUnfolderSteps |> Array.map TwoOrbitUnfolderStepDto.fromDomain }

    let toDomain (dto: twoOrbitUf6Dto) : twoOrbitUf6 =
        let steps = 
            dto.twoOrbitUfSteps 
            |> Array.map TwoOrbitUnfolderStepDto.toDomain
        
        twoOrbitUf6.create dto.seedType steps

    let getOrder (twoOrbitUnfolder: twoOrbitUf6Dto) : int =
        6 * (MathUtils.integerPower 2 (Array.length twoOrbitUnfolder.twoOrbitUfSteps))