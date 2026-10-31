namespace Sortopia.Permutations

open Sortopia.Core

type twoOrbitType =
    | Ortho
    | Para
    | SelfRefl


type twoOrbitPairType =
    | Ortho
    | Para
    | SelfRefl

/// Represents a pair of distinct, non-negative indices in canonical order (smaller index first).
[<CustomEquality; NoComparison>]
type twoOrbit = private TwoOrbit of (int * int) with
    /// Creates a TwoOrbit from a list of exactly two distinct, non-negative indices.
    /// <param name="indices">A list of exactly two distinct, non-negative integers.</param>
    /// <exception cref="System.ArgumentException">Thrown when the list doesn't contain exactly two indices, indices are negative, or indices are equal.</exception>
    static member create (indices: int list) : twoOrbit =
        if indices.Length <> 2 then
            failwith "TwoOrbit must contain exactly 2 indices"
        if indices |> List.exists (fun i -> i < 0) then
            failwith "TwoOrbit indices must be non-negative"
        match indices with
        | [a; b] when a = b -> failwith "TwoOrbit indices must be distinct"
        | [a; b] -> TwoOrbit (min a b, max a b)
        | _ -> failwith "Invalid TwoOrbit indices"

    member this.First with get() =
        match this with
        | TwoOrbit (a, b) -> a

    member this.Second with get() =
        match this with
        | TwoOrbit (a, b) -> b

    /// Gets the indices as a list.
    member this.Indices =
        match this with
        | TwoOrbit (a, b) -> [a; b]

    /// Gets the indices as a tuple.
    member this.IndicesTuple =
        match this with
        | TwoOrbit tuple -> tuple

    /// Converts the TwoOrbit to an Orbit.
    member this.ToOrbit =
        match this with
        | TwoOrbit (a, b) -> orbit.create [a; b]

    /// Checks if this TwoOrbit is disjoint from another.
    member this.IsDisjoint (other: twoOrbit) =
        Set.ofList this.Indices |> Set.intersect (Set.ofList other.Indices) |> Set.isEmpty

    /// Determines whether this instance equals another TwoOrbit.
    override this.Equals (obj: obj) =
        match obj with
        | :? twoOrbit as other -> this.IndicesTuple = other.IndicesTuple
        | _ -> false

    /// Computes the hash code for this instance.
    override this.GetHashCode () =
        match this with
        | TwoOrbit tuple -> hash tuple

    /// Returns a string representation of the TwoOrbit.
    override this.ToString () =
        match this with
        | TwoOrbit (a, b) -> sprintf "TwoOrbit(%d, %d)" a b


/// Operations on TwoOrbit types.
module TwoOrbit =
    /// Returns the reflection of the TwoOrbit about (order - 1) / 2.
    /// <param name="order">The order (must be non-negative and even).</param>
    /// <param name="orbit">The TwoOrbit to reflect.</param>
    /// <exception cref="System.ArgumentException">Thrown when order is negative or odd.</exception>
    let getReflection (order: int) (orbit: twoOrbit) : twoOrbit =
        if order < 0 then
            failwith "Order must be non-negative"
        if order % 2 <> 0 then
            failwith "Order must be even"
        let reflectedIndices = orbit.Indices |> List.map (fun i -> order - 1 - i)
        twoOrbit.create reflectedIndices

    /// Checks if the TwoOrbit is equal to its reflection.
    /// <param name="order">The order (must be non-negative and even).</param>
    /// <param name="orbit">The TwoOrbit to check.</param>
    let isReflectionSymmetric (order: int) (orbit: twoOrbit) : bool =
        let reflected = getReflection order orbit
        orbit.Equals(reflected)

        
    /// Gets the TwoOrbitType.
    /// <param name="order">The order (must be non-negative and even).</param>
    /// <param name="orbit">The TwoOrbit to check.</param>
    let getTwoOrbitType (order: int) (orbit: twoOrbit) : twoOrbitType =
        if (isReflectionSymmetric order orbit) then
            twoOrbitType.SelfRefl
        else if snd orbit.IndicesTuple >= order / 2 then
            if (fst orbit.IndicesTuple >= order / 2) then
                twoOrbitType.Ortho
            else
                twoOrbitType.Para
        else
            twoOrbitType.Ortho



    let getCannonicalTwoOrbits (topType:twoOrbitPairType) : twoOrbit list =
        match topType with
        | twoOrbitPairType.Ortho -> [twoOrbit.create [0; 1]; twoOrbit.create [2; 3]]
        | twoOrbitPairType.Para -> [twoOrbit.create [0; 2]; twoOrbit.create [1; 3]]
        | twoOrbitPairType.SelfRefl -> [twoOrbit.create [0; 3]; twoOrbit.create [1; 2]]
