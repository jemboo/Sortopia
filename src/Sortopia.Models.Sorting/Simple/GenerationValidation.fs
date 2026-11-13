namespace Sortopia.Models.Sorting.Simple

open System
open FSharp.UMX
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Models.Sorting

module internal GenerationValidation =
    let width modelType channelCount =
        if not (SimpleSorterModelType.supportsChannelCount channelCount modelType) then
            invalidArg "channelCount" $"{SimpleSorterModelType.toString modelType} requires {SimpleSorterModelType.channelCountRequirement modelType}."

    let dimensions modelType (channelCount: int<channelCount>) (stages: int<stageLength>) =
        width modelType (int channelCount)
        if int stages < 1 then invalidArg "stageLength" "Suffix stage length must be at least 1."
        if int64 channelCount * int64 stages > int64 Int32.MaxValue then
            invalidArg "stageLength" "Channel count and stage length exceed the supported suffix-size range."

    let unfoldingWidths modelType seedWidth channelCount =
        width modelType channelCount
        [ let mutable size = seedWidth
          while size < channelCount do
              yield size
              size <- size * 2 ]

    let private probabilities name values =
        if values |> List.exists (fun value -> not (Double.IsFinite value) || value < 0.0 || value > 1.0)
           || abs (List.sum values - 1.0) > 1e-10 then
            invalidArg name "Probabilities must be finite, between 0 and 1, and sum to 1."

    let ops name (rates: opsGenRates) = probabilities name [ rates.OrthoRate; rates.ParaRate; rates.SelfReflRate ]
    let seed6 (rates: seed6GenRates) =
        probabilities "seedProbabilities" [ rates.Ortho1Rate; rates.Ortho2Rate; rates.Para1Rate; rates.Para2Rate;
                                           rates.Para3Rate; rates.Para4Rate; rates.SelfReflRate ]
    let unfolding expected (rates: opsGenRates list) =
        if rates.Length <> expected then
            invalidArg "unfoldingProbabilities" $"Expected {expected} unfolding distributions, got {rates.Length}. These are unfolding levels within each stage."
        rates |> List.iteri (fun index rates -> ops $"unfoldingProbabilities[{index}]" rates)
        rates |> List.toArray |> opsGenRatesArray.create
