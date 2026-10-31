namespace Sortopia.Permutations

open Sortopia.Core


/// Represents a pair of TwoOrbit instances (second optional) with an associated order.
/// The order must be non-negative and even, and the TwoOrbits must be disjoint if both are present.
/// The Second of the pair has a higher first index than the First if both are present.
[<CustomEquality; NoComparison>]
type twoOrbitPair = private { First: twoOrbit; Second: twoOrbit option; order: int } with
    /// Creates a TwoOrbitPair with a required first TwoOrbit, an optional second TwoOrbit, and an order.
    /// <param name="first">The first TwoOrbit (required).</param>
    /// <param name="second">The second TwoOrbit (optional).</param>
    /// <param name="order">The order (must be non-negative and even).</param>
    /// <exception cref="System.ArgumentException">Thrown when order is negative, odd, or the TwoOrbits share indices.</exception>
    static member create (order: int) (first: twoOrbit) (second: twoOrbit option) : twoOrbitPair =
        if order < 0 then
            failwith "TwoOrbitPair order must be non-negative"
        if order % 2 <> 0 then
            failwith "TwoOrbitPair order must be even"
        // Validate indices are within bounds (0 to order-1)
        let firstIndices = first.Indices
        if firstIndices |> List.exists (fun i -> i >= order) then
            failwith "First TwoOrbit indices must be within order bounds"
        match second with
        | Some s ->
            let secondIndices = s.Indices
            if secondIndices |> List.exists (fun i -> i >= order) then
                failwith "Second TwoOrbit indices must be within order bounds"
            if not (first.IsDisjoint s) then
                failwith "TwoOrbitPair TwoOrbits must be disjoint"

            if (first.First < s.First) then
                { First = first; Second = Some s; order = order }
            else
                { First = s; Second = Some first; order = order }

        | None -> { First = first; Second = None; order = order }

    /// Gets the first TwoOrbit.
    member this.FirstOrbit with get () = this.First

    /// Gets the second TwoOrbit (if present).
    member this.SecondOrbit with get () = this.Second

    /// Gets the order.
    member this.Order with get () = this.order

    /// Checks if the TwoOrbits are disjoint (returns true if second is None).
    member this.IsDisjoint =
        match this.Second with
        | Some second -> this.First.IsDisjoint second
        | None -> true

    /// Determines whether this instance equals another TwoOrbitPair.
    override this.Equals(obj: obj) =
        match obj with
        | :? twoOrbitPair as other ->
            this.First = other.First &&
            this.Second = other.Second &&
            this.Order = other.Order
        | _ -> false

    /// Computes the hash code for this instance.
    override this.GetHashCode() =
        hash (this.GetType(), this.First, this.Second, this.Order)

    /// Returns a string representation of the TwoOrbitPair.
    override this.ToString() =
        sprintf "TwoOrbitPair(First=%A, Second=%A, Order=%d)" this.First this.Second this.Order