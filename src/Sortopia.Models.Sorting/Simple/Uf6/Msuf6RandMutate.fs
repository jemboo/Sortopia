namespace Sortopia.Models.Sorting.Simple.Uf6

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Models.Sorting

[<Struct; CustomEquality; NoComparison>]
type msuf6RandMutate =
    private
        {
          id : Guid<sorterModelMutatorId>
          rngFactory: rngFactory
          uf6MutationRates: uf6MutationRates
          mutVariant: mutatorVariant
        }
    with
    static member create
            (rngFactory: rngFactory)
            (uf6MutationRates: uf6MutationRates)
            (mutVariant: mutatorVariant) : msuf6RandMutate =

        let id =
            [
                box "Sortopia.Models.Sorting/msuf6RandMutate/v1"
                box (MutatorVariant.toString mutVariant)
                box rngFactory
                box uf6MutationRates
            ] |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelMutatorId>

        {
            id = id
            rngFactory = rngFactory
            uf6MutationRates = uf6MutationRates
            mutVariant = mutVariant
        }

    member this.Id with get () = this.id
    member this.MutatorVariant with get() = this.mutVariant
    member this.RngFactory with get () = this.rngFactory
    member this.Uf6MutationRates with get () = this.uf6MutationRates

    override this.Equals(obj) =
        match obj with
        | :? msuf6RandMutate as other -> this.Id = other.Id
        | _ -> false

    override this.GetHashCode() =
        hash (this.Id)

    interface IEquatable<msuf6RandMutate> with
        member this.Equals(other) = this.Id = other.Id

    member this.MakeSorterModelId
                (parent: msuf6)
                (index: int<mutationIndex>)
                (modd: int<mutationMod>) : Guid<sorterModelId> =
        CommonMutator.makeSorterModelId parent.Id this.Id index modd

    member this.MakeSorterModelFromId
                (parent: msuf6)
                (id: Guid<sorterModelId>) : msuf6 =
        let rng = this.RngFactory.Create %id

        let unfolderArray = parent.TwoOrbitUnfolder6s
        let rates = this.uf6MutationRates

        let mutatedUnfolders =
            unfolderArray
            |> Array.map (fun unfolder ->
                RandomUnfolderOps6.mutateTwoOrbitUf6 rng.NextFloat rates unfolder)

        msuf6.create id parent.ChannelCount mutatedUnfolders

    member this.MakeSorterModelFromIndexAndMod
                        (parent: msuf6)
                        (index: int<mutationIndex>)
                        (modd: int<mutationMod>) : msuf6 =
        let id = this.MakeSorterModelId parent index modd
        id |> this.MakeSorterModelFromId parent


module Msuf6RandMutate =

    let toString (msuf6RandMutate: msuf6RandMutate) : string =
        let rates = msuf6RandMutate.Uf6MutationRates
        // Simplified reporting since we are using uniform rates
        let seedStr = sprintf "Seed6(O1:%.2f, P1:%.2f)"
                        rates.Seed6TransitionRates.Ortho1Rates.Ortho2Rate
                        rates.Seed6TransitionRates.Para1Rates.Ortho1Rate

        sprintf "Msuf6RandMutate(RngType=%A, MutationRates=%s)"
                msuf6RandMutate.RngFactory
                seedStr