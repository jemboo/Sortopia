namespace Sortopia.Permutations

open Sortopia.Core

open Combinatorics

/// The seven reflection symmetric order 6 TwoCycles.
type twoOrbitTripleType =
    | Ortho1
    | Ortho2
    | Para1
    | Para2
    | Para3
    | Para4
    | SelfRefl


module TwoOrbitTripleType =

    /// 
    let getTwoOrbits (seedType:twoOrbitTripleType) : twoOrbit list =
        match seedType with
        | Ortho1 -> [twoOrbit.create [0; 1]; twoOrbit.create [4; 5]; twoOrbit.create [2; 3]]
        | Ortho2 -> [twoOrbit.create [0; 2]; twoOrbit.create [3; 5]; twoOrbit.create [1; 4]]
        | Para1 -> [twoOrbit.create [0; 3]; twoOrbit.create [2; 5]; twoOrbit.create [1; 4]]
        | Para2 -> [twoOrbit.create [0; 4]; twoOrbit.create [1; 5]; twoOrbit.create [2; 3]]
        | Para3 -> [twoOrbit.create [1; 3]; twoOrbit.create [2; 4]; twoOrbit.create [0; 5]]
        | Para4 -> [twoOrbit.create [0; 3]; twoOrbit.create [1; 4]; twoOrbit.create [2; 5]]
        | twoOrbitTripleType.SelfRefl -> [twoOrbit.create [0; 5]; twoOrbit.create [1; 4]; twoOrbit.create [2; 3]]
