namespace Sortopia.Permutations

open Sortopia.Core

// Perm_Rs type: a permutation that is its own inverse (p ∘ p = identity)
// Is reflection symmetric, and has an order that is divisible by 4.
type permRs = private PermRs of permSi with
    // Static method to create a Perm_Rs, validating self-inverse property
    static member create (arr: int array) : permRs =
        if arr.Length < 4 then
            failwith "Perm_Rs order must be at least 4"
        if arr.Length % 2 <> 0 then
            failwith "Perm_Rs order must be divisible by 2"
        let perm_Si = permSi.create arr
        if not (PermSi.isReflectionSymmetric perm_Si) then
            failwith "Invalid Perm_Rs: permutation must be self-inverse"
        PermRs perm_Si

    // Static method to create a Perm_Rs, validating self-inverse property
    static member createUnsafe (arr: int array) : permRs =
        let perm = permSi.createUnsafe arr
        PermRs perm

    // Property to access the underlying permutation
    member this.Permutation
        with get () =
            match this with
            | PermRs perm -> perm.Permutation

    // Property to access the underlying PermSi
    member this.PermSi
        with get () =
        match this with
        | PermRs perm_rs -> perm_rs

    member this.Id
        with get () =
            match this with
            | PermRs perm -> perm.Id

    member this.Order
        with get () =
            match this with
            | PermRs perm -> perm.Order
             
    member this.Array
        with get () =
            match this with
            | PermRs perm -> perm.Array

    member this.equals (other: permRs) : bool =
            this.Permutation.equals(other.Permutation)
