namespace Sortopia.Models.Sorting.Simple.Uf4

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Models.Sorting

[<Struct; CustomEquality; NoComparison>]
type msuf4RandMutate =
    private
        {
          id : Guid<sorterModelMutatorId>
          rngFactory: rngFactory
          uf4MutationRates: uf4MutationRates
          mutVariant: mutatorVariant
        }
    with
    static member create
            (rngFactory: rngFactory)
            (uf4MutationRates: uf4MutationRates)
            (mutVariant: mutatorVariant) : msuf4RandMutate =

        let id =
            [
                box "Sortopia.Models.Sorting/msuf4RandMutate/v1"
                box (MutatorVariant.toString mutVariant)
                box rngFactory
                box uf4MutationRates
            ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelMutatorId>

        {
            id = id
            rngFactory = rngFactory
            uf4MutationRates = uf4MutationRates
            mutVariant = mutVariant
        }

    member this.Id with get () = this.id
    member this.MutatorVariant = this.mutVariant
    member this.RngFactory with get () = this.rngFactory
    member this.Uf4MutationRates with get () = this.uf4MutationRates

    override this.Equals(obj) =
        match obj with
        | :? msuf4RandMutate as other -> this.Id = other.Id
        | _ -> false

    override this.GetHashCode() =
        hash (this.Id)

    interface IEquatable<msuf4RandMutate> with
        member this.Equals(other) = this.Id = other.Id

    member this.MakeSorterModelId
                (parent: msuf4)
                (index: int<mutationIndex>)
                (modd: int<mutationMod>) : Guid<sorterModelId> =
        CommonMutator.makeSorterModelId parent.Id this.Id index modd

    member this.MakeSorterModelFromId
                        (parent: msuf4)
                        (id: Guid<sorterModelId>) : msuf4 =
        let rng = this.RngFactory.Create %id

        // Pull values out of 'this' into local variables to avoid capturing the struct byref
        let parentUf4mutationRates = this.Uf4MutationRates

        let mutTwoOrbitUf4s =
            match this.MutatorVariant with
            | mutatorVariant.V1
            | mutatorVariant.V2 ->
                parent.TwoOrbitUnfolder4s
                |> Array.map (fun unfolder ->
                    RandomUnfolderOps4.mutateTwoOrbitUf4 rng.NextFloat parentUf4mutationRates unfolder)
            | mutatorVariant.V3 ->
                failwith "V3 mutator variant not implemented for msuf4RandMutate"

        msuf4.create id parent.ChannelCount mutTwoOrbitUf4s

    member this.MakeSorterModelFromIndexAndMod
                        (parent: msuf4)
                        (index: int<mutationIndex>)
                        (modd: int<mutationMod>) : msuf4 =
        let id = this.MakeSorterModelId parent index modd
        this.MakeSorterModelFromId parent id