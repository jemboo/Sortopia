namespace Sortopia.Permutations

open Sortopia.Core


module TwoOrbitPairOps =
    /// Gets the TwoOrbitType of the TwoOrbitPair
    /// returns the TwoOrbitType of the first TwoOrbit if the second is None,
    /// or checks the TwoOrbitType of both if the second is present.
    /// returns None if the TwoOrbits are not of the same type.
    /// <param name="twoOrbitPair">The TwoOrbitPair to check.</param>
    let getTwoOrbitPairTypeOption (twoOrbitPair: twoOrbitPair) : twoOrbitPairType option =

        match twoOrbitPair.Second with
        | None -> 
               match (TwoOrbit.getTwoOrbitType twoOrbitPair.Order twoOrbitPair.FirstOrbit) with
               | twoOrbitType.Ortho -> Some twoOrbitPairType.Ortho
               | twoOrbitType.Para -> Some twoOrbitPairType.Para
               | twoOrbitType.SelfRefl -> Some twoOrbitPairType.SelfRefl
        | Some second ->
          match (TwoOrbit.getTwoOrbitType twoOrbitPair.Order twoOrbitPair.FirstOrbit) with
            | twoOrbitType.Ortho ->
                match (TwoOrbit.getTwoOrbitType twoOrbitPair.Order second) with
                | twoOrbitType.Ortho -> Some twoOrbitPairType.Ortho 
                | _ -> None
            | twoOrbitType.Para ->
                match (TwoOrbit.getTwoOrbitType twoOrbitPair.Order second) with
                | twoOrbitType.Para -> Some twoOrbitPairType.Para 
                | _ -> None
            | twoOrbitType.SelfRefl ->
                match (TwoOrbit.getTwoOrbitType twoOrbitPair.Order second) with
                | twoOrbitType.SelfRefl -> Some twoOrbitPairType.SelfRefl 
                | _ -> None


    let fromTwoOrbits (order:int) (twoOrbits:seq<twoOrbit>) : seq<twoOrbitPair> =
        twoOrbits |> CollectionUtils.pairWithNext 
                  |> Seq.map(fun (to1, to2) -> twoOrbitPair.create order to1 to2)

    
    let makeTwoOrbits (twoOrbitPairs:seq<twoOrbitPair>) : seq<twoOrbit> =
        seq {
            for top in twoOrbitPairs do
                yield top.FirstOrbit
                if (top.SecondOrbit.IsSome) then
                    yield top.SecondOrbit.Value
        }


    // creates a TwoOrbitPair from a TwoOrbit by reflection
    let unfoldTwoOrbitIntoTwoOrbitPair
            (toOb:twoOrbit) 
            (order:int) 
            (twoOrbitPairType:twoOrbitPairType) : twoOrbitPair =

        let (low, high) = toOb.IndicesTuple
        let (highR, lowR) = (TwoOrbit.getReflection (order*2) toOb).IndicesTuple
        let (twoOrbitA, twoOrbitB) = 
            match twoOrbitPairType with
            | twoOrbitPairType.Ortho -> (twoOrbit.create [low; high], twoOrbit.create [lowR; highR])
            | twoOrbitPairType.Para -> (twoOrbit.create [low; highR], twoOrbit.create [high; lowR])
            | twoOrbitPairType.SelfRefl -> (twoOrbit.create [low; lowR], twoOrbit.create [high; highR])

        twoOrbitPair.create (order*2) twoOrbitA (Some twoOrbitB)
