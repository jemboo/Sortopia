namespace Sortopia.Permutations

open Sortopia.Core

open System
open System.Buffers.Binary
open FSharp.UMX
open CollectionUtils
open Combinatorics

[<Measure>] type Order

module private PermutationIdentity =
    let compute (values: int[]) =
        let bytes = Array.zeroCreate<byte> (values.Length * sizeof<int>)
        for i = 0 to values.Length - 1 do
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * sizeof<int>), values.[i])
        GuidUtils.guidFromBytes bytes

[<Struct; CustomEquality; CustomComparison>]
type permutation =
    private Perm of arr: int array * id: Guid
    with
    static member create (arr: int[]) =
        let values = Array.copy arr
        if values.Length = 0 then failwith "array cannot be empty"
        if Array.sort values <> [|0 .. values.Length - 1|] then
            failwith "Invalid permutation: must contain each integer from 0 to n-1 exactly once"
        Perm (values, PermutationIdentity.compute values)

    /// Skip validation while still taking a snapshot of the input.
    static member createUnsafe (arr: int[]) =
        let values = Array.copy arr
        Perm (values, PermutationIdentity.compute values)

    member private this.Values = match this with Perm (values, _) -> values
    /// Returns an independent, mutable copy of the permutation.
    member this.Array = Array.copy this.Values
    member this.Id = match this with Perm (_, id) -> id
    member this.Order = UMX.tag<Order> this.Values.Length
    member this.permute (index: int) = this.Values.[index]

    member this.equals (other: permutation) =
        this.Values.AsSpan().SequenceEqual(other.Values)

    override this.Equals(obj) =
        match obj with
        | :? permutation as other -> this.equals other
        | _ -> false

    override this.GetHashCode() = this.Id.GetHashCode()

    interface IComparable<permutation> with
        member this.CompareTo(other) =
            let byLength = compare this.Values.Length other.Values.Length
            if byLength <> 0 then byLength
            else this.Values.AsSpan().SequenceCompareTo(other.Values)

    interface IComparable with
        member this.CompareTo(obj) =
            match obj with
            | :? permutation as other -> 
                (this :> IComparable<permutation>).CompareTo(other)
            | _ -> 
                invalidArg "obj" "Argument must be of type permutation"

    interface IStableSerializable with
        member this.WriteStableBytes (writer: System.IO.BinaryWriter) =
            writer.Write(this.Id.ToByteArray())

module Permutation =
    
    // All callers of this helper transfer a freshly allocated array.
    let private fromOwnedArray values = Perm (values, PermutationIdentity.compute values)

    let private identityId order = Array.init order id |> PermutationIdentity.compute

    let private identityIds = Array.init 129 identityId

    let getIdentityId order =
        if order >= 0 && order < identityIds.Length then identityIds.[order]
        else identityId order

    let identity (order: int) : permutation =
        if order <= 0 then failwith "Permutation order must be positive"
        Perm (Array.init order id, getIdentityId order)

    let isIdentity (perm: permutation) : bool =
        let (Perm (values, _)) = perm
        let mutable i = 0
        while i < values.Length && values.[i] = i do
            i <- i + 1
        i = values.Length

    // --- Core Operations ---

    let compose (p1: permutation) (p2: permutation) : permutation =
        let (Perm (arr1, _)) = p1
        let (Perm (arr2, _)) = p2
        if arr1.Length <> arr2.Length then failwith "Orders must match"
        fromOwnedArray (Array.init arr1.Length (fun i -> arr2.[arr1.[i]]))

    let inverse (perm: permutation) : permutation =
        let (Perm (arr, _)) = perm
        let n = arr.Length
        let inv = Array.zeroCreate n
        for i in 0 .. n-1 do
            inv.[arr.[i]] <- i
        fromOwnedArray inv

    let conjugate (p: permutation) (q: permutation) : permutation =
        compose (compose (inverse q) p) q

    // --- Sequence & Analysis ---

    let powerSequence (perm: permutation) : permutation seq =
        seq {
            let mutable cur = perm
            while true do
                yield cur
                cur <- compose perm cur
        }
        
    let isSelfInverse (perm: permutation) : bool =
        let (Perm (values, _)) = perm
        let mutable i = 0
        while i < values.Length && values.[values.[i]] = i do
            i <- i + 1
        i = values.Length


    // --- Visualization & Orbits ---


    // returns a random Permutation by shuffling the identity Permutation
    let randomPermutation (indexShuffler: int -> int) (order: int) : permutation =
        let initialPerm = identity order
        let shuffled = fisherYatesShuffle indexShuffler initialPerm.Array
                       |> Seq.toArray
        fromOwnedArray shuffled

    // Returns a "rotated" identity permutation of given by k positions (positive k for left, negative for right)
    let rotated (k: int) (order:int) : permutation =
        let arr = (identity order).Array
        if order <= 0 then
            failwith "Permutation order must be positive"
        let k = k % order // Normalize k to avoid unnecessary cycles
        let k = if k < 0 then k + order else k // Handle negative k
        fromOwnedArray (Array.init order (fun i -> arr.[(i + k + order) % order]))
