namespace Sortopia.Permutations

open Sortopia.Core

open System
open Combinatorics

[<CustomEquality; NoComparison>]
type orbit = private Orbit of indices: int list with
    // Static method to create an Orbit, validating input
    static member create (indices: int list) : orbit =
        // Validate non-negative indices
        if indices |> List.exists (fun i -> i < 0) then
            failwith "Orbit indices must be non-negative"
        // Validate unique indices
        let distinctIndices = indices |> List.distinct
        if List.length distinctIndices <> List.length indices then
            failwith "Orbit indices must be unique"
        // Find the smallest index and its position
        if indices.IsEmpty then
            Orbit []
        else
            let minIndex = indices |> List.min
            let minPos = indices |> List.findIndex (fun i -> i = minIndex)
            // Rotate the list so the smallest index is at position 0
            let rotated =
                indices.[minPos .. List.length indices - 1] @ indices.[0 .. minPos - 1]
            Orbit rotated

    // Property to access the orbit's indices
    member this.Indices =
        match this with
        | Orbit indices -> indices

    // Check if two orbits are equal (based on normalized indices)
    member this.Equals (other: orbit) =
        this.Indices = other.Indices

    // Override for object equality
    override this.Equals (obj: obj) =
        match obj with
        | :? orbit as other -> this.Equals other
        | _ -> false

    // Override GetHashCode for consistency with Equals
    override this.GetHashCode () =
        // Use a simple hash based on sorted indices for consistency
        this.Indices
        |> List.fold (fun acc i -> (acc * 397) ^^^ i) 0

    // Implement IEquatable<Orbit> for structural equality
    interface IEquatable<orbit> with
        member this.Equals (other: orbit) = this.Equals other

module Orbit =

    // returns a reflection of the orbit about (order - 1) / 2
    let getReflection (order: int) (orb: orbit) : orbit =
        // Reflect each index in the orbit
        let reflectedIndices = orb.Indices |> List.map (fun i -> order - 1 - i)
        // Create a new Orbit from the reflected indices
        orbit.create reflectedIndices

    let isReflectionSymmetric (order: int) (orbit: orbit) =
        (orbit.Indices.[0] |> reflect order) = (orbit.Indices.[1])
