namespace Sortopia.Models.Inputs

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Inputs


[<Struct; CustomEquality; NoComparison>]
type msasO =
    private
        { id: Guid<sorterTestModelID>
          sortableIntArrays : sortableIntArray array
          seedPermutation : permutation
          maxOrbit: int; }

    static member create
            (seedPermutation : permutation)
            (maxOrbit : int )
            : msasO =
            if maxOrbit < 1 then invalidArg "maxOrbit" "Orbit limit must be positive."
            let sias = seedPermutation |> SortableIntArray.getOrbit maxOrbit |> Seq.toArray
            let id =
                [
                    "MsasO" :> obj
                    seedPermutation :> obj
                    maxOrbit :> obj
                ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterTestModelID>

            { id = id; seedPermutation = seedPermutation; maxOrbit = maxOrbit; sortableIntArrays = sias}

    member this.Id with get() = this.id
    member this.MaxOrbit with get() = this.maxOrbit
    member this.SeedPermutation with get() = this.seedPermutation
    member this.ChannelCount with get() = (%this.seedPermutation.Order |> UMX.tag<channelCount>)

    override this.Equals(obj) =
        match obj with
        | :? msasO as other ->
            this.id = other.id
        | _ -> false

    override this.GetHashCode() =
        hash (this.id, this.seedPermutation, this.maxOrbit)

    interface IEquatable<msasO> with
        member this.Equals(other) =
            this.id = other.id

    member this.MakeSortableBoolTest
            (sorterTestId: Guid<sortableTestsId>) : sortableBinaryTests =
        let bArrays =
            this.sortableIntArrays
            |> Array.map(fun sia -> sia.ToSortableBoolArrays())
            |> Array.collect id
            |> Array.distinct
        sortableBinaryTests.create
                sorterTestId
                this.ChannelCount
                bArrays

    member this.MakeSortableIntTest
            (sorterTestId: Guid<sortableTestsId>) : sortableIntTests =
        sortableIntTests.create
                sorterTestId
                this.ChannelCount
                this.sortableIntArrays


module MsasO = ()

