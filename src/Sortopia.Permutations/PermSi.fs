namespace Sortopia.Permutations

open Sortopia.Core
open FSharp.UMX
open Sortopia.Permutations.Permutation
open CollectionUtils
open Combinatorics

// permSi: a permutation that is its own inverse (p ∘ p = identity)
type permSi = private PermSi of permutation with

    // Static method to create a Perm_Rs, validating self-inverse property
    static member create (arr: int array) : permSi =
        if arr.Length < 1 then
            failwith "array must contain items"
        let perm = permutation.create arr
        if not (Permutation.isSelfInverse perm) then
            failwith "Invalid: permutation must be self-inverse"
        PermSi perm

    // Static method to create a Perm_Rs, validating self-inverse property
    static member createUnsafe (arr: int array) : permSi =
        let perm = permutation.createUnsafe arr
        PermSi perm

    // Property to access the underlying permutation
    member this.Permutation =
        match this with
        | PermSi perm -> perm

    member this.Id
        with get () =
            match this with
            | PermSi perm -> perm.Id

    member this.Order
        with get () =
            match this with
            | PermSi perm -> perm.Order

    member this.Array
        with get () =
            match this with
            | PermSi perm -> perm.Array

    member this.equals (other: permSi) : bool =
            this.Permutation.equals(other.Permutation)


module PermSi =

    type MutationMode =
        | Ortho
        | Para
        | NoAction

    /// The returned TwoOrbits are ordered ascending by their first index.
    /// <param name="perm_Si">The PermSi to convert.</param>
    /// <exception cref="System.ArgumentException">Thrown when the PermSi order is odd, or the array is invalid.</exception>
    let getTwoOrbits (perm_Si: permSi) : twoOrbit array =
        let order = perm_Si.Order |> UMX.untag
        if order < 0 || order % 2 <> 0 then
            failwith "PermSi order must be non-negative and even"
        let visited = Array.create order false
        let rec findOrbits i acc =
            if i >= order then acc
            elif visited.[i] then findOrbits (i + 1) acc
            else
                visited.[i] <- true
                let j = perm_Si.Permutation.permute i
                if j < 0 || j >= order then
                    failwith "PermSi array contains invalid indices"
                visited.[j] <- true
                let twoOrbit = twoOrbit.create [i; j]
                findOrbits (i + 1) (twoOrbit :: acc)
        findOrbits 0 [] |> List.rev |> Array.ofList


    // Create a PermSi from a list of disjoint transpositions
    let fromTranspositions 
                (n: int) 
                (transpositions: List<int * int>) : permSi =
        if n < 0 then
            failwith "PermSi order must not be negative"
        let arr = Array.init n id
        let mutable seen = Set.empty
        for (i, j) in transpositions do
            if i < 0 || i >= n || j < 0 || j >= n || i = j then
                failwith "Invalid transposition: must be distinct indices in [0, n-1]"
            if Set.contains i seen || Set.contains j seen then
                failwith "Transpositions must be disjoint"
            seen <- seen |> Set.add i |> Set.add j
            arr.[i] <- j
            arr.[j] <- i
        permSi.create arr


    // Creates a PermSi of order n with a maximum number of orbits
    let saturatedWithTwoCycles (order: int) : permSi =
        let cycleCount = order / 2
        let transpositions =
            [ for i in 0 .. (cycleCount - 1) -> (2 * i, 2 * i + 1) ]
        fromTranspositions order transpositions 


    // makeReflection returns a reflection of the given PermSi
    let makeReflection (perm_Rs: permSi) : permSi =
        let origArray = perm_Rs.Permutation.Array
        let arrayLength = origArray.Length
        let _refl pos = 
            arrayLength - pos - 1
        permSi.createUnsafe
            (Array.init arrayLength (fun dex -> origArray.[_refl dex] |> _refl))


    // isReflectionSymmetric checks if a PermSi is reflection symmetric
    let isReflectionSymmetric (perm_Rs: permSi) :bool =
        let arr = perm_Rs.Permutation.Array
        let reflArray = (makeReflection perm_Rs).Permutation.Array
        arrayEquals arr reflArray


    // Conjugate a PermSi (t) with a permutation (p) : p^-1 ∘ t ∘ p
    // This always makes a PermSi
    let conjugate (t: permSi) (p: permutation) : permSi =
        let arrT = t.Permutation.Array
        let arrP = p.Array
        if Array.length arrT <> Array.length arrP then
            failwith "Permutations must have the same order"
        let res = compose (compose (inverse p) t.Permutation) p
        permSi.createUnsafe res.Array


    // Generates a random Perm_Rs by conjugating a saturated PermSi with a random permutation
    let makeRandoms (indexShuffler: int -> int) (order: int) : permSi seq =
        seq {
            while true do
                let initialPerm = Permutation.randomPermutation indexShuffler order
                yield conjugate (saturatedWithTwoCycles order) initialPerm
        }

    // Mutates a PermSi in a minimal way based on siMutationMode
    let mutate 
            (indexPicker: int -> int) 
            (siMutationMode:MutationMode) 
            (psi: permSi) : permSi =

        // for None mode, return the permutation as is
        if (siMutationMode = MutationMode.NoAction) then
            psi
        else
            let first, second = pickAndOrderTwoDistinct indexPicker %psi.Order
            // if the two items are in the same orbit, do nothing
            if psi.Permutation.permute first = second then
                psi
            else
                let fpLow, fpHi = orderItems first (psi.Permutation.permute first)
                let spLow, spHi = orderItems second (psi.Permutation.permute second)
                let newArray = psi.Array
                match siMutationMode with
                | MutationMode.Ortho -> 
                   newArray.[fpLow] <- spLow
                   newArray.[spLow] <- fpLow
                   newArray.[spHi] <- fpHi
                   newArray.[fpHi] <- spHi
                   permSi.createUnsafe newArray
                | MutationMode.Para ->
                   newArray.[fpLow] <- spHi
                   newArray.[spHi] <- fpLow
                   newArray.[fpHi] <- spLow
                   newArray.[spLow] <- fpHi
                   permSi.createUnsafe newArray
                | MutationMode.NoAction -> psi


    /// Creates a Perm_Rs from a list of TwoOrbit pairs by mutating the identity Perm
    let fromTwoOrbitPair (twoOrbitPairs : twoOrbitPair array) : permSi =
        if twoOrbitPairs.Length < 1 then
            failwith "RsOrbitPair list must have an element"

        let arr = Array.init twoOrbitPairs.[0].Order id
        for orbitPair in twoOrbitPairs do
            let a, b = orbitPair.First.IndicesTuple
            arr.[a] <- b
            arr.[b] <- a
            if (orbitPair.Second.IsSome) then
                let c, d = orbitPair.Second.Value.IndicesTuple
                arr.[c] <- d
                arr.[d] <- c

        permSi.create arr
