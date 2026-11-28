namespace Sortopia.Permutations

open Sortopia.Core

open TwoOrbitPairOps

/// Specifies the transformation of sequences of TwoOrbitPairs to sequences of TwoOrbitPairs.
[<CustomEquality; NoComparison>]
type twoOrbitUfStep = private { twoOrbitPairTypes: twoOrbitPairType array; order: int } with
    /// The TwoOrbitUfStep specifies the transformation of a group of TwoOrbits into another
    /// group of TwoOrbits (of twice the size and twice the order), via reflection and further
    /// modification according to the list of TwoOrbitTypes
    /// <param name="twoOrbitTypes">The list of TwoOrbitType values (must be non-empty).</param>
    /// <param name="order">The order TwoOrbits the Perm_RsUnfolderStep acts upon.</param>
    /// <exception cref="System.ArgumentException">Thrown when order is negative or the type types is empty.</exception>
    static member create (twoOrbitPairTypes: twoOrbitPairType array) (order: int) : twoOrbitUfStep =
        if isNull twoOrbitPairTypes then nullArg "twoOrbitPairTypes"
        if order < 4 then
            failwith "TwoOrbitUfStep order must be at least 4"
        if order % 2 <> 0 then
            failwith "TwoOrbitUfStep order must be even"
        if Array.length twoOrbitPairTypes <> order / 2 then
            failwith $"TwoOrbitUfStep twoOrbitTypes length must be order / 2 ({order / 2})"
        { twoOrbitPairTypes = Array.copy twoOrbitPairTypes; order = order }

    /// Determines whether this instance equals another Perm_RsUnfolderStep.
    override this.Equals (obj: obj) =
        match obj with
        | :? twoOrbitUfStep as other -> this.twoOrbitPairTypes = other.twoOrbitPairTypes && this.Order = other.Order
        | _ -> false

    member this.Order
        with get () = this.order

    member this.TwoOrbitPairTypes
        with get () = Array.copy this.twoOrbitPairTypes

    /// Internal mutation keeps immutable storage when no transition changes a value.
    /// Every transition still runs, preserving the random stream for later stages.
    member internal this.MapTypes(mapping: twoOrbitPairType -> twoOrbitPairType) =
        let mutable changed = null
        for index in 0 .. this.twoOrbitPairTypes.Length - 1 do
            let value = mapping this.twoOrbitPairTypes[index]
            if value <> this.twoOrbitPairTypes[index] then
                if isNull changed then changed <- Array.copy this.twoOrbitPairTypes
                changed[index] <- value
        if isNull changed then this
        else { twoOrbitPairTypes = changed; order = this.order }

    /// Computes the hash code for this instance.
    override this.GetHashCode () =
        hash (this.GetType(), this.twoOrbitPairTypes, this.Order)

    /// Returns a string representation of the TwoOrbitUnfol der.
    override this.ToString () =
        sprintf "TwoOrbitUfStep(TwoOrbitTypes=%A, Order=%d)" this.twoOrbitPairTypes this.Order


module TwoOrbitUfStep =


    /// creates an ordered list of TwoOrbitPairs, equal in length to 
    /// twoOrbitUnfolder.TwoOrbitTypes, and twice the order of the given Perm_RsUnfolderStep.
    let unfoldTwoOrbitPairs
        (twoOrbitUfStep:twoOrbitUfStep) 
        (twoOrbitPairs:twoOrbitPair array) : twoOrbitPair array =

        let twoOrbits = twoOrbitPairs |> TwoOrbitPairOps.makeTwoOrbits |> Seq.toArray
        Array.map2 (fun orbit kind ->
            unfoldTwoOrbitIntoTwoOrbitPair orbit twoOrbitUfStep.Order kind)
            twoOrbits twoOrbitUfStep.twoOrbitPairTypes
